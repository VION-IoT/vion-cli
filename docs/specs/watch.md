---
trace: enforced
---

# Watching the bus

What `vion` guarantees a person or a script that watches a channel of the platform's message bus.
The watch commands, their options and their help are derived from the committed event document
`asyncapi/v1.json`, read together with `openapi/v1.json` for the operations it names; what this page
states is the written procedure around them — which row is read, how the tool connects, what it
prints, how it ends. Area code `WATCH`, anchored by the event document and by the watch commands'
entries in the help snapshot `docs/snapshots/help.txt`. Process:
[`../spec-process.md`](../spec-process.md).

A watch reports what the bus delivers after its subscription stands, and nothing else: no first row
is read from elsewhere, and a message missed while the connection was down is not replayed. The
rules a subscriber of the bus follows are cloud-api's and are cited, not restated:
`cloud-api/AC-BUS-004.4` — a channel names the row that hands out its topic, the operation that
answers that row and the join that picks it; `cloud-api/AC-BUS-004.8` — a channel that delivers only
to registered subscribers names its registration; `cloud-api/AC-BUS-010.1` — what a registration
sends to the gateways; `cloud-api/AC-BUS-010.2` — a subscriber may write its own last will.

## The watch commands are derived

A watch exists exactly when a row hands a channel out. It sits in the tree beside the aggregate's
operations, takes as options only what selects that row, and is described by the channel.

- `AC-WATCH-001.1` (Ubiquitous): THE SYSTEM SHALL offer one command `vion <domain> <aggregate> watch` for every channel of the committed document `asyncapi/v1.json` that a row hands out, the domain and aggregate being the channel id's first two segments in kebab case, and no watch command for any other channel.
- `AC-WATCH-001.2` (Ubiquitous): THE SYSTEM SHALL take as an option, named by the parameter's name in kebab case, every address parameter of a channel that some hand-out joins to an operation parameter or to a member of a listed row, except `tenantId`, `integratorId`, `platformId` and `projectId`, which the context supplies, requiring each unless some hand-out of the channel does not join it.
- `AC-WATCH-001.3` (Unwanted): IF two channels derive to one command, or a channel derives to a command an operation has, THEN THE SYSTEM SHALL fail the derive step naming both.
- `AC-WATCH-001.4` (Ubiquitous): THE SYSTEM SHALL describe each watch command by its channel's description and each of its options by the parameter's description in help.

## Subscribing

One procedure for every channel, in one order: the row and its topic, the subscriber, the
connection with its will, the subscription, the registration where the channel names one, then the
notice that the watch stands. The same order holds after a lost connection. A refusal happens as
early as it can: a row that does not hand the channel out, or a registration value the row does not
carry, ends the command before any connection.

- `AC-WATCH-002.1` (Event-driven): WHEN a watch command runs THE SYSTEM SHALL read, with the operation of the first hand-out of the channel that joins exactly the options given, the row those options select, subscribe to the topic that row carries in `meta.topics` under the channel's id, and, where no row matches or the row carries no such topic, refuse before connecting, naming the row and the channel, and exit 1.
- `AC-WATCH-002.2` (Event-driven): WHEN a watch command connects THE SYSTEM SHALL connect to the broker, with the client id, protocol version and last will, and subscribe at the quality of service, that `me.subscriber.create` answers, presenting an empty username and the token resolved at that connect as the password.
- `AC-WATCH-002.3` (Optional): WHERE the committed document marks a channel with `x-vion-registeredBy` THE SYSTEM SHALL, once the broker has acknowledged the subscription, call the operation it names with the subscriber's id, the address parameters as it fills them, and each value given for an input it fills from a row member, or every value the row carries where none is given.
- `AC-WATCH-002.4` (Unwanted): IF a value given for a registration's input is not one the row carries THEN THE SYSTEM SHALL refuse before connecting, naming it, and exit 2.
- `AC-WATCH-002.5` (Event-driven): WHEN the broker has acknowledged the subscription and, where the channel is registered, the registration has answered THE SYSTEM SHALL write one notice to standard error that it is watching, naming the channel.
- `AC-WATCH-002.6` (Unwanted): IF the connection is lost after that notice THEN THE SYSTEM SHALL connect again with the token resolved again, subscribe and register again in that order, and write a notice to standard error that messages may have been missed.
- `AC-WATCH-002.7` (Unwanted): IF the broker refuses the connection or the subscription THEN THE SYSTEM SHALL report the refusal with its reason and exit 1.
- `AC-WATCH-002.8` (Event-driven): WHEN a watch ends, by its timeout or an interrupt, THE SYSTEM SHALL publish an empty message to the last will's topic and then disconnect from the broker before it exits.
- `AC-WATCH-002.9` (Unwanted): IF the broker address `me.subscriber.create` answers is not a `wss` address, or carries credentials of its own, THEN THE SYSTEM SHALL refuse it before opening a connection, so that the token is sent to no one.

## What comes back

A watch prints payloads, as published. Under `--json` each is one line, so the stream is
line-delimited and a filter is a pipe; in table mode each is the key–value table any object gets.
Only the channel's own message is printed. A watch that ends as asked has succeeded.

- `AC-WATCH-003.1` (Ubiquitous): THE SYSTEM SHALL write each message whose `schema` user property names the channel's message, and no other message, as its payload on one line of standard output under `--json` and as an object's key–value table otherwise, projected by `--fields` in both.
- `AC-WATCH-003.2` (Ubiquitous): THE SYSTEM SHALL watch until `--timeout` where one is given, without `--wait` beside it, and until interrupted otherwise, and exit 0 at either.
