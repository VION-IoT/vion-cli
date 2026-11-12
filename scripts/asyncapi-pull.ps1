#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Pull cloud-api's event document into the committed snapshot, asyncapi/v1.json.

.DESCRIPTION
  Reads the public document (no token) from the test environment, the one the watch commands are
  derived from, re-indents it with two spaces and writes it as UTF-8 without a byte-order mark and
  with LF line endings, so that a diff reads per channel whatever the server's formatting. Member
  order is kept as served: the derive step reads channels and their hand-outs in document order.

  The OpenAPI snapshot has its own script, scripts/snapshot-pull.ps1; each snapshot is pulled by its
  own and by nothing else. A channel names operations of the OpenAPI document, so a pull of one
  without the other can fail the derive step, which says which operation is missing.

  After a pull, run scripts/derive.ps1 and review the diff of Vion.Cli/Derived/Channels.g.cs and
  docs/snapshots/help.txt — that diff is the review of the move.

.PARAMETER ApiUrl
  The API base to pull from. Default https://api.test.vion.swiss, the environment the snapshot is
  cut from; another base is for a one-off comparison, never for the committed file.

.EXAMPLE
  pwsh scripts/asyncapi-pull.ps1
  pwsh scripts/derive.ps1
#>
[CmdletBinding()]
param(
    [string]$ApiUrl = 'https://api.test.vion.swiss'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$target = Join-Path $repoRoot 'asyncapi/v1.json'
$url = "$($ApiUrl.TrimEnd('/'))/asyncapi/v1.json"

Write-Host "Pulling $url"
$raw = (Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 60).Content
if ($raw -is [byte[]]) { $raw = [System.Text.Encoding]::UTF8.GetString($raw) }

$document = [System.Text.Json.JsonDocument]::Parse($raw)
$options = [System.Text.Json.JsonSerializerOptions]::new()
$options.WriteIndented = $true
$options.Encoder = [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping
$pretty = [System.Text.Json.JsonSerializer]::Serialize($document, $options) -replace "`r`n", "`n"

$utf8 = [System.Text.UTF8Encoding]::new($false)
[System.IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
[System.IO.File]::WriteAllText($target, $pretty + "`n", $utf8)

$channels = @($document.RootElement.GetProperty('channels').EnumerateObject()).Count
$hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Wrote asyncapi/v1.json: $channels channels, sha256 $hash"
