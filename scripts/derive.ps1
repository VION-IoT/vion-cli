#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Regenerate everything derived from the committed document: the operations table and the help
  snapshot. With -Verify, fail when either differs from what is committed (the CI gate).

.DESCRIPTION
  Three steps, in order:
    1. Vion.Cli.Derive reads openapi/v1.json and cli.json and emits Vion.Cli/Derived/Operations.g.cs.
       A cli.json entry the document lacks fails here.
    2. Vion.Cli is built, so the table just emitted is the one compiled in.
    3. The built tool writes every command's --help to docs/snapshots/help.txt through its hidden
       __help-snapshot command, which walks its own tree — so a new command or option is inside the
       gate by construction.

  There is no MSBuild target behind this: both outputs are committed source, a build never needs to
  regenerate them, and what must hold is that they match the committed document and cli.json —
  which -Verify holds, in CI and before a pull request.

.PARAMETER Verify
  CI mode: refuse to run on a tree with uncommitted changes to the derived paths (exit 2, as
  cleanup-code.ps1 -Verify refuses), then fail with the diff when a step changed them (exit 1).

.EXAMPLE
  pwsh scripts/derive.ps1
  Regenerate, then review `git diff -- Vion.Cli/Derived docs/snapshots` and commit.

.EXAMPLE
  pwsh scripts/derive.ps1 -Verify
  The pull request's gate.
#>
[CmdletBinding()]
param(
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$derivedPaths = @('Vion.Cli/Derived/Operations.g.cs', 'docs/snapshots/help.txt')

Push-Location $repoRoot
try {
    if ($Verify) {
        # The drift signal below is `git status` over the derived paths, which cannot tell a regeneration's
        # rewrite from an edit already in the tree. Refuse up front, naming the cause, rather than report
        # drift that was never about derivation.
        $dirty = @(git status --porcelain -- $derivedPaths)
        if ($dirty.Count -gt 0) {
            Write-Host "-Verify needs a clean tree on the derived paths: $($dirty.Count) file(s) have uncommitted changes."
            $dirty | ForEach-Object { Write-Host "    $_" }
            exit 2
        }
    }

    Write-Host 'Deriving Vion.Cli/Derived/Operations.g.cs from openapi/v1.json and cli.json'
    dotnet run --project Vion.Cli.Derive/Vion.Cli.Derive.csproj -c Debug -- openapi/v1.json cli.json Vion.Cli/Derived
    if ($LASTEXITCODE -ne 0) { Write-Host 'derive: the derive step failed.'; exit 1 }

    Write-Host 'Building Vion.Cli'
    dotnet build Vion.Cli/Vion.Cli.csproj -c Debug --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { Write-Host 'derive: the build failed.'; exit 1 }

    Write-Host 'Writing docs/snapshots/help.txt'
    dotnet run --project Vion.Cli/Vion.Cli.csproj -c Debug --no-build -- __help-snapshot --out docs/snapshots/help.txt
    if ($LASTEXITCODE -ne 0) { Write-Host 'derive: the help snapshot failed.'; exit 1 }

    $drift = @(git status --porcelain -- $derivedPaths)
    if ($Verify) {
        if ($drift.Count -gt 0) {
            Write-Host "::error::Derived files drift from the committed document. Run 'pwsh scripts/derive.ps1' and commit the result."
            git --no-pager diff --stat -- $derivedPaths
            $drift | Where-Object { $_ -match '^\?\?' } | ForEach-Object { Write-Host "  untracked: $_" }
            exit 1
        }
        Write-Host 'Derived files: up to date.'
    }
    elseif ($drift.Count -gt 0) {
        Write-Host "derive.ps1 regenerated files - review with 'git diff' and commit:"
        git --no-pager diff --stat -- $derivedPaths
    }
    else {
        Write-Host 'Already up to date - nothing to do.'
    }
}
finally {
    Pop-Location
}
