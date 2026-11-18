---
slug: deprecation-and-stability
status: proposed           # proposed | in-flight | parked | archived
blocked-on: none           # for parked docs: what's blocking + ref
areas: CLI
author: the api-change-communication session, for Jonas Bertsch
created: 2026-11-18
updated: 2026-11-18
supersedes: none           # path of a superseded change doc, or none
---

# Show each operation's stability and sunset, and say when an answer comes from a deprecated one

> Change doc — one in-flight change. The `Spec delta` below is distilled into the current-truth
> pages under `docs/specs/` in the implementation PR, then this doc is archived
> (`spec-change.ps1 archive <slug>`). Process: `docs/spec-process.md`. Never put change narrative
> inline in a spec page — it lives here.

## At a glance

### Summary

cloud-api now publishes on every operation `x-stability-level` (`stable` or `beta`) and, on a
deprecated one, `deprecated: true` with `x-sunset`, and answers a deprecated operation with
`Deprecation`, `Sunset` and `Link: </changelog>; rel="deprecation"`. `vion` reads none of it. This
change makes `ops` and a derived command's own help show stability and sunset, makes `ops --live`
report a change in any of the three as drift, and writes one notice on standard error when the
answer to the invoked operation carries `Deprecation`. Cross-repo spec:
`specs/in-flight/2026-11-17-api-change-communication.md`.

### Spec implications

`cli.md` changes four criteria and gains one. `AC-CLI-001.2` keeps a deprecated command invocable
and out of its parent's help, and now states the deprecation and sunset on the command's own help.
`AC-CLI-001.6` adds the stability to that same help. `AC-CLI-006.1` adds a stability column and the
sunset to `ops`. `AC-CLI-006.2` adds `x-stability-level`, `deprecated` and `x-sunset` to what
`ops --live` compares. `AC-CLI-003.6` is new: the notice. The help snapshot moves: every visible
derived command's page gains its stability line. It can never show a sunset, because it walks
visible commands only and a deprecated one is hidden; a test carries that half of `AC-CLI-001.2`.

### Decisions

- `D1` — Two levels, `stable` and `beta`, in the document's lowercase words, everywhere `vion`
  writes one. Ratified: brief § Constraints already decided, 1.
- `D2` — A deprecated command stays invocable and hidden. The notice informs and never blocks:
  standard output and the exit code are what they would be without it. Ratified: brief §
  Constraints 2, `AC-CLI-001.2`.
- `D3` — The notice is not written in `ApiClient.SendAsync`, which also serves the wait's polls
  and the `If-Match` pre-read. Ratified: brief § Constraints 3.
- `D4` — The notice is written right after the one send of the invoked operation's request, in
  each of the three commands that send one: `OperationInvoker`, `ApiCommand`, `WhoamiCommand`. One
  helper writes it, and each site calls it once. **Propose-and-wait**, reviewer's question 1.
- `D5` — A watch writes no notice. **Propose-and-wait**, reviewer's question 2.
- `D6` — Stability and sunset show in a derived command's own help only, not in its parent's
  command list, by the help action's existing per-command swap (`CliTree.cs:131–146`).
  Decide-and-document: reviewer's question 3.
- `D7` — `ops` adds a `stability` member and column, and a `sunset` member. In the table, a
  deprecated operation's flags read `deprecated sunset=2027-02-01`. A watch row has no stability:
  the event document publishes none. Decide-and-document: reviewer's question 3.
- `D8` — A deprecated schema *property* reaches no output. Decide-and-document: reviewer's
  question 4.
- `D9` — `vion` does not read the `/capabilities` row's new `stability` and `deprecation`. `ops
  --live` compares the document's own three facts, so the `Stable`/`stable` casing and the null
  `deprecation` never meet code. Decide-and-document, see Full design § `ops --live`.
- `D10` — The derive step refuses an operation with no `x-stability-level`, or with one other than
  `stable` or `beta`. It also refuses an `x-sunset` that is not an ISO date. A deprecated operation
  with no `x-sunset` derives, and shows no date. Decide-and-document: this follows the refusal of a
  missing `x-vion-capability.effect` (`DocumentReader.cs:96–103`). A level that no one wrote down
  must not read as a promise.
- `D11` — The notice is tested with `RecordingHandler` answering `Deprecation`. It is checked
  against a real host by whoever deprecates the first operation on test: not run, routes to a
  human. Decide-and-document: reviewer's question 5.

### Reviewer's questions

1. (c) propose-and-wait — **Where the notice is written** (`D4`). The spec says "the renderer". The
   renderer alone misses four of the paths that answer an invoked operation. Options:
   - **A, at the renderer.** It mirrors how the replay notice travels. The notice goes in
     `Renderer.Render` and in `RenderIncluded`'s JSON branch. `LongRunningWait.FollowAsync` writes
     it once on its first answer and clears it from every answer it returns, as it already does
     for `Replayed` (`LongRunningWait.cs:54–59,78,85`). `WhoamiCommand`'s table branch adds a fourth
     call. Every poll answer must be cleared too, or a deprecated poll operation is announced as
     if it were the invoked one: the error return at `:116–119` hands back the poll's own answer
     unchanged.
   - **B, at the invoked send (recommended).** One call after the one `client.SendAsync` that
     carries the invoked operation's request: `OperationInvoker.cs:147`, `ApiCommand.cs:140`,
     `WhoamiCommand.cs:45`. Nothing has to be cleared. A wait left running (exit 3) or interrupted
     has written the notice already, because the send came before the wait. `--include --json`
     needs no branch of its own. Polls, the `If-Match` pre-read, `can-i`'s reads and a watch's
     setup calls never pass these three lines.
   B departs from the spec's word, not from its intent: one notice per answer of the invoked
   operation, never on standard output. Under `--wait` the notice comes before the outcome rather
   than after it, as the replay notice already does.
2. (c) propose-and-wait — **A watch** (`D5`). A watch's HTTP calls are setup: the hand-out read,
   `me.subscriber.create`, and the registration, which repeats after every reconnect
   (`WatchInvoker.cs:170,257,286`). The operation a watch invokes is the event document's, and its
   answers are messages, not HTTP answers. Options:
   - **out of scope by name (recommended)**, the same rule as the pre-reads. `ops` already shows
     each setup operation's sunset, and `ops --live` reports the deprecation.
   - one notice per distinct deprecated setup operation per run, written at the first answer that
     carries `Deprecation`. A watch that will stop working at the sunset would then say so, at the
     cost of a rule of its own.
3. (b) decide-and-document — **Where stability and sunset show** (`D6`, `D7`). Help: the command's
   own help ends its description with one line: `Stability: beta — may change without notice.` or
   `Stability: stable — changes only additively or by a deprecation.`, and on a deprecated command
   `Deprecated: may be gone from 2027-02-01.` These words are cloud-api's own: the `Stability`
   schema, and `CapabilityDeprecationOutput.sunset`'s "the first day the operation may be gone". The
   parent's list keeps the bare description. Otherwise nearly every entry in every list would end in `beta`.
   `ops`: a `stability` column after `completion`. The JSON row gains `stability`
   (`stable`|`beta`|null) and `sunset` (ISO date|null), and the table's `deprecated` flag gains
   `sunset=<date>` beside it. A separate `sunset` column was the alternative, and today it would be
   empty on every row. OUTCOME: pending ratification.
4. (b) — **A deprecated property** (`D8`). Today `edgeGatewayIds` on a deployment and `topic` on a
   service property, sunset 2027-02-01. `vion` renders a body verbatim as `JsonNode` and never reads
   response schemas: help lists options, never the members of an answer. So no output names a
   member, and there is nowhere to attach a property's sunset without a new schema walk per
   answer. `ops --live` compares operations, not schemas: a removed member is not drift there today
   either. The property's deprecation reaches the operator through cloud-api's changelog. OUTCOME:
   pending ratification.
5. (b) — **Testing the notice** (`D11`). No operation is deprecated anywhere, so every notice test
   is a `RecordingHandler.Json(…, configure)` answer with the three headers. Each path in Full
   design § The notice gets a test. The negative tests: a poll's `Deprecation` and an `If-Match`
   pre-read's `Deprecation` write nothing. The real-host check goes in the pull request's
   Verification as "not run, routes to a human": whoever deprecates the first operation on test
   runs its command once with `--json` and confirms the notice reaches standard error and nothing
   reaches standard output beyond the body. OUTCOME: pending ratification.

---

## Full design

### What cloud-api publishes, read on test

The pulled snapshot (`openapi/v1.json`, sha256 `5455645a…`) carries `x-stability-level` on all 272
operations: 271 `beta` and `stable` on `me.identity.get` alone. No operation is `deprecated`. Two
schema properties carry `deprecated: true` with `x-sunset: 2027-02-01`:
`DeploymentOutput.edgeGatewayIds` and `ServicePropertyOutput.topic`. `CapabilityOutput` gains
`stability` (`Stability`: `Beta`, `Stable`) and `deprecation` (`CapabilityDeprecationOutput`:
`since`, `sunset`, `replacedBy`). The headers are set in cloud-api's
`PermissionEnforcementFilter.cs:90–96`: `Deprecation: @<since, Unix seconds>`,
`Sunset: <sunset, RFC 1123>`, `Link: </changelog>; rel="deprecation"`.

The pull moved nothing beyond this spec's facts. A script over both versions of the document
found: no operation added or removed; on the operations, only `x-stability-level` added; two
schemas added (`Stability`, `CapabilityDeprecationOutput`); three changed (`CapabilityOutput`
gained the two members, and the two property sunsets). `derive.ps1` changed only the sha in
`Operations.g.cs`. The help snapshot did not move, because nothing reads the new facts yet.

### Derive

`DocumentReader.ReadOperation` reads `x-stability-level` and `x-sunset` beside `deprecated`
(`DocumentReader.cs:130`), into `DerivedOperation.Stability` and `DerivedOperation.Sunset`. The
emitter writes them into `Operation` as `Stability Stability` (an enum `Beta`, `Stable`, the
precedent being `Effect` and `Completion`) and `DateOnly? Sunset`. `D10` sets the refusals, written
as the effect and completion refusals are.

### Help

`DerivedCommands.Create` registers each derived command with its operation in a map on `CliTree`,
the precedent being `tree.Watches`. `TreeHelpAction.Invoke` (`CliTree.cs:129`) swaps a derived
command's description for the description plus the stability line, and the deprecation line where
there is one, for the length of its own help. The watch branch already swaps the `--timeout`
description the same way. `__help-snapshot` parses `<path> --help` (`HelpSnapshotCommand.cs:48`), so
it goes through the same action, and every visible derived command's page in `help.txt` gains its
line.

### `ops`

`Run` adds `stability` (`operation.Stability`, lowercased) and `sunset` (`yyyy-MM-dd` or null) to
each JSON row (`OpsCommand.cs:58–72`). On a watch row both are null. The table gains the
`stability` column (`—` on a watch, as for its method and path), and the flags gain
`sunset=<date>` after `deprecated`.

### `ops --live`

`ReadOpenApiOperations` (`OpsCommand.cs:334–338`) also reads each served operation's
`x-stability-level`, `deprecated` and `x-sunset` into `LiveOperation`. The comparison at `:186–213`
adds three details: `x-stability-level`, which compares the served token with the committed level
ignoring case, so that a missing token differs; `deprecated`; and `x-sunset`, which compares the
served string with the committed date as `yyyy-MM-dd`. Under `D9`, neither side reads
`/capabilities`, so the row's member-name casing never meets the document's.

### The notice

What it says, on one line of standard error:

```
deprecated: tenant.thing.get may be gone from 2027-02-01; see https://api.vion.swiss/changelog.
```

- **The name.** The invoked operation's id. Under `api` with a path no template matches, the
  method and path as given.
- **The date.** `Sunset` read as an HTTP date and written as `yyyy-MM-dd`. An unparseable value is
  written as given. With no `Sunset`, the clause is left out.
- **The pointer.** The `Link` entry whose `rel` includes `deprecation`, resolved against the
  context's API URL with `new Uri(new Uri(apiUrl + "/"), reference)`. `</changelog>` is an absolute
  path, so a path base on a custom API URL is dropped. The built-in URLs have none
  (`ApiEnvironment.cs:18,22`). With no such link, the clause is left out. The pointer is printed and
  never followed, so no origin check applies, unlike `Operation-Location` (`LongRunningWait.cs:139`).
- **The trigger.** `Deprecation` present, whatever its value, on a success or an error answer.
  cloud-api sends it on a refusal at the authorization check too, and an operator refused by a
  deprecated operation also learns that it is deprecated.

`ApiResponse` gains `Deprecation`, `Sunset` and `DeprecationLink`. `ApiClient` parses them beside
`Operation-Location` (`ApiClient.cs:130`); parsing there is not emitting. Which paths write the
notice, under recommendation B (reviewer's question 1):

| Path | Notice |
| --- | --- |
| a derived command, table or `--json` | once, after the send (`OperationInvoker.cs:147`) |
| a derived command with `--wait`: `Succeeded`, `Failed`, left running (exit 3), interrupted | once, for the start's answer, before the wait; never for a poll |
| `api`, `api --include` in table and in `--json` mode, `api --wait` | once, after the send (`ApiCommand.cs:140`) |
| `api --no-write-safety` | once: the same send |
| `whoami`, table and `--json` | once, after the send (`WhoamiCommand.cs:45`) |
| the `If-Match` pre-read (`WriteSafety.cs:77`), a wait's poll (`LongRunningWait.cs:169,177`) | none: not the invoked operation (`D3`) |
| `can-i`'s catalog and permission reads (`CanICommand.cs:172`) | none: the command's own reads, not an operation the caller named |
| a watch's hand-out read, subscriber and registration (`WatchInvoker.cs:170,257,286`) | none, under `D5` |
| `ops --live`'s document reads | none: no operation is called |

Alternatives considered: emitting in `ApiClient.SendAsync` (ruled out by `D3`); emitting at the
renderer (option A of reviewer's question 1).

---

## Drift checkpoints

- _(none yet — append as implementation diverges from Full design)_

---

## Spec delta (to distill)

- MODIFIED AC-CLI-001.2 -> docs/specs/cli.md : WHERE an operation is `deprecated` THE SYSTEM SHALL keep its command invocable, leave it out of its parent's help, and state in the command's own help that it is deprecated and, where the document gives one, its `x-sunset`.
- MODIFIED AC-CLI-001.6 -> docs/specs/cli.md : THE SYSTEM SHALL describe each derived command by its operation's description and, in the command's own help, its `x-stability-level`, and each derived option by its parameter's description in help.
- ADDED AC-CLI-003.6 -> docs/specs/cli.md : WHEN a derived command, `api` or `whoami` is answered with `Deprecation` on the request it sends for its operation THE SYSTEM SHALL write one notice to standard error naming the operation, or the method and path where `api` matches none, with the `Sunset` date and the `Link` of relation `deprecation` resolved against the environment's API URL where the answer carries them, and SHALL leave standard output and the exit code as they are without it.
- MODIFIED AC-CLI-006.1 -> docs/specs/cli.md : WHEN `ops` runs THE SYSTEM SHALL list, from the committed documents and without a request, every tree command with its operationId, protocol, method, path, effect, completion, stability and flags, a deprecated operation's flags carrying its `x-sunset` — a watch with the operation of the event document that sends on its channel, the protocol the event document's servers declare, no method, path or stability, the effect of its channel's registration operation where the channel registers and `Read` otherwise, and completion `Stream` — and with `--all` the `system` operations too.
- MODIFIED AC-CLI-006.2 -> docs/specs/cli.md : WHEN `ops --live` runs THE SYSTEM SHALL read the environment's two documents and print the operations added, removed or changed against the committed ones — in method, path, `x-vion-authorization`, `x-vion-capability`, `x-stability-level`, `deprecated` or `x-sunset` for the OpenAPI document, and in action, channel, parameters, message names, `x-vion-handedOutBy` or `x-vion-registeredBy` for the event document, never in a channel's address — exiting 0 only when none differ.

---

## Tasks

- `T-001` (none): pull the snapshot from test and re-derive. It moves no criterion: done before
  ratification, in its own commit.
- `T-002` (`AC-CLI-001.2`, `AC-CLI-001.6`): derive reads `x-stability-level` and `x-sunset`.
  `Operation` carries them, and a derived command's own help states them. Tests: a synthetic
  deprecated operation's own `--help` names its sunset and its parent's help omits it; a beta and
  a stable command's own help name their level. The regenerated help snapshot goes in the same
  commit.
- `T-003` (`AC-CLI-006.1`): `ops` shows stability and sunset, in table and JSON mode.
- `T-004` (`AC-CLI-006.2`): `ops --live` compares the three facts, with one served-document test
  per fact.
- `T-005` (`AC-CLI-003.6`): the notice, per the path table, with the negative tests for a poll and
  a pre-read.
- `T-006`: distill the delta into `cli.md` and archive this doc.
