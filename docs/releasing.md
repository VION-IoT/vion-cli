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
   `win-x64` on a Windows runner.
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
  `release: published`, waits for the win-x64 zip to be attached (the Linux archives can land first),
  and submits the manifest update to `microsoft/winget-pkgs` through `winget-releaser`, pinned by
  commit because it is handed `WINGET_TOKEN` — a classic personal access token with `public_repo`
  on an account that owns a fork of `microsoft/winget-pkgs`. The pull request it opens is reviewed by
  Microsoft's bots and merges on its own when the manifest validates.
- **The first release** of the package is submitted by hand, because `winget-releaser` updates an
  existing manifest only: `wingetcreate new <zip url>` generates the three manifest files, which are
  reviewed and submitted with `wingetcreate submit`. The reviewers' first pass takes days.

Verify the result with `winget show VION-IoT.vion` once the manifest merges.

## The snapshot is not a release

`scripts/snapshot-pull.ps1` and `scripts/derive.ps1` move the tool to a newer API document; that is
a `chore` pull request whose diff of `Vion.Cli/Derived/Operations.g.cs` and `docs/snapshots/help.txt`
is the review. It ships at the next release like any change. A release never pulls the snapshot.

## Required configuration

One-time setup per repository, by a maintainer:

- GitHub secrets `ARCHITECTURE_APP_CLIENT_ID` and `ARCHITECTURE_APP_PRIVATE_KEY` — the GitHub App
  that reads the private `architecture` repository; `gates.yml`'s `vion-sdd` job is red without them.
- GitHub secret `WINGET_TOKEN` — the personal access token above; `winget.yml` fails without it.
- Labels `feat`, `fix`, `chore`; squash merging only; branch protection on `main` with one approving
  review and the required checks named after the first run.
