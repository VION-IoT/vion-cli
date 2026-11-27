# Releasing

Maintainer reference. Not for public consumption — see the root [README](../README.md) for the
user-facing install page.

## How versions work

Git tags drive versions. There is no `<Version>` in any `.csproj`; a local build is `0.0.0-local`
and CI passes `-p:Version` from the tag ([`compute-version`](https://github.com/VION-IoT/shared-workflows)
in `dotnet-aot-app.yml`).

| Trigger | Version | What ships |
|---|---|---|
| Push to `main`, pull request | `0.0.0-ci.<run>` | nothing: the publishes are built and deleted; a pull request that changes the build files also publishes, to prove the publish |
| Push tag `v0.2.0` | `0.2.0` | the three archives and their checksums, and the two install scripts, on the GitHub release |
| Push tag `v0.2.0-preview.1` | `0.2.0-preview.1` | the same, marked pre-release |

## Cutting a release

Prerequisites: `main` is green on the commit you want to release; `gh auth status` is signed in.

```bash
gh release create v0.2.0 --target main --generate-notes --title "v0.2.0" --notes "Short release summary."
# pre-release: add --prerelease
```

`gh release create` creates the tag and the release in one step. The tag runs
[`ci-cd.yml`](../.github/workflows/ci-cd.yml), which through shared-workflows' `dotnet-aot-app.yml`:

1. Builds and tests the solution, verifies code style, checks the committed lock files for
   dependency majors.
2. Publishes NativeAOT for `linux-musl-x64` and `linux-musl-arm64` in Alpine containers and for
   `win-x64` on a Windows runner. The Linux executables link statically with OpenSSL included
   (`StaticExecutable` and `StaticOpenSslLinking` in `Vion.Cli.csproj`), so one archive per
   architecture runs on glibc and musl hosts with no OpenSSL installed. The two go together: a static
   executable cannot `dlopen`, which is how the runtime otherwise finds the host's `libssl`, so a
   static binary without OpenSSL linked in aborts on its first HTTPS call with "No usable version of
   libssl was found", whatever the host has installed. The static link compiles the runtime's OpenSSL
   shim with `cmake` against `openssl-dev` and `openssl-libs-static`, which the shared workflow's
   `apk add` provides in the publish container; without them the publish fails in `build-local.sh`.
3. Attaches to the release `vion-0.2.0-linux-musl-x64.tar.gz`, `vion-0.2.0-linux-musl-arm64.tar.gz`
   and `vion-0.2.0-win-x64.zip`, each with a `.sha256` in `sha256sum` format. The Linux job and the
   Windows job do not wait for each other, so the release can briefly hold two archives and no zip.
4. Deletes the run's artifacts.

Beside that call, the workflow's own `install-scripts` job attaches `install/install.sh` and
`install/install.ps1` from the tag (§ The install scripts).

The release notes are sectioned by [`release.yml`](../.github/release.yml) from the merged pull
requests' labels: `feat`, `fix`, `chore`, and everything else under Other.

### When the run fails

The run's log names the failing job. The README's install lines and `vion update` follow the latest
release, so while its archives are missing they refuse, naming the archive and installing nothing:
`gh release edit v0.2.0 --prerelease` makes the previous release the latest again until they are
attached, and
`gh release edit v0.2.0 --prerelease=false --latest` restores it.

Nothing is pushed anywhere but the release, so the version is not burned until the archives are
attached:

- A cause outside the release — a runner or network failure — re-runs the failed job on the same tag.
- A fault in the release is fixed on a branch, merged through a pull request as any change, and the
  release cut again at the same number: `gh release delete v0.2.0 --cleanup-tag --yes`, then the
  `gh release create` line above at the fixed `main`. Both move a tag others may have fetched, so a
  maintainer runs them by hand. Once archives were attached, the version is burned: fix forward at
  the next number.

## The install scripts

`install/install.sh` (Linux) and `install/install.ps1` (Windows) are what the README's one-liners
fetch, from `releases/latest/download/`. So a release serves the scripts committed at its tag, and a
change to them reaches users with the next release, not at merge. What they guarantee is
[`specs/cli.md`](specs/cli.md) § Installing and updating.

The `install-scripts` job in [`ci-cd.yml`](../.github/workflows/ci-cd.yml) uploads both on a `v*`
tag with `gh release upload --clobber`. A tag pushed by hand without a release fails the job until
the release exists and the job is re-run. It builds nothing, so the scripts land well before the archives; in between, a script names the
missing archive and installs nothing. A red `install-scripts` is rerun from its page.

Once a release's jobs have finished, both
`https://github.com/VION-IoT/vion-cli/releases/latest/download/install.sh` and `…/install.ps1`
resolve. No suite test runs the scripts: a change to one is checked by running it, on Linux in an
Alpine container and on Windows against a temporary `VION_INSTALL_DIR` and `VION_HOME` with
`VION_NO_MODIFY_PATH` set, so the desk's own PATH is left alone.

## The snapshot is not a release

`scripts/snapshot-pull.ps1` and `scripts/derive.ps1` move the tool to a newer API document; that is
a `chore` pull request whose diff of `Vion.Cli/Derived/Operations.g.cs` and `docs/snapshots/help.txt`
is the review. The derived table carries no response and, of `x-vion-capability`, only `effect`,
`idempotent`, `versioned` and `dryRun`, so the diff of `openapi/v1.json` is read for the rest: an
operation that changes what it answers, for example a `200` with the row becoming a `202` with a
long-running operation, changes what its command prints while the table stays put. The pull request
body names each such operation beside those added, removed and changed. It ships at the next release
like any change. A release never pulls the snapshot.

A move made for a live-check issue proposes its release in two places: the pull request's Summary,
and the REPORT or closing message of the session that made it. Both also name the issue, and who
closes it and when, as § The live check says.

## The live check

[`live-check.yml`](../.github/workflows/live-check.yml) runs every day at 04:17 UTC: it waits up to
twenty minutes for the latest release's `vion-<v>-linux-musl-x64.tar.gz` and its `.sha256` to be
attached (`gh release create` publishes before anything is built, and a scheduled run can start hours
late), downloads them, refuses the archive unless it matches its `.sha256`, and runs
`vion ops --live --env test --json` and `… --env production --json`, tokenless. It covers what the
pull request gates do not: the released snapshot against what each host serves today, and the static
Linux executable's HTTPS over a real network. It sees only what `ops --live` compares,
the derived tables' members: a host that changes no more than what an operation answers, a
member of `x-vion-capability` the table does not carry, a channel's address or description, or
its servers' protocol, leaves it green.

When both are clean, nothing happens. When either is not, the run comments on the open issue
labelled `live-check` in `VION-IoT/architecture`, or opens one titled `vion live check: …` when
none is open, then goes red. This repository is public and gets no issues of its own. The issue
carries the release, each environment's state and exit code, and what `ops --live` printed:

- **differs**: the release is stale against that host. Pull the snapshot the rows' `protocol` names
  (`http`: `scripts/snapshot-pull.ps1`; `mqtt`: `scripts/asyncapi-pull.ps1`), derive, release
  (§ The snapshot is not a release).
- **fails**: the executable could not read one of the host's documents, or the archive was not
  attached within twenty minutes, could not be downloaded or failed its checksum. That points at the
  host, the network, the release's build or assets, or the executable's HTTPS; the download step's
  log says which. A red that clears on the next run was the host, the network, or a release build
  that outran the wait.

Nothing closes the issue on its own: it is closed by hand once a run is clean on both environments.
A clean run posts nothing on the issue; `gh run list --workflow live-check.yml` or the Actions tab
shows it. When a release answers the issue, the maintainer who cuts that release closes it, after a
run that checks it: the next daily run, or a `workflow_dispatch` run once it is the latest full
release with its archives attached. A pre-release is never checked. When no release answers it, for
example a `fails` that clears, the maintainer who sees the clean run closes it. A row that differs
only because production does not yet serve what test does keeps the issue open until production
catches up.

`workflow_dispatch` runs it on demand, from the Actions tab or with `gh workflow run live-check.yml`.
The issue token is the architecture App's (§ Required configuration), minted with Issues: read and
write on `architecture` only; the App's installation must grant that permission, or the token step
is red. GitHub disables a public repository's scheduled workflows after sixty days without a commit,
so a check that has gone quiet is itself a signal to look at: a `workflow_dispatch` run or any
commit re-enables it.

## Required configuration

One-time setup per repository, by a maintainer:

- The organization secrets `ARCHITECTURE_APP_CLIENT_ID` and `ARCHITECTURE_APP_PRIVATE_KEY` — the
  GitHub App that reads the private `architecture` repository and writes its issues — have
  selected-repository visibility; this repository must be in the selection (`gh api -X PUT orgs/VION-IoT/actions/secrets/<name>/repositories/<repo id>`),
  or `gates.yml`'s `vion-sdd` job is red. The App's installation must grant Contents: read, for
  `gates.yml`, and Issues: read and write, for `live-check.yml`'s issue step. No value is copied into the repository.
- Labels `feat`, `fix`, `chore`; squash merging only; branch protection on `main` with one approving
  review and the required checks named after the first run.
