# Review checks

## From the founding change

Six checks written with the repository, from the design points its first change doc argued.

- CHK-01 — Derived means derived: a hand edit in `Vion.Cli/Derived/`, `docs/snapshots/help.txt` or `openapi/v1.json`; a per-operation branch, name or special case written into `Vion.Cli/` that the table could carry; a presentation fact (hide, alias) outside `cli.json`.
- CHK-02 — Every count is pasted with its command: a number in a change doc, a REPORT or a pull request body that was re-read, carried from an earlier run or composed, instead of recounted at the head it describes.
- CHK-03 — The snapshot diff is the review of an API move: a `chore` that pulls the snapshot without reading what `Operations.g.cs` and `help.txt` gained and lost; a document fact the derive step read wrong, fixed in the snapshot instead of reported to cloud-api.
- CHK-04 — Public from the first push: a token, a secret, a customer name, a cluster-internal host or a person's data in any committed file — code, tests, fixtures, the journal, the help snapshot, a pasted command in a doc.
- CHK-05 — A claim about the document names its operationId: a sentence about what the API takes or answers with no `operationId`, `file:line` or command behind it; a fixture document that does not say which real shape it mirrors.
- CHK-06 — JSON mode is one document: anything but the response body or the `error` object on standard output under `--json`; a notice, prompt or table leaking there; a notice missing from standard error where table mode shows it.

## Added by retro

This group grows by retro.
