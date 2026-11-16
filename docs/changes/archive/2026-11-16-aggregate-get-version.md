---
slug: aggregate-get-version
status: archived
blocked-on: none           # for parked docs: what's blocking + ref
areas: CLI                 # area codes from docs/specs/_corpus.yml's `owns:`
author: the logic-configuration-document session, for Jonas Bertsch
created: 2026-11-16
updated: 2026-11-16
supersedes: none           # path of a superseded change doc, or none
---

# Read If-Match from the aggregate's get where the operation's own path has none

> Change doc — one in-flight change. The `Spec delta` below is distilled into the current-truth
> pages under `docs/specs/` in the implementation PR, then this doc is archived
> (`spec-change.ps1 archive <slug>`). Process: `docs/spec-process.md`. Never put change narrative
> inline in a spec page — it lives here.

## At a glance

### Summary

cloud-api serves `tenant.logicConfiguration.apply`, a versioned `POST` on
`…/logicConfigurations/{id}/apply`, a route with no `GET` of its own. `vion` resolves `If-Match`
from a `GET` on the same template only, so `apply` from a hand-written document — one with no
`meta` — is refused with exit 2 unless `--if-match` is given. This change adds one step after the
same-template read: the aggregate's `get`, found by operation id, sent on the row the operation's
path addresses. Cross-repo spec: `specs/in-flight/2026-11-12-logic-configuration-document.md`
(§ Settled after the review, `If-Match` on `apply`), slice 3.

### Spec implications

`cli.md` changes one criterion, `AC-CLI-002.4`: its third source, "the aggregate's `get` on the
same path", read as one step and was implemented as a `GET` on the same template only. The
`MODIFIED` text separates the two steps and states the order. `AC-CLI-006.5` already says the raw
call takes the derived command's write-safety rules, so the raw call moves with it and no criterion
of its own changes. No new id, no other page.

### Decisions

- `D1` — The step is one rule for every versioned operation, keyed on the operation id
  `<domain>.<aggregate>.get`, not a case for `apply` — `CLAUDE.md` § The three layers: nothing per
  operation is written. Ratified by the cross-repo spec.
- `D2` — The `get`'s path is built from the operation's path by parameter name: the values the
  operation's path carries for its template's `{name}` segments fill the `get` template's segments of
  the same name. Where the `get` needs a path parameter the operation's path does not carry, the
  step does not apply and the refusal stands — decide-and-document.
- `D3` — Both callers pass the path they will send (the derived command its expanded template, the
  raw call its literal path), and the values are read back from that path against the operation's
  template. One reading serves both, so the derived command's dictionary is not threaded through a
  second signature — decide-and-document.

### Reviewer's questions

1. (a) ratified — the step sits after the same-template `GET` and before the refusal, and is keyed
   on `<domain>.<aggregate>.get`: the cross-repo spec settled both. OUTCOME: accepted, the cross-repo
   spec § Settled after the review.
2. (b) decide-and-document — matching by parameter name (`D2`) rather than by template prefix. A
   prefix rule would serve `apply` too, but would read a `get` whose segments happen to align under
   different names. OUTCOME: decided by the implementing session as recorded; the human review of
   the pull request accepts or changes it.
3. (b) — values are read back from the path with `OperationMatcher`'s segment matching, unescaped,
   and re-escaped by `RequestBuilder.Expand` (`D3`), so a segment reaches the `get` escaped as it
   reached the operation: `t%20one` stays `t%20one`. OUTCOME: decided by the implementing session as
   recorded; the human review of the pull request accepts or changes it.

---

## Full design

`WriteSafety.AddIfMatchAsync` (`Vion.Cli/Http/WriteSafety.cs`) resolves the header in order:
`--if-match`, the `--input` document's `meta.version`, a `GET` on the same template
(`Operations.All.FirstOrDefault(o => o.Method == "GET" && o.Template == operation.Template)`), then
a refusal (`UsageException`, exit 2). The new step sits between the third and the refusal:

1. Find `<operation.Domain>.<operation.Aggregate>.get` in the table, a `GET`.
2. Read the operation's path values: `OperationMatcher.Values(operation.Template, path)`, segment by
   segment, the precedent being `OperationMatcher.Matches`, which already walks a template against a
   path. A `{name}` segment yields its value, unescaped.
3. Where every path parameter of the `get` (`Operation.Parameters`, `In == Path`) has a value,
   expand the `get`'s template with them (`RequestBuilder.Expand`, the precedent every request
   takes) and read it exactly as the same-template step does: `meta.version`, else the `ETag`; a
   failed read is a `CliFailure` naming the `get`.

The step reuses the same-template branch's read, so the only new code is finding the `get` and
building its path.

Who reaches it, re-derived over the pulled snapshot (a script over `openapi/v1.json`'s paths and
`x-vion-capability`): 32 versioned operations, 27 with no `GET` on their own path, and of those one
whose aggregate has a `get` — `tenant.logicConfiguration.apply`. The other 26 keep the refusal.

Alternatives considered: a `cli.json` entry naming each operation's version source (per-operation
presentation, and `cli.json` carries only hide and alias); a derive-time column on `Operation`
naming the `get` (a generator change for one lookup the runtime does in one line).

---

## Drift checkpoints

- 2026-11-16: none against the design. The brief's count held when re-derived over the pulled
  snapshot: 27 versioned operations without a `GET` on their own path, `apply` the only one whose
  aggregate has a `get`. One rule left without a test: a `get` needing a path value the
  operation's path does not carry. No operation in the document reaches it, and the table is
  compiled, so no fixture can; it keeps today's refusal. Sibling sweep: N/A, one resolver.

---

## Spec delta (to distill)

- MODIFIED AC-CLI-002.4 -> docs/specs/cli.md : WHERE the operation is `versioned` THE SYSTEM SHALL send `If-Match` from `--if-match`, else from the `--input` document's `meta.version`, else from the version a `GET` on the operation's own path answers where the committed document has one, else from the version the aggregate's `get` answers for the row the operation's path addresses where the committed document has one, and SHALL otherwise refuse before sending with exit 2.

---

## Tasks

- `T-001` (`AC-CLI-002.4`): read `If-Match` from the aggregate's `get` where the operation's own
  path has no `GET`, for the derived command and the raw call, with tests proven red.
- `T-002` (`AC-CLI-002.4`): distill the delta into `cli.md` and archive this doc.
