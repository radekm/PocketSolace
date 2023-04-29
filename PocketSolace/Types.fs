namespace PocketSolace

open System

type IncomingMetadata =
    { SenderTimestamp : DateTimeOffset

      // Indicates that one or more of the previous messages were discarded by message broker.
      // The reason could be that the client is too slow so broker must discard some messages.
      BrokerDiscardIndication : bool
    }

/// `'T` should use structural equality.
type Message<'T> =
    { // Topics.
      Topic : string
      ReplyTo : string option

      // Metadata.
      ContentType : string option
      ContentEncoding : string option
      CorrelationId : string option
      SenderId : string option

      Payload : 'T
    }

type RawMessage = Message<byte[]>
