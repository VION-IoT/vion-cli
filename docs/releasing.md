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
| Push tag `v0.2.0` | `0.2.0` | the three archives and their checksums on the GitHub release; the winget manifest update |
| Push tag `v0.2.0-preview.1` | `0.2.0-preview.1` | the same, marked pre-release; winget is skipped for a pre-release |

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

The release notes are sectioned by [`release.yml`](../.github/release.yml) from the merged pull
requests' labels: `feat`, `fix`, `chore`, and everything else under Other.

### When the run fails

The run's log names the failing job. Nothing is pushed anywhere but the release, so the version is
not burned until the archives are attached:

- A cause outside the release — a runner or network failure — re-runs the failed job on the same tag.
- A fault in the release is fixed on a branch, merged through a pull request as any change, and the
  release cut again at the same number: `gh release delete v0.2.0 --cleanup-tag --yes`, then the
  `gh release create` line above at the fixed `main`. Both move a tag others may have fetched, so a
  maintainer runs them by hand. Once archives were attached and a winget manifest submitted, the
  version is burned: fix forward at the next number.

## winget

The package is `VION-IoT.vion`, moniker `vion`, a portable zip installer
(`InstallerType: zip`, `NestedInstallerType: portable`, `Commands: [vion]`), so that
`winget install vion` works as `winget install gh` does for `GitHub.cli`.

- **Every release after the first:** [`winget.yml`](../.github/workflows/winget.yml) runs on
  `release: released` — a full release, or a pre-release promoted to one; a pre-release as such never
  fires it. It waits up to twenty minutes for the win-x64 zip to be attached (`gh release create`
  publishes before anything is built, and the Linux archives can land first), downloads `komac` at
  the version and SHA-256 committed in the workflow — refused on a mismatch, so the code handed
  `WINGET_TOKEN` is the one reviewed — and runs `komac sync-fork`, `komac update VION-IoT.vion
  --version <tag without v> --urls <zip url> --release-notes-url <release page> --submit`, then
  `komac cleanup --only-merged`. `WINGET_TOKEN` is a classic personal access token with `public_repo`
  and `workflow` (the fork sync fails intermittently without `workflow`); komac submits from the token
  owner's fork of `microsoft/winget-pkgs`, the one the first submission creates. The pull request it
  opens is reviewed by Microsoft's bots and merges on its own when the manifest validates; the run's
  log carries its URL.
- **Upgrading komac:** a reviewed change to `KOMAC_VERSION` and `KOMAC_SHA256` in the workflow. The
  hash is the release's `SHA256SUMS` entry for `komac-<version>-x86_64-pc-windows-msvc.exe`, confirmed
  by downloading that file and hashing it (`Get-FileHash -Algorithm SHA256`); a hash copied without
  that check pins nothing.
- **Recovery:** a red `winget` run is rerun from its page; nothing else is needed. Three causes are
  expected: a release after the first, cut before the first manifest merged, fails in `komac update`
  until the manifest exists (nothing checks for it — a check did, and went red at random when GitHub
  refused the contents API call); a wait that ran out is rerun once the zip is attached; and GitHub
  can refuse komac's own reads of `winget-pkgs` the same way, which a rerun recovers. The first
  release's own run is the exception: it goes red in `komac update` and stays red, never rerun — its
  version is the hand submission.
- **The first release** of the package is submitted by hand, because the workflow's `komac update`
  adds a version to a package `winget-pkgs` already has: `wingetcreate new <zip url>` generates the
  three manifest files, which are reviewed and submitted with `wingetcreate submit` from the token
  owner's account, which creates the fork komac then uses. The reviewers' first pass takes days.

Verify the result with `winget show VION-IoT.vion` once the manifest merges.

## The snapshot is not a release

`scripts/snapshot-pull.ps1` and `scripts/derive.ps1` move the tool to a newer API document; that is
a `chore` pull request whose diff of `Vion.Cli/Derived/Operations.g.cs` and `docs/snapshots/help.txt`
is the review. The derived table carries no response and, of `x-vion-capability`, only `effect`,
`idempotent`, `versioned` and `dryRun`, so the diff of `openapi/v1.json` is read for the rest: an
operation that changes what it answers, for example a `200` with the row becoming a `202` with a
long-running operation, changes what its command prints while the table stays put. The pull request
body names each such operation beside those added, removed and changed. It ships at the next release
like any change. A release never pulls the snapshot.

## The live check

[`live-check.yml`](../.github/workflows/live-check.yml) runs every day at 04:17 UTC: it downloads
the latest release's `vion-<v>-linux-musl-x64.tar.gz`, refuses it unless it matches its `.sha256`,
and runs `vion ops --live --env test --json` and `… --env production --json`, tokenless. It covers
what the pull request gates do not: the released snapshot against what each host serves today, and
the static Linux executable's HTTPS over a real network. It sees only what `ops --live` compares,
the derived table's members: a host that changes no more than what an operation answers, or a
member of `x-vion-capability` the table does not carry, leaves it green.

When both are clean, nothing happens. When either is not, the run comments on the open issue
labelled `live-check` in `VION-IoT/architecture`, or opens one titled `vion live check: …` when
none is open, then goes red. This repository is public and gets no issues of its own. The issue
carries the release, each environment's state and exit code, and what `ops --live` printed:

- **differs**: the release is stale against that host. Pull the snapshot, derive, release
  (§ The snapshot is not a release), then close the issue by hand.
- **fails**: the executable could not read the host's document, or the archive could not be
  downloaded or failed its checksum. That points at the host, the network, the release's assets or
  the executable's HTTPS; a red that clears on the next run was the host or the network.

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
- GitHub secret `WINGET_TOKEN` — a classic personal access token with `public_repo` and `workflow`,
  on the account whose fork of `microsoft/winget-pkgs` the submissions come from; `winget.yml` fails
  without it. No fork variable: komac uses the token owner's fork.
- Labels `feat`, `fix`, `chore`; squash merging only; branch protection on `main` with one approving
  review and the required checks named after the first run.
