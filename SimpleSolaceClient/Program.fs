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

    Solace.initGlobalContextFactory SolLogLevel.Notice solaceClientLogger

    let channel = Channel.CreateBounded<IMessage>(512)

    let channelTask = backgroundTask {
        let mutable stop = false
        let mutable n = 0
        while not stop do
            match! channel.Reader.WaitToReadAsync() with
            | false -> stop <- true
            | true ->
                use! _message = channel.Reader.ReadAsync()
                n <- n + 1
        Console.WriteLine("{0} messages received", n)
    }

    let sessionTask = backgroundTask {
        use! solace = Solace.connect pocketSolaceLogger props channel.Writer
        do! solace.Subscribe(testTopic)

        let _sendTask = backgroundTask {
            try
                for i in 1 .. 500_000 do
                    use topic = solace.CreateTopic(testTopic)
                    use msg = solace.CreateMessage()
                    msg.Destination <- topic
                    msg.BinaryAttachment <- System.Text.Encoding.UTF8.GetBytes $"Message %d{i}"
                    do! solace.Send(msg)

                    if i % 10_000 = 0 then
                        Console.WriteLine("{0} messages sent", i)
            with e -> Console.WriteLine("Sending failed with {0}", e)
        }

        // Stay connected for 20 seconds.
        do! Task.Delay(20_000)

        // Not necessary.
        do! solace.Unsubscribe(testTopic)
    }

    Task.WaitAll(channelTask, sessionTask)

    0
