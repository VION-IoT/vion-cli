---
slug: install-and-update
status: archived
blocked-on: none           # for parked docs: what's blocking + ref
areas: CLI
author: the VION-477 session, for Jonas Bertsch
created: 2026-11-27
updated: 2026-11-27
supersedes: none           # path of a superseded change doc, or none
---

# Install `vion` with one line, and update it with `vion update`

> Change doc — one in-flight change. The `Spec delta` below is distilled into the current-truth
> pages under `docs/specs/` in the implementation PR, then this doc is archived
> (`spec-change.ps1 archive <slug>`). Process: `docs/spec-process.md`. Never put change narrative
> inline in a spec page — it lives here.

## At a glance

### Summary

VION-477. A person installs `vion` with one line — `curl -fsSL …/install.sh | sh` on Linux,
`irm …/install.ps1 | iex` on Windows — into a per-user folder, with no root and no admin, and from
then on updates it with `vion update`. Both install scripts and `vion update` write an **install
record**; `vion update` replaces only the binary the record names, and refuses any other copy,
naming how to update it instead. Today the README's `winget install vion` does not work (the first
submission, microsoft/winget-pkgs#464839, still awaits a moderator), the Linux route is five lines
ending in `sudo install`, and the daily check only names a newer version.

### Spec implications

`cli.md`'s section "The update check" becomes "Installing and updating". `AC-CLI-007.1`, the daily
notice, gains its wording rule — `vion update` where the record vouches for the running binary, the
release page otherwise — and stops running after `update` itself. Six criteria are new under the
same umbrella: `AC-CLI-007.2` the update, `AC-CLI-007.3` the refusal of an unrecorded copy,
`AC-CLI-007.4` an archive that is missing or fails its checksum, `AC-CLI-007.5` `--check`,
`AC-CLI-007.6` a folder that refuses the new file, `AC-CLI-007.7` the Windows rename-aside. A new
umbrella, `AC-CLI-009`, states the install scripts: what they place and record, what they refuse,
and the PATH. Its three criteria are `GAP`: the suite runs no shell script, so they are proven by
the round trip in the pull request and routed to a human after merge (D15). The help snapshot gains
the `update` page and its line in the root's command list.

### Decisions

- `D1` — The one-line install and `vion update` land in one change: they share where vion lives
  and the record. Ratified: brief § Constraints already decided, 1.
- `D2` — A managed install is recognised by an install record, never by folder rules; `update`
  replaces only a binary the record vouches for. Ratified: brief § Constraints 2.
- `D3` — The scripts are served from the GitHub release, `releases/latest/download/install.sh`
  and `…/install.ps1`; no VION domain. Ratified: brief § Constraints 3.
- `D4` — `--version <x>` pins or rolls back, `--check` only reports, success prints `old → new`;
  the checksum is verified before anything on disk changes; Linux renames a temporary file in the
  same folder over the binary and, where that folder refuses it, prints the exact `sudo` line;
  Windows renames the running binary aside. Ratified: brief § Constraints 4 (the issue's design).
- `D5` — **The install folder** is `VION_INSTALL_DIR` where set; else on Linux `XDG_BIN_HOME`,
  else `~/.local/bin`; on Windows `%LOCALAPPDATA%\Programs\vion`. Decide-and-document. Reason:
  `~/.local/bin` is the XDG user-binary folder that systemd's file hierarchy names and that
  Debian's and Ubuntu's default `~/.profile` already put on PATH once it exists, and the one uv
  installs into; `%LOCALAPPDATA%\Programs\<app>` is where Windows' own per-user installers put an
  application, and a folder of vion's own makes adding it to PATH change nothing but `vion`.
- `D6` — **The record** is `install.json` in vion's home directory (`VION_HOME`, else `~/.vion`,
  as for the credentials), one JSON object holding `path`, the binary's full path, and nothing
  else. Only the install scripts write it: `update` keeps the path, so it leaves the record as it
  is (amendment 1, item 3, decided by the operator). It vouches for
  the running binary when `path` names the same file as the process's own executable path, after
  both are made full paths, compared case-insensitively on Windows. Decide-and-document. Reason:
  uv's receipt is the precedent (D2) — a receipt in the tool's own config folder plus a check that
  the running executable is the one it names. The home directory is the folder the tool already
  owns and tests already isolate. A hash of the binary was considered and left out: the record
  answers *who placed the file at this path, and so who may replace it*; a package manager never
  writes into vion's own folder, so a hash would refuse only a person's own rebuild placed there.
  A record under another `VION_HOME` than the installer saw is no record, and `update` refuses:
  the safe side.
- `D7` — **`update` ignores `VION_NO_UPDATE_CHECK`.** Decide-and-document. The variable silences the
  daily network read that every command would otherwise make; `vion update` is a request made by
  name, and a script that silenced the notice still updates when it asks to.
- `D8` — **Off a terminal and under `--json`**, `update` behaves as on a terminal: it asks nothing,
  because running it is the confirmation. Under `--json` standard output is one document —
  `{ "from", "to", "release", "path", "updated" }` for an update, `{ "running", "release", "newer", "managed",
  "path" }` for `--check`, the `error` object for a refusal — and progress lines go to standard
  error. Every update document carries `release`, the version read, beside `from` and `to`, so a
no-op (`updated: false`, `to` equal to `from`) still says what the release was. The daily notice
never runs after `update`: the process that just replaced itself still
  reports its old version, and would name the release it just installed as newer.
  Decide-and-document.
- `D9` — **Existing copies are left alone.** A hand-placed binary, one at `/usr/local/bin` from the
  old README route, or winget's, has no record, so `update` refuses it (exit 2, before any request)
  naming the install one-liner and, on Windows, `winget upgrade vion`; on Linux, where no package
  manager ships vion, it says to update the copy the way it was installed and that an older
  hand-placed copy, for example in `/usr/local/bin`, can be removed. `update --check` on such a
  copy prints the same text. The one-liner installs a managed copy in the folder of D5; it never deletes or
  overwrites another copy. Where another `vion` comes first on PATH, the script says where it is and
  how to remove it. Decide-and-document. Reason: a copy the tool did not place may belong to a
  package manager that tracks it; deleting it would desynchronise that manager, and adopting it in
  place would write into a folder the manager owns.
- `D10` — **The daily notice** names `run \`vion update\`` where the record vouches for the running
  binary, and keeps today's release-page link otherwise: the tool cannot know which package
  manager placed an unrecorded copy, and naming `vion update` there would lead to its refusal.
  Decide-and-document (the brief leaves the wording to this doc).
- `D11` — **Exit codes**: the refusal of an unrecorded copy is a usage refusal, exit 2, as the
  other refusals made before anything is sent (`AC-CLI-002.5`, `AC-CLI-002.6`); a release that
  cannot be read, an archive that is missing or fails its checksum, and a folder that refuses the
  file are failures, exit 1 (`AC-CLI-003.5`). Already at the target version, or `--check`, is
  exit 0. Decide-and-document.
- `D12` — **The scripts reach every release** through a job of this repository's own in
  `ci-cd.yml`, `install-scripts`, on a `v*` tag: it checks out the tag and runs `gh release upload
  … install/install.sh install/install.ps1 --clobber`, as the shared workflow's jobs upload the
  archives. No shared-workflows change: `dotnet-aot-app.yml@v1` takes no input for extra assets
  and needs none (its two upload jobs, `release-win-x64` and `release-linux-archives`, upload their
  own files only). The job needs no build, so the scripts are attached within a minute of
  `gh release create`; the archives follow when the publishes end. The window in between is the
  one the release process already has (`docs/releasing.md` § Cutting a release): a script that
  meets it fails naming the archive it lacks, installs nothing, and names `VION_VERSION` to pin an
  earlier release. Decide-and-document.
- `D13` — **PATH**: on Windows the script puts the folder first on the *user* PATH (and in the
  running session); on Linux it never edits a shell profile, and prints the `export PATH=…` line to
  add when the folder is not on PATH. Decide-and-document. Reason: the Windows user PATH is one
  registry value a script sets cleanly, and putting vion's own folder first shadows nothing but
  `vion`, including winget's link; a Linux profile is one of several files per shell, and a piped
  script rewriting it is the step people object to. `VION_NO_MODIFY_PATH` leaves the Windows PATH
  alone too, as uv's `UV_NO_MODIFY_PATH` does, for a container, a CI runner or a trial install.
- `D14` — **Two seams join `CliServices`**: `ExecutablePath`, the running binary's path
  (`Environment.ProcessPath` in production), and `Platform`, the release archive's platform suffix
  for this process (`win-x64`, `linux-musl-x64`, `linux-musl-arm64`, or none). The swap test and
  the record test need the first (its doc comment named it as the missing seam); the second makes
  the archive picked a value a test sets. Decide-and-document (`docs/testing-conventions.md` § 2).
- `D15` — **The scripts' criteria are `GAP`.** The suite is MSTest; proving a shell script there
  means spawning `sh` and `pwsh` against a fake release server on both CI runners. The scripts are
  exercised instead by the round trip in the pull request and, after merge, on clean accounts — a
  human's check. Decide-and-document.
- `D16` — **On Windows the replaced binary is moved aside as `vion.exe.old`** and removed at the
  next `update`; where an earlier `.old` is still held by a running process, the aside name takes
  a number of its own, and the next `update` removes every one it can. Decide-and-document.

### Reviewer's questions

1. (b) D6: a record that vouches by path only. Is a person's own rebuild at the recorded path, which
   `update` then replaces, the outcome wanted? The brief's round trip relies on it. OUTCOME: decided
   by the implementing session as recorded; the pull request's human review accepts or changes it.
2. (b) D9: the refusal names the one-liner, which installs a second, managed copy rather than
   adopting the unrecorded one in place. Is "switch to a managed install" the right reading of
   "the one-liner that adopts the copy"? OUTCOME: decided by the implementing session as recorded;
   the pull request's human review accepts or changes it.
3. (b) D11: exit 2 for the unrecorded copy. It is not a mistyped invocation; it is classed with the
   other refusals made before anything is sent. OUTCOME: decided by the implementing session as
   recorded; the pull request's human review accepts or changes it.
4. (b) D15: three `GAP` criteria that no suite test will lift. The alternative is a fake release
   server and a shell per runner in the suite. OUTCOME: decided by the implementing session as
   recorded; the round trips in the pull request's Verification are the evidence, and the human
   review accepts or changes it.

---

## Full design

### The record

```json
{
  "path": "/home/someone/.local/bin/vion"
}
```

Written atomically (a temporary file beside it, moved over it) by the two install scripts. `install.sh`
resolves the folder with `pwd -P`, so the path it records is the one the kernel reports as the
process's executable (`/proc/self/exe` is symlink-free).

### `vion update`

```
vion update [--version <x>] [--check] [--json]
```

1. Refuse `--dry-run` (exit 2), naming `--check`. Read the record; refuse (exit 2, D9) unless it
   vouches for `ExecutablePath`. No request yet. `--check` skips the refusal, reports
   `managed: false` and prints the refusal's text on standard error instead.
2. Refuse (exit 1) when `Platform` is none: no archive is published for this OS and CPU.
3. Read `api.github.com/repos/VION-IoT/vion-cli/releases/latest`, or `…/releases/tags/v<x>`; a 404
   on a tag is "no release v<x>".
4. Compare: with `--version`, equal to the running version is a no-op; without it, a release that
   is not newer than the running one is a no-op (a dev build ahead of the release is never
   downgraded unasked). `--check` reports here and stops.
5. Find the release's assets `vion-<v>-<platform>.tar.gz|.zip` and `.sha256`; a missing one fails
   naming it and the build that attaches it. Download both into memory.
6. Hash the archive (SHA-256) and compare it to the `.sha256`'s first field; a mismatch fails with
   both hashes. Nothing has been written yet.
7. Extract the one entry, `vion` or `vion.exe`, into memory.
8. Swap:
   - Linux: create `.vion.<id>.tmp` in the binary's folder, mode 0755, and rename it over the
     binary — `rename(2)` is atomic, and a running process keeps the old inode.
   - Windows: remove left `*.old` files; rename the binary to `vion.exe.old` (allowed while it
     runs); move the temporary into place; on failure move the old one back.
   - A folder that refuses the temporary file (`UnauthorizedAccessException`): the verified binary
     is written to the system temporary folder instead, and the command fails, printing the line
     that places it — `sudo install -m 0755 <tmp> <path>` on Linux, an administrator
     `Move-Item -Force` on Windows. The binary and the record are untouched.
9. Print `old → new`. The record already names the path, so it is left as it is.

### The install scripts

`install/install.sh` (POSIX `sh`, Linux x64 and arm64) and `install/install.ps1` (Windows PowerShell
5.1 and PowerShell 7). Both: resolve the version (`VION_VERSION`, else the latest release — the
redirect of `releases/latest` for `sh`, the API for PowerShell 5.1, which cannot read a redirect
without following it); download the archive and its `.sha256` into a temporary folder; verify;
extract; place in the folder of D5 by a same-folder rename (Linux) or a rename aside (Windows,
where an earlier install may be running); write the record; PATH (D13); warn about a `vion` that
comes first on PATH (D9); print what was installed where. The PowerShell script runs inside a
script block and never calls `exit`, because `irm | iex` runs it in the caller's own session and
`exit` would close that window.

### Alternatives considered

- Folder rules, uv's precedent rejected by the operator (D2).
- Serving the scripts from a VION domain (D3).
- A self-extracting `vion` that installs itself: there is no `vion` to run before the first install.
- A shared-workflows input for extra release assets: not needed (D12), and it would make the work
  cross-repo.

---

## Drift checkpoints

- 2026-11-27: the Windows round trip found the record write failing after a successful swap, with an
  empty message: a single-file build that is not NativeAOT (the one this desk can link) loads
  `System.Text.Encodings.Web` from its own file on first use, and after the swap that file is the
  other release. The record's text and the command's output are now rendered before the swap
  (`UpdateCommand.RunAsync`). The shipped NativeAOT binaries load nothing lazily; sibling sweep:
  nothing else runs between the swap and the exit but the two writes. Done.
- 2026-11-27: the Linux round trip first targeted v0.7.1 and was refused: that release carries no
  Linux archives (`gh release view v0.7.1 --json assets` lists the win-x64 zip and its `.sha256`
  only), which is `AC-CLI-007.4` meeting a real release. It ran again against v0.7.0.
- 2026-11-27: a background security review of the install-script commit flagged two things in
  `install.sh`, both fixed before the pull request: the staging files had predictable names
  (`$$`) in the install folder and the home directory, now `mktemp`; and the script created
  `~/.vion`, which also holds the credentials, with the default umask, now `umask 077`.
- 2026-11-27: `AC-CLI-007.2`'s "say the running version is current" became "say so": a build ahead
  of the latest release is told it is newer, not current. `AC-CLI-007.4` gained "no archive is
  published for this platform", the refusal before any request where `Platform` is none. Both
  before publication, so the delta lines were reworded with the page.
- 2026-11-27: `VION_NO_MODIFY_PATH` added to `install.ps1` (D13), so the Windows round trip could
  run here without rewriting this desk's PATH.
- 2026-11-27: amendment 1, a review round. The operator decided the record holds `path` only, so
  `update` no longer writes it (`AC-CLI-007.2` MODIFIED; D6). `update --dry-run` had replaced the
  binary, the recursive global being accepted and ignored: it is refused now (`AC-CLI-007.8`
  ADDED). The refusal named winget on Linux: it names winget on Windows only and, on Linux, the
  removal of a hand-placed copy (`AC-CLI-007.3` MODIFIED; D9). `--check` on an unrecorded copy
  pointed at another command for the advice: it prints the refusal's text (`AC-CLI-007.5`
  MODIFIED). A timed-out download reported "cancelled."; it is a no-answer failure. Sibling sweep
  for another recursive global that would change what `update` does when ignored: none. `--wait`
  is documented as ignored where the answer is the outcome, `--timeout` without `--wait` is refused
  by `VionCli`, and the context, token and confirmation options name nothing `update` reads. Done.

---

## Spec delta (to distill)

- MODIFIED AC-CLI-007.1 -> docs/specs/cli.md : WHEN a command other than `update` runs on a terminal and the last check is older than a day THE SYSTEM SHALL read the latest release once, cache the result under its home directory, and name a newer version on standard error, with `vion update` where the install record vouches for the running binary and the release page otherwise.
- ADDED AC-CLI-007.2 -> docs/specs/cli.md : WHEN `update` runs and the install record vouches for the running binary THE SYSTEM SHALL read the latest release, or the one `--version` names, and where it differs from the running version, newer for the latest, download this platform's archive and its `.sha256`, replace the binary at the recorded path with the archive's, write the new version into the record and print the running and the new version as `old → new`, and otherwise say so and change nothing.
- ADDED AC-CLI-007.3 -> docs/specs/cli.md : IF `update` runs with no install record under the home directory, or one naming a binary other than the running one, THEN THE SYSTEM SHALL refuse before any request, naming the running binary, a package manager's upgrade command and the install one-liner, and exit 2.
- ADDED AC-CLI-007.4 -> docs/specs/cli.md : IF no archive is published for this platform, the release carries no archive or no `.sha256` for it, or the archive does not match its `.sha256`, THEN THE SYSTEM SHALL leave the binary and the install record as they were, name what is missing or both checksums, and exit 1.
- ADDED AC-CLI-007.5 -> docs/specs/cli.md : WHEN `update --check` runs THE SYSTEM SHALL report the running version, the latest release or the one `--version` names, whether it is newer and whether the install record vouches for the running binary, change nothing on disk, and exit 0.
- ADDED AC-CLI-007.6 -> docs/specs/cli.md : IF the folder holding the recorded binary refuses the new file THEN THE SYSTEM SHALL leave the binary and the install record as they were, write the verified binary to a temporary file, print the command that places it, `sudo install` on Linux, and exit 1.
- ADDED AC-CLI-007.7 -> docs/specs/cli.md : WHERE the platform is Windows THE SYSTEM SHALL move the binary `update` replaces aside beside it as `<name>.old`, and remove a `.old` left by an earlier `update`.
- MODIFIED AC-CLI-007.2 -> docs/specs/cli.md : WHEN `update` runs and the install record vouches for the running binary THE SYSTEM SHALL read the latest release, or the one `--version` names, and where it differs from the running version, newer for the latest, download this platform's archive and its `.sha256`, replace the binary at the recorded path with the archive's, leave the record as it is and print the running and the new version as `old → new`, and otherwise say so and change nothing.
- MODIFIED AC-CLI-007.3 -> docs/specs/cli.md : IF `update` runs with no install record under the home directory, or one naming a binary other than the running one, THEN THE SYSTEM SHALL refuse before any request, naming the running binary, the install one-liner, `winget upgrade vion` on Windows and on Linux that a copy placed by hand can be removed, and exit 2.
- MODIFIED AC-CLI-007.5 -> docs/specs/cli.md : WHEN `update --check` runs THE SYSTEM SHALL report the running version, the latest release or the one `--version` names, whether it is newer and whether the install record vouches for the running binary, write on standard error what `update` would refuse with where it does not, change nothing on disk, and exit 0.
- ADDED AC-CLI-007.8 -> docs/specs/cli.md : IF `update` is given `--dry-run` THEN THE SYSTEM SHALL refuse before any request, naming `vion update --check`, and exit 2.
- ADDED AC-CLI-009.1 -> docs/specs/cli.md : WHEN `install.sh` or `install.ps1` runs THE SYSTEM SHALL download the latest release's archive for the platform, or `VION_VERSION`'s, verify it against its `.sha256`, place its binary in `VION_INSTALL_DIR`, else `XDG_BIN_HOME` or `~/.local/bin` on Linux and `%LOCALAPPDATA%\Programs\vion` on Windows, and write the install record naming it. GAP: the suite runs no shell script; proven by the pull request's round trip, after merge by a human on clean accounts
- ADDED AC-CLI-009.2 -> docs/specs/cli.md : IF the release carries no archive or `.sha256` for the platform, or the two do not match, or the install folder is not writable THEN THE SYSTEM SHALL place nothing, name what was missing, mismatched or refused, and end unsuccessfully. GAP: the suite runs no shell script; proven by the pull request's round trip, after merge by a human on clean accounts
- ADDED AC-CLI-009.3 -> docs/specs/cli.md : WHEN an install script has placed the binary THE SYSTEM SHALL put the install folder first on the user's PATH on Windows unless `VION_NO_MODIFY_PATH` is set, print the line that adds it on Linux where it is not on PATH, and name any other `vion` that comes first on PATH. GAP: the suite runs no shell script; proven by the pull request's round trip, after merge by a human on clean accounts

---

## Tasks

- `T-001` (`AC-CLI-007.2`, `AC-CLI-007.3`, `AC-CLI-007.4`, `AC-CLI-007.5`, `AC-CLI-007.6`, `AC-CLI-007.7`): the seams, the record, `vion update` and its tests.
- `T-002` (`AC-CLI-007.1`): the daily notice's wording and its silence after `update`.
- `T-003` (`AC-CLI-009.1`, `AC-CLI-009.2`, `AC-CLI-009.3`): `install/install.sh` and `install/install.ps1`.
- `T-004` (`AC-CLI-009.1`): the `install-scripts` job in `ci-cd.yml`; `docs/releasing.md`.
- `T-005`: README § Install; the help snapshot; distill and archive.
