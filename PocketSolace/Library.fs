namespace PocketSolace

open System
open System.Threading.Channels
open System.Threading.Tasks

open Microsoft.Extensions.Logging
open SolaceSystems.Solclient.Messaging

type ISolace =
    abstract Subscribe : string -> Task
    abstract Unsubscribe : string -> Task

    abstract CreateTopic : string -> ITopic
    abstract CreateMessage : unit -> IMessage
    abstract Send : IMessage -> Task

    abstract TerminationReason : Task

    /// Set after session and context are disposed.
    abstract Terminated : Task

    inherit IAsyncDisposable

[<Sealed>]
type private CanSendSignal() =
    // It's crucial to use `TaskCreationOptions.RunContinuationsAsynchronously`.
    [<VolatileField>]
    let mutable tcs = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)

    member _.Next = tcs.Task

    member _.Signal() =
        tcs.SetResult()
        tcs <- TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)

// Implementation must ensure that Solace functions are not executed from Solace context thread.
// That means that all `TaskCompletionSource`s which are set in message handler or in session event handler
// are created with `TaskCreationOptions.RunContinuationsAsynchronously`.
type private Solace
    ( logger : ILogger,
      session : ISession,

      canSend : CanSendSignal,
      terminationReason : TaskCompletionSource,
      writer : ChannelWriter<IMessage>,

      terminated : TaskCompletionSource
    ) =

    interface ISolace with
        override _.Subscribe(pattern : string) = backgroundTask {
            use topic = ContextFactory.Instance.CreateTopic(pattern)

            // Unfortunately we use blocking `Subscribe`.
            // There's no non-blocking variant which requests confirmation
            // and works without topic dispatch.
            // See https://solace.community/discussion/1390/subscribe-to-direct-messages-with-requestconfirm-and-correlationkey-net.
            match! Task.Run(fun () -> session.Subscribe(topic, true)) with
            | ReturnCode.SOLCLIENT_OK -> ()
            | code -> failwith $"Unexpected return code from Session.Subscribe: %A{code}"
        }

        override _.Unsubscribe(pattern : string) = backgroundTask {
            use topic = ContextFactory.Instance.CreateTopic(pattern)

            // Unfortunately we use blocking `Unsubscribe`.
            // For more details see `Subscribe`.
            match! Task.Run(fun () -> session.Unsubscribe(topic, true)) with
            | ReturnCode.SOLCLIENT_OK -> ()
            | code -> failwith $"Unexpected return code from Session.Unsubscribe: %A{code}"
        }

        override _.CreateTopic(name : string) = ContextFactory.Instance.CreateTopic(name)
        override _.CreateMessage() = session.CreateMessage()
        override _.Send(msg : IMessage) = backgroundTask {
            let mutable sent = false

            while not sent do
                if terminationReason.Task.IsCompleted then
                    failwith "Solace is terminating"

                // `canSend` contains a task which will be completed after next `CanSend` session event is received.
                //
                // We must store `canSend` task before calling `session.Send`.
                // Because `CanSend` session event can be received immediately after calling `session.Send`
                // even before the call to `canSend.Next` happens. And thus task returned by `canSend.Next`
                // would be waiting for another `CanSend` which may never happen.
                let canSend = canSend.Next
                match session.Send(msg) with
                | ReturnCode.SOLCLIENT_OK -> sent <- true
                | ReturnCode.SOLCLIENT_WOULD_BLOCK ->
                    logger.LogDebug("Waiting for CanSend session event")
                    let! _ = Task.WhenAny(canSend, terminationReason.Task)
                    ()
                | code -> failwith $"Unexpected return code from Session.Send: %A{code}"
        }

        override _.TerminationReason = terminationReason.Task
        override _.Terminated = terminated.Task

        override _.DisposeAsync() =
            if terminationReason.TrySetResult() then
                logger.LogDebug("Terminating normally because of dispose")
                writer.TryComplete() |> ignore
            ValueTask terminated.Task

module Solace =
    let private ofSolLogLevel = function
        | SolLogLevel.Critical -> LogLevel.Critical
        | SolLogLevel.Error -> LogLevel.Error
        | SolLogLevel.Warning -> LogLevel.Warning
        | SolLogLevel.Notice | SolLogLevel.Info -> LogLevel.Information
        | SolLogLevel.Debug -> LogLevel.Debug
        | _ -> LogLevel.None

    let initGlobalContextFactory logLevel (logger : ILogger) =
        let contextFactoryProperties = ContextFactoryProperties()
        contextFactoryProperties.SolClientLogLevel <- logLevel
        contextFactoryProperties.LogDelegate <- fun e ->
            logger.Log(ofSolLogLevel e.LogLevel, e.LogException, e.LogMessage)
        ContextFactory.Instance.Init(contextFactoryProperties)

    /// Like defer from Zig or Go.
    let inline private defer ([<InlineIfLambda>] f : unit -> unit) =
        { new IDisposable with
            override _.Dispose() = f () }

    let private spawnSolaceProcess
        (logger : ILogger)
        (props : SessionProperties)
        (writer : ChannelWriter<IMessage>)
        (connectResult : TaskCompletionSource<ISolace>) = backgroundTask {

        use _ = defer (fun () -> logger.LogDebug("Solace process stopped"))
        logger.LogDebug("Solace process started")

        let terminationReason = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        let terminateWithException (e : exn) =
            if terminationReason.TrySetException(e) then
                logger.LogError(e, "Terminating because of error")
                writer.TryComplete(e) |> ignore

        let terminated = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)

        try
            // Session owner.
            // Lifetime of Solace context and Solace session is bound to this background task
            // called session owner. After session owner ends both Solace context and Solace session were disposed.
            let sessionOwner = backgroundTask {
                use _ = defer  (fun () -> logger.LogDebug("Solace context disposed (or never created)"))
                use context =
                    ContextFactory.Instance.CreateContext(
                        ContextProperties(),
                        fun _ e ->
                            let info = e.ErrorInfo
                            let msg =
                                sprintf "Context error: %s %d %d"
                                    info.ErrorStr info.ResponseCode info.ResponseCode
                            logger.LogError(e.Exception, msg)
                    )

                let connected = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
                let canSend = CanSendSignal()

                use _ = defer  (fun () -> logger.LogDebug("Solace session disposed (or never created)"))
                use session =
                    context.CreateSession(
                        props,
                        (fun _ args ->
                            if writer.TryWrite(args.Message) |> not then
                                args.Message.Dispose()
                                Exception "Message channel full"
                                |> terminateWithException),
                        (fun _ args ->
                            match args.Event with
                            | SessionEvent.UpNotice -> connected.SetResult()
                            | SessionEvent.ConnectFailedError ->
                                connected.SetException(Exception $"Session not connected: %s{args.Info}")
                            | SessionEvent.CanSend ->
                                logger.LogDebug("CanSend session event received")
                                canSend.Signal()
                            | SessionEvent.DownError ->
                                Exception $"Session down: %s{args.Info}"
                                |> terminateWithException
                            | _ ->
                                Exception $"Unexpected session event %A{args.Event}: %s{args.Info}"
                                |> terminateWithException)
                    )

                logger.LogDebug("Connecting Solace session")
                match session.Connect() with
                | ReturnCode.SOLCLIENT_IN_PROGRESS -> ()
                | code -> failwith $"Unexpected return code from Session.Connect: %A{code}"

                // If we don't connect successfully then awaiting `connected.Task` throws and we jump
                // out of session owner right into the exception handler where we set `connectResult`
                // to exception and call `terminateWithException`.
                do! connected.Task

                Solace(logger, session, canSend, terminationReason, writer, terminated)
                |> connectResult.SetResult

                logger.LogDebug("Solace session connected, waiting for termination")
                do! terminationReason.Task

                // Disconnect is skipped if `terminationReason` contains exception.
                logger.LogDebug("Disconnecting Solace session")
                match session.Disconnect() with
                | ReturnCode.SOLCLIENT_OK -> ()
                | code -> failwith $"Unexpected return code from Session.Disconnect: %A{code}"
            }
            do! sessionOwner

            Exception "Internal error: No connect result set after session owner terminated normally"
            |> connectResult.TrySetException
            |> ignore
            Exception "Internal error: No termination reason set after session owner terminated normally"
            |> terminateWithException
        with e ->
            logger.LogDebug(e, "Session owner terminated with exception")
            connectResult.TrySetException(e) |> ignore
            terminateWithException e

        terminated.TrySetResult() |> ignore
    }

    let createSessionProperties () =
        let props = SessionProperties()
        props.ConnectBlocking <- false
        props.SubscribeBlocking <- true
        props.SendBlocking <- false
        props.BlockWhileConnecting <- false
        props.TopicDispatch <- false
        props.ReconnectRetries <- 0
        props.IgnoreDuplicateSubscriptionError <- false
        props

    let private checkSessionProperties (props : SessionProperties) =
        if props.ConnectBlocking then
            failwith $"%s{nameof props.ConnectBlocking} must be false"
        if not props.SubscribeBlocking then
            failwith $"%s{nameof props.SubscribeBlocking} must be true"
        if props.SendBlocking then
            failwith $"%s{nameof props.SendBlocking} must be false"
        if props.BlockWhileConnecting then
            failwith $"%s{nameof props.BlockWhileConnecting} must be false"
        if props.TopicDispatch then
            failwith $"%s{nameof props.TopicDispatch} must be false"
        if props.ReconnectRetries <> 0 then
            failwith $"%s{nameof props.ReconnectRetries} must be 0"
        if props.IgnoreDuplicateSubscriptionError then
            failwith $"%s{nameof props.IgnoreDuplicateSubscriptionError} must be false"

    let connect (logger : ILogger) (props : SessionProperties) (writer : ChannelWriter<IMessage>) = backgroundTask {
        checkSessionProperties props

        let connectResult = TaskCompletionSource<ISolace>()
        let _ = spawnSolaceProcess logger props writer connectResult
        return! connectResult.Task
    }
