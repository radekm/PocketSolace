namespace PocketSolace

open System

type IncomingMetadata =
    { SenderTimestamp : DateTimeOffset

      // Indicates that one or more of the previous messages were discarded by message broker.
      // The reason could be that the client is too slow so broker must discard some messages.
      BrokerDiscardIndication : bool
    }

type RawMessage =
    { // Topics.
      Topic : string
      ReplyTo : string option

      // Metadata.
      ContentType : string option
      ContentEncoding : string option
      CorrelationId : string option
      SenderId : string option

      Payload : byte[]
    }
