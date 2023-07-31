namespace PocketSolace

open System

type IncomingMetadata =
    { SenderTimestamp : DateTimeOffset

      // Indicates that one or more of the previous messages were discarded by message broker.
      // The reason could be that the client is too slow so broker must discard some messages.
      BrokerDiscardIndication : bool
    }

// NOTE: We removed `ReplyTo` for two reasons:
//       - It seems unsecure to create programs which blindly respond to `ReplyTo` topic
//         without further verifying this topic. Especially when Solace ACL Profile can't enforce
//         any restrictions on `ReplyTo` header.
//
//         This means that malicious service can send special `ReplyTo` header
//         forcing other service to send replies to unintended destinations, disrupting the system.
//
//       - Official clients from Solace company don't support direct manipulation with `ReplyTo` header.
//         This means that protocol created with the help of PocketSolace library
//         would be unusable from other languages.
//
//       Better alternative to `ReplyTo` header is to directly derive reply to topic
//       from the topic where the request arrived. The advantage of this approach is that
//       Solace ACL Profile can enforce specific topic where specific client username can publish.
//
//       For example we can configure ACL Profile where all publishing is disallowed except
//       publishing to `exchange/trade/db/command/*/$client-username`.
//       Assigning this ACL profile to client usernames `john` and `tim` will
//       ensure that `john` can only publish to topics `exchange/trade/db/command/*/john`
//       and `tim` can only publish to topics `exchange/trade/db/command/*/tim`
//       (we say plural topics because as expected the star acts as wildcard).
//
//       With the above ACL we can easily derive reply to topic from the topic where the request arrived
//       by replacing the prefix `exchange/trade/db/command/` by `exchange/trade/db/reply/`.
type RawMessage =
    { Topic : string

      // Metadata.
      ContentType : string option
      ContentEncoding : string option
      CorrelationId : string option
      SenderId : string option

      Payload : byte[]
    }
