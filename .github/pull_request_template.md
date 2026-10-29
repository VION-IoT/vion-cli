> External contributors: this repository is **source-available**. Pull requests from outside the `VION-IoT` organization are not accepted and will be auto-closed. See [CONTRIBUTING.md](../blob/main/CONTRIBUTING.md). For help, contact `info@vion-iot.com`.

<!--
The squash merge keeps the title and this body and nothing else, so what is not here is not
recorded. `docs/spec-process.md` § Lanes says which lane you are in.
-->

## Summary

<!-- One to three sentences: what a user of `vion` sees change, in their words, not the diff's — a
command, an option, an output, an exit code; nothing user-visible is a fine answer. Name the spec,
brief, change doc or issue. -->

## Changes

<!-- At most ten bullets, each a non-obvious decision and its reason; never a file list. -->

## Deviations

<!-- Only when a spec, brief or change doc was given: where the change departs from it. Omit the
section otherwise. -->

## Draft items

<!-- Only when a finding is neither fixed nor stated on a page: one draft Jira item each, for the
operator to file or drop (`docs/spec-process.md` § Routing). Omit the section otherwise. -->

## Verification

<!-- What no check run shows: a test proven red first with the mutation named, a run against the
test environment with its command and its answer, what was not run and why. The last line is
`Reviewed at <sha>: <each finding accepted rather than fixed, with its reason; or "no findings accepted">`. -->

## Spec ids touched

<!-- Every `AC-…` this PR adds, rewords or removes, each with what changed. `None.` where the
change moves no criterion — a process document, a script, a test-only edit. A fix-sized change
carries its page edit here in the same commit set; that page edit is the distill. -->

## Gates

<!-- The pasted output of each pre-PR obligation `CLAUDE.md` names for the paths this PR touches:
`dotnet build`, `dotnet test`, `pwsh scripts/cleanup-code.ps1 -Verify`, `pwsh scripts/derive.ps1
-Verify`, `spec-lint`, `spec-trace` — verbatim, in a fenced block. A gate not run says so and why. -->
