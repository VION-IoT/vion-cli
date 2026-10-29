#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Pull cloud-api's OpenAPI document into the committed snapshot, openapi/v1.json.

.DESCRIPTION
  Reads the public document (no token) from the test environment, the one the tree is derived from,
  re-indents it with two spaces and writes it as UTF-8 without a byte-order mark and with LF line
  endings, so that a diff reads per operation whatever the server's formatting. Member order is kept
  as served: the derive step reads parameters in document order.

  After a pull, run scripts/derive.ps1 and review the diff of Vion.Cli/Derived/Operations.g.cs and
  docs/snapshots/help.txt — that diff is the review of the API move.

.PARAMETER ApiUrl
  The API base to pull from. Default https://api.test.vion.swiss, the environment the snapshot is
  cut from by the spec; another base is for a one-off comparison, never for the committed file.

.EXAMPLE
  pwsh scripts/snapshot-pull.ps1
  pwsh scripts/derive.ps1
#>
[CmdletBinding()]
param(
    [string]$ApiUrl = 'https://api.test.vion.swiss'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$target = Join-Path $repoRoot 'openapi/v1.json'
$url = "$($ApiUrl.TrimEnd('/'))/openapi/v1.json"

Write-Host "Pulling $url"
$raw = (Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 60).Content
if ($raw -is [byte[]]) { $raw = [System.Text.Encoding]::UTF8.GetString($raw) }

$document = [System.Text.Json.JsonDocument]::Parse($raw)
$options = [System.Text.Json.JsonSerializerOptions]::new()
$options.WriteIndented = $true
$options.Encoder = [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping
$pretty = [System.Text.Json.JsonSerializer]::Serialize($document, $options) -replace "`r`n", "`n"

$utf8 = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($target, $pretty + "`n", $utf8)

$operations = 0
foreach ($path in $document.RootElement.GetProperty('paths').EnumerateObject()) {
    foreach ($method in $path.Value.EnumerateObject()) {
        if ($method.Name -in 'get', 'put', 'post', 'delete', 'patch') { $operations++ }
    }
}
$hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Wrote openapi/v1.json: $operations operations, sha256 $hash"
