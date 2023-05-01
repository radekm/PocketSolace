
# About PocketSolace

PocketSolace is an F# library designed for sending and receiving direct messages over Solace.
PocketSolace provides much simpler interface than the official package `SolaceSystems.Solclient.Messaging`.
Here are the most important advantages of PocketSolace over the official package:

- PocketSolace provides asynchronous functions which use standard .NET types like `Task`s and `ChannelReader`.
  On the other hand the official package provides asynchronous interface based on callbacks.
  User has to provide two callbacks. One for receiving messages and the other
  for handling all session events such as connection up, error while connecting, connection down or
  readiness to send more messages.
- PocketSolace uses plain string for topics and F# record type for messages.
  This simplifies library usage and unit testing.
  You can even use the interface of PocketSolace but provide custom implementation
  which doesn't use Solace.
- PocketSolace supports `ILogger` from `Microsoft.Extensions.Logging.Abstractions` out of the box,
  the official package doesn't.
- The official package is fragile. If you call certain Solace functions
  from Solace context thread (eg. from session event handler)
  then a segmentation fault may occur.

PocketSolace tries to be very simple alternative to the official package for direct messaging.
It does not support guaranteed messaging. I decided not to support
guaranteed messaging because as of July-September 2022 throughput was much lower with guaranteed messaging
than with direct messaging.

PocketSolace also doesn't support queues. Because queues are not reliable
with direct messaging: When Solace broker gets direct message it must
promote it to guaranteed message before storing it into a queue.
This promotion is an expensive operation and the broker may decide not to do it
when it's under heavy load. If it doesn't promote a message then the message
is not stored in queues but it's delivered to consumers.
This leads to a system where applications receive messages but Solace queues don't store those messages
even if they have the same subscriptions. The remedy is to use guaranteed messages but unfortunately
these were (maybe still are) too slow.

# About license

PocketSolace library license looks like the 3-clause BSD License
but it has additional conditions:

- This software must not be used for military purposes.
- Source code must not be used for training or validating AI models.
  For example AI models which generate source code.
