> **Cross-repo work**: this repo is part of the VION platform.
> Architecture state, decisions, and cross-repo specs live in [`../architecture`](https://github.com/VION-IoT/architecture).
> Clone it: `git clone git@github.com:VION-IoT/architecture.git ../architecture`
> Before planning a feature with scope ≥ 2 repos, read the relevant `architecture/systems/*.md`
> and run `/spec <slug> <repos>` from the architecture repo.
> Cross-repo work is dispatched with the `vion-dispatch` plugin
> ([mechanics](https://github.com/VION-IoT/architecture/blob/main/plugins/vion-dispatch/README.md),
> [VION procedure](https://github.com/VION-IoT/architecture/blob/main/runbooks/session-orchestration.md)).
> A session dispatched into this repo ends with `/vion-dispatch:report`.

# vion — the VION platform's operator CLI

`vion` is one NativeAOT binary. Its command tree, options and help are **derived** from a committed
snapshot of cloud-api's OpenAPI document; only sign-in, contexts, rendering and the safety flags are
**written**. This repository is **source-available** (Apache 2.0) but closed to external
contributions — see [CONTRIBUTING.md](CONTRIBUTING.md). Everything committed is public from the
first push: no customer name, cluster-internal host, token or secret enters the tree, the journal
and the help snapshot included.

Detail lives in the convention docs below, loaded when you do the work they govern — **read the
linked doc before doing the matching work, and follow it.**

## Read before you write

| When you're… | Read |
| --- | --- |
| changing what a command does — its options, output, exit code, a flag's rule | [`docs/specs/cli.md`](docs/specs/cli.md), the current truth; its anchor is [`docs/snapshots/help.txt`](docs/snapshots/help.txt) |
| touching `openapi/`, `cli.json`, `Vion.Cli.Derive/` or anything under `Vion.Cli/Derived/` | § The three layers below — the table and the snapshot are regenerated, never edited |
| writing or modifying a test | [`docs/testing-conventions.md`](docs/testing-conventions.md) — the seams, the mutation discipline, how a spec id is cited |
| changing specified behaviour, or dispatching an implementing session | [`docs/spec-process.md`](docs/spec-process.md) — the corpus, the lanes, the gates |
| writing or reworking an inline comment | [`docs/comment-conventions.md`](docs/comment-conventions.md) |
| cutting a release, or the winget manifest | [`docs/releasing.md`](docs/releasing.md) |
| reviewing a change | `/vion-git:review` — it reads [`docs/review-checks.md`](docs/review-checks.md), this repo's named checks |

**Before writing new code, read similar existing files** in the same area and replicate their
structure. Do not invent new patterns; name the precedent you followed.

**Design docs are change docs in [`docs/changes/`](docs/changes/)** per
[`docs/spec-process.md`](docs/spec-process.md); the current-truth spec corpus is
[`docs/specs/`](docs/specs/). Cross-repo specs live in `../architecture/specs/`, never here.

## The three layers

- **The snapshot**, `openapi/v1.json`: cloud-api's document as the test environment serves it,
  pulled by `pwsh scripts/snapshot-pull.ps1`. A fact the document carries wrong is cloud-api's to
  fix and is reported there, never patched in the snapshot.
- **The derived table**, `Vion.Cli/Derived/Operations.g.cs`: emitted by `Vion.Cli.Derive` from the
  snapshot and `cli.json` (presentation: hide, alias), by `pwsh scripts/derive.ps1`, which also
  rebuilds the help snapshot. Committed, so that an API move is a reviewable diff; `-Verify` is
  the gate. A `cli.json` entry the snapshot lacks fails the derive.
- **The written tool**, `Vion.Cli/`: `Program.cs`, `Commands/`, `Http/`, `Rendering/`, `Context/`,
  `Auth/`. One `HttpClient`; payloads are `JsonNode`; nothing per operation is written.

## Working agreement

### Lanes

At the start of a task, answer two questions out loud: is the change local? is a design point open?

- **Fix-sized** — local, nothing open: branch, commit, review, pull request. No document. A change
  that turns out not to be local stops and says so: it is feature-sized.
- **Feature-sized** — a change doc first, in `docs/changes/`. Ratified before code when a question
  in it is open. Archived in the pull request that lands it.

### STOPs

- A STOP is named up front — by the brief, an open question in the change doc, or the lane answer —
  and no other. With none named, human review is on the pull request.
- A STOP is a `partial` REPORT with a question in it.
- A decision nobody named is surfaced, not taken. A hedge in a brief is a STOP when it fails.
- Scope does not widen on its own: a design or naming question gets options and changes nothing
  until the human chooses; work nobody asked for is proposed, not produced.
- A question from the human is a question, not an instruction.
- Anything committed after a `done` REPORT needs a new REPORT.
- A request that breaks a convention of this repo is pushed back on before complying, by name.
- Verification only a human can do is not a STOP: write it as "not run, routes to a human" under the
  pull request's Verification.

### Communication

- Say what was run, not that it worked.
- A claim a decision rests on names its evidence: a command, a file and line, or that it is inferred.
- Promise no notification that cannot be subscribed to.

### Design

- A cross-cutting concern, for example serialization, authorization or versioning, is solved once,
  where every caller passes, never remembered per type, endpoint or call site.
- A boundary nothing outside the repository crosses, such as an unpublished package, is no argument
  for or against a design.

### Never

- Push to or commit on the default branch.
- Force-push.
- Delete a remote branch.
- Merge a pull request.
- Write to Jira without saying so first.
- Paste a secret into chat.

## Skills in this repo

| moment | skill |
|---|---|
| starting work on a change | `/vion-git:branch` |
| a unit of work lands — a task, a criterion, a fixed review finding | `/vion-git:commit` |
| a correction to produced work, tooling that fought or false-passed, upstream that was wrong, a settled point, a grumble | `/vion-improve:journal` |
| editing a file written for the agent — `CLAUDE.md`, a command, a skill, a convention doc, settings | `/vion-improve:harness` |
| the branch is ready for a pull request | `/vion-git:pr` |
| a retro is due, by the count and age `retro` states | `/vion-improve:retro` |
| work changes specified behaviour — a criterion fixed, behaviour new or reshaped | `/vion-sdd:change` |
| a whole area is brought to current truth | `/vion-sdd:pass` |

### Lanes in this repo

The block's **Fix-sized** is lane 1 and **Feature-sized** is lane 2 of
[`docs/spec-process.md`](docs/spec-process.md) § Lanes, which owns all three and what each obliges;
lane 3 is `/vion-sdd:pass`, whose roles are: dispatch `/vion-dispatch:spawn`, report
`/vion-dispatch:report`, ingest `/vion-dispatch:ingest`, git flow `/vion-git:branch`, `commit` and `pr`.
Kind is not lane: the branch prefix names what the change is for release notes, the lane names its
process weight.

### Pre-PR obligations

1. On `*.cs`, `*.csproj`, `Vion.Cli.sln`, `Vion.Cli.sln.DotSettings`, `Directory.Build.props`,
   `.config/dotnet-tools.json`: `/cleanup`; commit what it changes.
2. On `*.cs`, `*.csproj`, `Vion.Cli.sln`, `Directory.Build.props`, `.config/dotnet-tools.json`,
   `packages.lock.json`: `dotnet build Vion.Cli.sln`, then `dotnet test Vion.Cli.sln`.
3. On `openapi/**`, `cli.json`, `Vion.Cli.Derive/**`, `Vion.Cli/**`: `pwsh scripts/derive.ps1 -Verify`.
4. On `docs/specs/**`, `docs/changes/**`, `*.Test/**`:
   `pwsh -NoProfile -File ../architecture/plugins/vion-sdd/scripts/spec-lint.ps1` and
   `…/spec-trace.ps1`; paste each result.

The pull request body follows [`.github/pull_request_template.md`](.github/pull_request_template.md),
which adds `## Spec ids touched` and `## Gates` to the base sections.

### Reader depth

Beyond `/vion-git:pr`'s defaults:

- contract: `docs/specs/**`
- harness: `docs/spec-process.md`, `docs/*-conventions.md`, `docs/releasing.md`
- generated: `docs/snapshots/**`, `Vion.Cli/Derived/**`, `openapi/**`

### Parallel sessions

The main checkout, `C:\_gh\vion-cli`, stays on `main`; a branch lives in `../vion-cli-<key>`
(`/vion-git:branch`).

A new worktree gets nothing copied into it, so it lacks the main checkout's ignored local state:
`.claude/settings.local.json`, the permission mode. A dispatched session's launcher carries it over;
a worktree opened by hand starts without it.

Build, test, `/cleanup` and `derive.ps1` write only inside the checkout they run in, and nothing here
binds a port, so they work in any worktree at once. A live check against the test environment uses
the operator's own `~/.vion/` store, which is per machine, not per checkout: set `VION_HOME` to a
checkout-local directory when two sessions must not share a sign-in.

## How this file stays true

Harness budgets, in bytes of the committed file: this file 10 kB, a `.claude/commands/*.md` 12 kB,
any other `.claude/**/*.md` 6 kB, `docs/testing-conventions.md` 20 kB; `docs/review-checks.md` 15
checks. No gate reads these numbers — enforced by hand.
