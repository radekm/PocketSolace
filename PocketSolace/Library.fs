namespace PocketSolace

open System
open System.Threading.Tasks

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
