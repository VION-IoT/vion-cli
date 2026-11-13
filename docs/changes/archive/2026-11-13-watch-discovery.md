---
slug: watch-discovery
status: archived
blocked-on: none           # for parked docs: what's blocking + ref
areas: CLI,WATCH
author: the watch-discovery session, for Jonas Bertsch
created: 2026-11-13
updated: 2026-11-13
supersedes: none           # path of a superseded change doc, or none
---

# A watch can be found and understood from the tool itself

> Change doc — one in-flight change. The `Spec delta` below is distilled into the current-truth
> pages under `docs/specs/` in the implementation PR, then this doc is archived
> (`spec-change.ps1 archive <slug>`). Process: `docs/spec-process.md`. Never put change narrative
> inline in a spec page — it lives here.

## At a glance

### Summary

Running the `v0.5.0` release against test showed two gaps. A watch's `--timeout` help described
what `--timeout` does with `--wait`, the opposite of what it does on a watch. And `vion ops`, the
discovery step that `architecture/runbooks/test-environment-checks.md` step 3 points to, listed no
watch. After this change `ops` lists every tree command, the watches included, each with an
operation id from its own document and a new `protocol` member. `ops --live` diffs the event
document as well as the OpenAPI one. `--wait` beside a watch is refused, as `--dry-run` already is.
Brief: `architecture/.claude/briefs/brief-watch-discovery-vion-cli.md` and its amendment 1.

### Spec implications

`cli.md`: two criteria change. `AC-CLI-006.1` gains the `protocol` member and a watch's row.
`AC-CLI-006.2` gains the event document's diff. The `ops` prose above them now says "documents".
`watch.md`: `AC-WATCH-001.5` is new (the `--timeout` help line) and `AC-WATCH-003.4` is new
(`--wait` refused). `AC-WATCH-003.2` loses "without `--wait` beside it", which `003.4` now states.
The help snapshot changed in the help fix (`eebd70e`) and changes again only in `ops`' own help
text. No invariant.

### Decisions

Ratified by Jonas on 2026-11-13 (amendment 1) unless marked (b).

- `D1` — **One list, every row an operation of its own document.** A watch row's `operationId` is
  the id of the `asyncapi/v1.json` operation that sends on the watch's channel (today equal to the
  channel id). (ratified)
- `D2` — **One new member on every row, `protocol`: `http` or `mqtt`.** Every OpenAPI row is
  `http`. An event-document row takes the protocol its document's servers declare, read by the
  derive (`servers.*.protocol`, today `mqtt`) rather than written in the tool. On a watch row
  `method` and `path` are null; `effect` is `Read`, or, where the channel registers, the effect of
  its registration operation, which the watch calls (`tenant service-property watch` is
  `Reversible`); `completion` is `Stream`; every flag is false. (Effect and protocol corrected by
  amendment 2.) The table shows `—` for a null. The count
  line names both kinds of row. No member, value or column of this repo uses `bus` where a reader
  would compare it with `protocol`. "Bus" appears only in prose, the `Vion.Cli/Bus/` folder and
  the test folder of the same name. (ratified)
- `D3` — **`ops --live` also diffs `/asyncapi/v1.json`, by operation id**: operations added or
  removed; and for each operation in both, what the watch reads of it: its `action`, its
  channel's `x-vion-handedOutBy` entries (`key`, `operation`, `row`, `join`), its message names,
  `x-vion-registeredBy`, and its parameters. It never compares the `address`, which names the
  environment (`v1/test/…` and `v1/prod/…`). A difference exits 1 under the same `--json` shape.
  (ratified; `key` and the message names added by amendment 2)
- `D4` — **`--wait` beside a watch is refused with exit 2**, at the same point as `--dry-run`.
  The help keeps the global `--wait` text. (ratified)
- `D5` — **Mint `AC-WATCH-001.5`** for the `--timeout` help line. (ratified)
- `D6` — (b) **Parameters are compared by name, in document order, not by description.** The
  OpenAPI diff compares no description either. A renamed, added or removed parameter changes the
  watch command; a reworded description changes only its help, and a snapshot pull catches that.
- `D7` — (b) **The operation's `channel` reference is compared too**, reported as `channel`. The
  derive keys a watch by channel id (it reads the row's `meta.topics` under it), so an operation
  that moves to another channel moves the watch, even when the address is unchanged.
- `D8` — (b) **`--all` is unchanged: it adds the `system` operations.** The event document's only
  operation without a command, the client's last will (`me.lastWill.event`, action `receive`), is
  not listed. `ops --live` still diffs it, because `D3` diffs every operation.
- `D9` — (b) **The derive fails when a watched channel has no `send` operation.** Without one, the
  watch row would have no operation id from its own document (`D1`). The derive's internals are
  exempt from the corpus (`docs/spec-process.md` § The roster), so this mints no criterion.
  `EventDocumentReaderShould` pins it.
- `D10` — (b) **The servers must declare exactly one protocol, or the derive fails.** None leaves
  a watch row with no protocol to name; two different ones leave it with two, and nothing in the
  document says which a watch uses. Several servers declaring the same one are one protocol.
  `EventDocumentReaderShould` pins both refusals.
- `D11` — (b) **`ops --live` does not compare the servers' protocol.** A watch connects to the
  broker that `me.subscriber.create` answers, refused unless it is a `wss` address
  (`AC-WATCH-002.9`); it reads nothing of the document's servers at run time. The protocol is
  what `ops` prints, and a moved protocol reaches it with the next snapshot pull.

### Reviewer's questions

1. (a) `D2`'s `mqtt` over `bus`: ratified. The repo-wide check for `bus` as a compared value is in
   `D2`. OUTCOME: accepted, amendment 1 answer 1.
2. (a) The smoke hedge: cloud-api's `Get-Walk` (`scripts/smoke-test.ps1:253`, `origin/main`) keeps
   only rows with `method -eq 'GET'` and rows with `operationId -like '*.list'` and a `command`. A
   watch row has `method` null, so it never enters the GET map. Its `operationId` is not null
   (`tenant.alert.state`, for example) but ends in `.state` or `.event`, never `.list`: the 18
   watched ids are listed in `Vion.Cli/Derived/Channels.g.cs`. The walk is unchanged. OUTCOME:
   accepted, amendment 1 § Unchanged.
3. (b) `D6`–`D9` are this session's calls. Each is cheap to reverse if the reviewer disagrees.
   OUTCOME: decided by the implementing session as recorded; the human review of the pull request
   accepts or changes them.
4. (b) `live-check.yml` reads `items` generically (`jq '.items | length'`), so the event diff
   arrives in the issue unchanged. Its "differs" hint named only the OpenAPI snapshot. It now
   names both pull scripts, as does `docs/releasing.md` § The live check. OUTCOME: decided by the
   implementing session as recorded; the human review of the pull request accepts or changes it.

---

## Full design

**Data the tool carries.** `Channels.g.cs` gains a second table, `EventOperations.All`: one row
per operation of the event document, with its id, its `action`, the id of the channel it
references, and the contract that `D3` compares. The contract is the channel's `parameters`,
`messages`, `x-vion-handedOutBy` and `x-vion-registeredBy` members, copied as the document has them and
minified. The committed side and the served side then go through one projection, at run time in
`OpsCommand`. The derive writes no projection of its own that the tool would have to agree with.
`Channel` gains `OperationId` (`D1`, `D9`) and `Protocol`, and `Channels` the constant `Protocol`,
both the servers' one protocol (`D10`).

**`ops`.** Rows are built as today. Each gains `protocol: http`, and after them comes one row per
watch: `command`, `operationId` = `Channel.OperationId`, `protocol` = `Channel.Protocol`, `method`
and `path` null, `effect` the registration operation's or `Read` (`D2`), `completion: Stream`,
every flag false. The table puts `protocol` after
`operation` and writes `—` for a null. The count line names each protocol's count, `<h> http operations,
<m> mqtt operations`.

**`ops --live`.** After the OpenAPI read, `GET <api>/asyncapi/v1.json`, tokenless, with the same
error handling. Operations are matched by id: an added or removed one is a row with
`protocol` = `Channels.Protocol`, `method` and `path` null and `detail` null. For an operation in both, `detail`
lists whichever of `action`, `channel`, `parameters`, `messages`, `x-vion-handedOutBy` and
`x-vion-registeredBy` differ. Every live row carries `protocol` as well. The closing notice counts
both documents.

**`--wait`.** `WatchInvoker` refuses `--wait` right after the `--dry-run` refusal, before the
broker check and before any request.

---

## Drift checkpoints

- 2026-11-13: `System.CommandLine` 2.0.14 keeps its per-command help customisation internal, so
  the `--timeout` text on a watch is swapped in by a help action wrapping the library's own, for
  the length of that one help (`eebd70e`). Sibling sweep: N/A — no other option's help differs by
  command.
- 2026-11-13: run against both hosts with the built tool, `ops --live --env test` and
  `--env production`: no differences, 269 http and 19 mqtt operations each, while production's
  channel addresses read `v1/prod/…` — the address is out of the comparison, as `D3` requires.
- 2026-11-13: correction round 1 on #15 (amendment 2), after the archive, each fix carrying its
  criterion and delta line: `ops --live` compares each hand-out's `key` and the channel's message
  names, both of which the watch reads at run time (`WatchInvoker` reads the topic under the key,
  `WatchRunner` keeps only the named message); a watch row's effect is its registration's where
  the channel registers; `protocol` is read from the servers (`D10`, `D11`). Sibling sweep: done —
  the watch's other run-time reads (row, join, registration, parameters) were already compared;
  the channel description and the message payload schema change only help and printed members,
  which a snapshot pull carries.

---

## Spec delta (to distill)

- ADDED AC-WATCH-001.5 -> docs/specs/watch.md : THE SYSTEM SHALL describe `--timeout` in a watch command's help as how long it watches once subscribed, until interrupted when absent, exiting 0 then and 1 if it never subscribed.
- MODIFIED AC-WATCH-003.2 -> docs/specs/watch.md : THE SYSTEM SHALL watch, once the subscription stands, until `--timeout` where one is given and until interrupted otherwise, and exit 0 at either.
- ADDED AC-WATCH-003.4 -> docs/specs/watch.md : IF `--wait` is given on a watch THEN THE SYSTEM SHALL refuse before sending anything, naming it, and exit 2.
- MODIFIED AC-CLI-006.1 -> docs/specs/cli.md : WHEN `ops` runs THE SYSTEM SHALL list, from the committed documents and without a request, every tree command with its operationId, protocol, method, path, effect, completion and flags — a watch with the operation of the event document that sends on its channel, the protocol the event document's servers declare, no method or path, the effect of its channel's registration operation where the channel registers and `Read` otherwise, and completion `Stream` — and with `--all` the `system` operations too.
- MODIFIED AC-CLI-006.2 -> docs/specs/cli.md : WHEN `ops --live` runs THE SYSTEM SHALL read the environment's two documents and print the operations added, removed or changed against the committed ones — in method, path, `x-vion-authorization` or `x-vion-capability` for the OpenAPI document, and in action, channel, parameters, message names, `x-vion-handedOutBy` or `x-vion-registeredBy` for the event document, never in a channel's address — exiting 0 only when none differ.

---

## Tasks

- `T-001` (`AC-WATCH-001.5`): cite the new criterion from `DescribeTimeoutOnWatchAsBoundingTheWatch` (code landed in `eebd70e`).
- `T-002` (`AC-WATCH-003.2`, `AC-WATCH-003.4`): refuse `--wait` on a watch with exit 2.
- `T-003` (`AC-CLI-006.1`): derive each watch's operation id and the event-operation table; `ops` lists watch rows with `protocol`.
- `T-004` (`AC-CLI-006.2`): `ops --live` diffs the event document; `live-check.yml`'s hint names both pulls.
- `T-005`: distill, archive.
