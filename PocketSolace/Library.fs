namespace PocketSolace

open System
open System.Threading.Tasks

open Microsoft.Extensions.Logging
open SolaceSystems.Solclient.Messaging

type ISolace =
    abstract Subscribe : string -> Task

    // TODO
    // abstract Unsubscribe : string -> Task
    //
    // abstract CreateMessage : string -> IMessage
    // abstract Send : IMessage -> Task

    abstract TerminationReason : Task
    
    /// Set after session and context are disposed.
    abstract Terminated : Task
    
    inherit IAsyncDisposable

module Solace =
    let private ofSolLogLevel = function
        | SolLogLevel.Critical -> LogLevel.Critical 
        | SolLogLevel.Error -> LogLevel.Error
        | SolLogLevel.Warning -> LogLevel.Warning
        | SolLogLevel.Notice | SolLogLevel.Info -> LogLevel.Information
        | SolLogLevel.Debug -> LogLevel.Debug
        | _ -> LogLevel.None

    let initGlobalContextFactory logLevel (logger : ILogger<_>) =
        let contextFactoryProperties = ContextFactoryProperties()
        contextFactoryProperties.SolClientLogLevel <- logLevel
        contextFactoryProperties.LogDelegate <- fun e ->
            logger.Log(ofSolLogLevel e.LogLevel, e.LogException, e.LogMessage)
        ContextFactory.Instance.Init(contextFactoryProperties)
