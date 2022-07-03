open Microsoft.Extensions.Logging
open SolaceSystems.Solclient.Messaging

open PocketSolace

[<EntryPoint>]
let main _ =
    let loggerFactory = LoggerFactory.Create(fun l -> l.AddConsole().SetMinimumLevel(LogLevel.Information) |> ignore)
    let logger = loggerFactory.CreateLogger<unit>()

    Solace.initGlobalContextFactory SolLogLevel.Debug logger
    0
