---
slug: deprecation-and-stability
status: in-flight          # proposed | in-flight | parked | archived
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

`cli.md` changes three criteria and gains one. `AC-CLI-001.2` keeps a deprecated command invocable
and out of its parent's help, and now states the deprecation and sunset on the command's own help.
`AC-CLI-001.6` adds a `stable` operation's stability to that same help. `AC-CLI-006.2` adds
`x-stability-level`, `deprecated` and `x-sunset` to what `ops --live` compares. `AC-CLI-003.6` is
new: the notice. `AC-CLI-006.1` keeps its text: it says "… and flags" and lists none, and `stable`
and `sunset=<date>` are flags. The help snapshot moves by one page, `vion me identity get`, the one
`stable` operation. It can never show a sunset, because it walks visible commands only and a
deprecated one is hidden; a test carries that half of `AC-CLI-001.2`.

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
  helper writes it, and each site calls it once. Ratified: amendment 1, Q1 → B.
- `D5` — A watch writes no notice: its HTTP calls are setup, out of scope by name by the rule the
  pre-reads follow. Ratified: amendment 1, Q2.
- `D6` — Stability and sunset show in a derived command's own help only, never in its parent's
  command list, by the help action's existing per-command swap (`CliTree.cs:131–146`). The default
  is never written where a person reads: the stability line appears only on a `stable` operation,
  the deprecation line on every deprecated one, `stable` or `beta`. A `beta` command that is not
  deprecated shows no new line. Ratified: amendment 1, 3.
- `D7` — `ops`' table writes `stable` as one more flag, beside `dry-run`, `deprecated` and `hidden`,
  with no stability column, and a deprecated operation's flags read `deprecated sunset=2027-02-01`.
  `ops --json` writes every value where a program reads: each row gains `stability` (`stable` or
  `beta`, null on a watch row, whose event document publishes none) and `sunset` (an ISO date or
  null), beside its existing `deprecated`. Ratified: amendment 1, 3.
- `D8` — A deprecated schema *property* reaches no output. Ratified: amendment 1, 4.
- `D9` — `vion` does not read the `/capabilities` row's new `stability` and `deprecation`. `ops
  --live` compares the document's own three facts, so the `Stable`/`stable` casing and the null
  `deprecation` never meet code. Ratified: amendment 1, 4.
- `D10` — The derive step refuses an operation with no `x-stability-level`, or with one other than
  `stable` or `beta`. It also refuses an `x-sunset` that is not an ISO date. A deprecated operation
  with no `x-sunset` derives, and shows no date. This follows the refusal of a missing
  `x-vion-capability.effect` (`DocumentReader.cs:96–103`): a level no one wrote down must not read
  as a promise. Ratified: amendment 1, 4.
- `D11` — The notice is tested with `RecordingHandler` answering `Deprecation`. It is checked
  against a real host by whoever deprecates the first operation on test: not run, routes to a
  human. Ratified: amendment 1 (the (b) decisions stand).
- `D12` — The notice is one line on standard error, `warning: <operationId> is deprecated, sunset
  <date> — <changelog url>`, its form mirroring `error: …` (`Guard.cs:65`). No flag silences it, and
  it is written in `--json` mode and when standard error is not a terminal too, unlike the update
  notice (`UpdateCheck.cs:16,30`): the owner of a script calling a deprecated operation is the one
  who has to act before the sunset, and a line on standard error breaks no parser of standard
  output. Under `--json` it goes to standard error, as `AC-CLI-003.1` says of every notice.
  Ratified: amendment 1, Q1.

### Reviewer's questions

1. (c) **Where the notice is written** (`D4`). Option A was the renderer plus the replay-style
   relay through `LongRunningWait.FollowAsync`, which would also have had to clear every poll's
   answer. Option B, recommended, writes it after the one send of the invoked operation
   (`OperationInvoker.cs:147`, `ApiCommand.cs:140`, `WhoamiCommand.cs:45`). OUTCOME: B, with the
   notice's form and its no-silencing rule as `D12` states. Decided by Jonas in amendment 1.
2. (c) **A watch** (`D5`). OUTCOME: out of scope by name, as recommended. Decided by Jonas in
   amendment 1.
3. (b) **Where stability and sunset show** (`D6`, `D7`). The proposal was a stability line on
   every command's own help and a stability column in `ops`. OUTCOME: changed. The default is not
   written where a person reads, so the line appears only on `stable`, and `stable` is a flag rather
   than a column. Every value is written where a program reads (`ops --json`). The `AC-CLI-006.1`
   delta line is dropped. Decided by Jonas in amendment 1.
4. (b) **A deprecated property** (`D8`). `vion` renders a body verbatim and never reads response
   schemas, so no output names a member, and `ops --live` compares operations, not schemas.
   OUTCOME: accepted, amendment 1.
5. (b) **Testing the notice** (`D11`). Every notice test is a `RecordingHandler` answer with the
   three headers, one per path in Full design § The notice, and the negative tests cover a poll and
   an `If-Match` pre-read. The real-host check is routed to a human in the pull request. OUTCOME:
   accepted, amendment 1.

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
precedent being `Effect` and `Completion`) and `string? Sunset`, the ISO date as the document
writes it once derive has checked it. `D10` sets the refusals, written as the effect and completion
refusals are.

### Help

`DerivedCommands.Create` registers each derived command with its operation in a map on `CliTree`,
the precedent being `tree.Watches`. `TreeHelpAction.Invoke` (`CliTree.cs:129`) swaps a derived
command's description, for the length of its own help, for the description followed by
`Stability: stable — changes only additively or by a deprecation.` on a `stable` operation and
`Deprecated: may be gone from <sunset>.` (or `Deprecated.` with no sunset) on a deprecated one. These
words are cloud-api's own: the `Stability` schema, and `CapabilityDeprecationOutput.sunset`'s "the
first day the operation may be gone". The watch branch already swaps the `--timeout` description
the same way. `__help-snapshot` parses `<path> --help` (`HelpSnapshotCommand.cs:48`), so it goes
through the same action, and the one `stable` page in `help.txt` gains its line.

### `ops`

`Run` adds `stability` (`operation.Stability`, lowercased) and `sunset` (`yyyy-MM-dd` or null) to
each JSON row (`OpsCommand.cs:58–72`). On a watch row both are null. In the table, the flags gain
`stable` and, after `deprecated`, `sunset=<date>` (`OpsCommand.cs:108–132`). There is no new
column.

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
warning: tenant.thing.get is deprecated, sunset 2027-02-01 — https://api.vion.swiss/changelog
```

- **The name.** The invoked operation's id. Under `api` with a path no template matches, the
  method and path as given.
- **The date.** `Sunset` read as an HTTP date and written as `yyyy-MM-dd`. An unparseable value is
  written as given. With no `Sunset`, the `, sunset …` clause is left out.
- **The pointer.** The `Link` entry whose `rel` includes `deprecation`, resolved against the
  context's API URL with `new Uri(new Uri(apiUrl + "/"), reference)`. `</changelog>` is an absolute
  path, so a path base on a custom API URL is dropped. The built-in URLs have none
  (`ApiEnvironment.cs:18,22`). With no such link, the ` — …` clause is left out. The pointer is printed and
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

- 2026-11-18: amendment 1 ratified the doc with a simpler display (`D6`, `D7`, reviewer's question
  3) and the notice's form (`D12`). The `AC-CLI-006.1` delta line is dropped, because its text names
  no flag. `Operation.Sunset` is the checked ISO string, not a `DateOnly`: nothing compares or
  computes with it, and the generated table needs no new namespace.
- 2026-11-18: a hidden command's own `--help` wrote nothing at all, because System.CommandLine skips
  a hidden command's help. So a deprecated command's sunset line could not have shown anywhere. The
  help action now also lifts `Hidden` for the length of that one help, beside the description swap.
  Sibling sweep: a command hidden by `cli.json` alone, neither stable nor deprecated, still writes
  no help. No `cli.json` entry hides one today (`cli.json` is `{}`), so that is left alone and named
  here, not fixed in this change.

---

## Spec delta (to distill)

- MODIFIED AC-CLI-001.2 -> docs/specs/cli.md : WHERE an operation is `deprecated` THE SYSTEM SHALL keep its command invocable, leave it out of its parent's help, and state in the command's own help that it is deprecated and, where the document gives one, its `x-sunset`.
- MODIFIED AC-CLI-001.6 -> docs/specs/cli.md : THE SYSTEM SHALL describe each derived command by its operation's description and, in the command's own help, its `x-stability-level` where it is `stable`, and each derived option by its parameter's description in help.
- ADDED AC-CLI-003.6 -> docs/specs/cli.md : WHEN a derived command, `api` or `whoami` is answered with `Deprecation` on the request it sends for its operation THE SYSTEM SHALL write one notice to standard error naming the operation, or the method and path where `api` matches none, with the `Sunset` date and the `Link` of relation `deprecation` resolved against the environment's API URL where the answer carries them, and SHALL leave standard output and the exit code as they are without it.
- MODIFIED AC-CLI-006.2 -> docs/specs/cli.md : WHEN `ops --live` runs THE SYSTEM SHALL read the environment's two documents and print the operations added, removed or changed against the committed ones — in method, path, `x-vion-authorization`, `x-vion-capability`, `x-stability-level`, `deprecated` or `x-sunset` for the OpenAPI document, and in action, channel, parameters, message names, `x-vion-handedOutBy` or `x-vion-registeredBy` for the event document, never in a channel's address — exiting 0 only when none differ.

---

## Tasks

- `T-001` (none): pull the snapshot from test and re-derive. It moves no criterion: done before
  ratification, in its own commit.
- `T-002` (`AC-CLI-001.2`, `AC-CLI-001.6`): derive reads `x-stability-level` and `x-sunset`.
  `Operation` carries them, and a derived command's own help states them. Tests: a synthetic
  deprecated operation's own `--help` names its sunset and its parent's help omits it; a stable
  command's own help names its level and a beta one's names none. The regenerated help snapshot,
  moved by one page, goes in the same commit.
- `T-003` (`AC-CLI-006.1`, text unchanged): `ops` writes `stable` and `sunset=<date>` as flags in
  the table, and `stability` and `sunset` in every JSON row.
- `T-004` (`AC-CLI-006.2`): `ops --live` compares the three facts, with one served-document test
  per fact.
- `T-005` (`AC-CLI-003.6`): the notice, per the path table, with the negative tests for a poll and
  a pre-read.
- `T-006`: distill the delta into `cli.md` and archive this doc.
