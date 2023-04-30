open System
open System.Threading.Tasks

open Microsoft.Extensions.Logging
open SolaceSystems.Solclient.Messaging

open PocketSolace

[<EntryPoint>]
let main _ =
    let simulateSlowConsumer = false

    let loggerFactory = LoggerFactory.Create(fun l -> l.AddConsole().SetMinimumLevel(LogLevel.Debug) |> ignore)
    let solaceClientLogger = loggerFactory.CreateLogger("Solace Client")
    let pocketSolaceLogger = loggerFactory.CreateLogger("Pocket Solace")

    let testTopic = Environment.GetEnvironmentVariable("SOLACE_TEST_TOPIC")
    if String.IsNullOrWhiteSpace testTopic then
        failwith "SOLACE_TEST_TOPIC environment variable must be non-empty"

    let props = Solace.createSessionProperties ()
    props.Host <- Environment.GetEnvironmentVariable("SOLACE_HOST")
    props.UserName <- Environment.GetEnvironmentVariable("SOLACE_USER")
    props.Password <- Environment.GetEnvironmentVariable("SOLACE_PASSWORD")
    props.VPNName <- Environment.GetEnvironmentVariable("SOLACE_VPN")

    Solace.initGlobalContextFactory SolLogLevel.Notice solaceClientLogger

    let sessionTask = backgroundTask {
        use! solace = Solace.connect pocketSolaceLogger props 512
        do! solace.Subscribe(testTopic)

        let receiveTask = backgroundTask {
            let mutable stop = false
            let mutable n = 0
            while not stop do
                match! solace.Received.WaitToReadAsync() with
                | false -> stop <- true
                | true ->
                    let _message = solace.Received.ReadAsync()

                    // If `simulateSlowConsumer` is true then the channel with received messages
                    // will quickly fill up because consumer is much slower than producer.
                    // Message handler will be unable to add received message
                    // into the channel and Solace session and Solace context will be destroyed.
                    // `TerminationReason` will be set to exception and the same exception will
                    // be used to complete the channel with received messages.
                    //
                    // In this program publisher will fail with exception immediately
                    // but consumer will run until it consumes all received messages.
                    // After consuming the last message `WaitToReadAsync` will raise the exception
                    // which was used to complete the channel.
                    if simulateSlowConsumer then
                        do! Task.Delay(50)
                    n <- n + 1

            Console.WriteLine("{0} messages received", n)
        }

        try
            for i in 1 .. 500_000 do
                let msg = { Topic = testTopic
                            ReplyTo = None
                            ContentType = None
                            ContentEncoding = None
                            CorrelationId = None
                            SenderId = None
                            Payload = System.Text.Encoding.UTF8.GetBytes $"Message %d{i}"
                          }
                do! solace.Send(msg)

                if i % 10_000 = 0 then
                    Console.WriteLine("{0} messages sent", i)
        with e -> Console.WriteLine("Sending failed with {0}", e)

        // Stay connected for 5 seconds after sending completes
        // so there's enough time to receive all messages.
        // Without this `Delay` all messages would be sent but there won't be enough
        // time to receive them.
        do! Task.Delay(5_000)

        // `DisposeAsync` marks channel with received messages as complete.
        // `receiveTask` then reaches the last message and stops.
        do! solace.DisposeAsync()
        do! receiveTask
    }

    Task.WaitAll(sessionTask)

    0
