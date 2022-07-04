open System
open System.Threading.Channels
open System.Threading.Tasks

open Microsoft.Extensions.Logging
open SolaceSystems.Solclient.Messaging

open PocketSolace

[<EntryPoint>]
let main _ =
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

    Solace.initGlobalContextFactory SolLogLevel.Debug solaceClientLogger

    let channel = Channel.CreateBounded<IMessage>(512)

    let channelTask = backgroundTask {
        let mutable stop = false
        while not stop do
            match! channel.Reader.WaitToReadAsync() with
            | false -> stop <- true
            | true ->
                use! message = channel.Reader.ReadAsync()
                Console.WriteLine("Got message {0}", message.Destination.Name)
    }

    let sessionTask = backgroundTask {
        use! solace = Solace.connect pocketSolaceLogger props channel.Writer
        do! solace.Subscribe(testTopic)

        // Stay connected for 20 seconds.
        do! Task.Delay(20_000)
    }

    Task.WaitAll(channelTask, sessionTask)

    0
