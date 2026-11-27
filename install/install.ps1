# Installs vion for this user on Windows, with no administrator rights:
#
#   irm https://github.com/VION-IoT/vion-cli/releases/latest/download/install.ps1 | iex
#
# It downloads the release's win-x64 zip and its .sha256, refuses the zip unless the two match,
# places vion.exe in the install folder, puts that folder first on the user's PATH, and writes the
# install record that lets `vion update` replace it later. Nothing is placed before the checksum
# matched. Windows PowerShell 5.1 and PowerShell 7 both run it.
#
#   $env:VION_VERSION      a release to install, such as 0.8.0; the latest when unset
#   $env:VION_INSTALL_DIR  the folder to place vion.exe in; %LOCALAPPDATA%\Programs\vion when unset
#   $env:VION_HOME         vion's home directory, where the record goes; %USERPROFILE%\.vion when unset
#   $env:VION_NO_MODIFY_PATH  when set, the user's PATH is left as it is and the script says what to add
#
# The record, $env:VION_HOME\install.json, names the binary's full path: `vion update` replaces only
# a binary a record names. Another vion - winget's, a hand-placed copy - is left as it is; the
# script says when one comes first on PATH.
#
# `irm | iex` runs this text in the caller's own session, where `exit` would close the window, so
# everything runs inside one script block that throws on failure instead.

& {
    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'
    $repo = 'https://github.com/VION-IoT/vion-cli'

    # A failure throws, so a script or a CI step that runs this sees it fail, and the window stays open.
    function Fail([string] $message) {
        throw "vion install: $message"
    }

    # Windows PowerShell 5.1 offers TLS 1.0 by default, which GitHub refuses.
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

    if ([Environment]::OSVersion.Platform -ne 'Win32NT') {
        Fail "this script installs vion on Windows; on Linux run: curl -fsSL $repo/releases/latest/download/install.sh | sh"
    }

    # An Arm64 Windows runs the x64 binary under emulation.
    $arch = $env:PROCESSOR_ARCHITEW6432
    if (-not $arch) { $arch = $env:PROCESSOR_ARCHITECTURE }
    if ($arch -notin @('AMD64', 'ARM64')) {
        Fail "no vion is published for $arch; the release carries win-x64"
    }

    $version = $env:VION_VERSION
    if (-not $version) {
        try {
            $release = Invoke-RestMethod -UseBasicParsing -Uri 'https://api.github.com/repos/VION-IoT/vion-cli/releases/latest' -Headers @{ 'User-Agent' = 'vion-install' }
            $version = $release.tag_name
        }
        catch {
            Fail "could not read the latest release from GitHub: $($_.Exception.Message)"
        }
    }

    $version = $version.Trim() -replace '^[vV]', ''
    if ($version -notmatch '^[0-9A-Za-z.+-]+$') {
        Fail "'$version' is not a release version, such as 0.8.0"
    }

    $dir = $env:VION_INSTALL_DIR
    if (-not $dir) { $dir = Join-Path $env:LOCALAPPDATA 'Programs\vion' }
    $vionHome = $env:VION_HOME
    if (-not $vionHome) { $vionHome = Join-Path $env:USERPROFILE '.vion' }
    $archive = "vion-$version-win-x64.zip"

    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("vion-install-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tmp | Out-Null
    try {
        Write-Host "downloading $archive"

        # A release is published before its build has attached the archives, so for a few minutes
        # the latest release can lack them: a 404 says so, and any other failure names its own cause.
        # Both editions' exceptions carry the answer, when there was one, as .Response.
        foreach ($name in @($archive, "$archive.sha256")) {
            try {
                Invoke-WebRequest -UseBasicParsing -Uri "$repo/releases/download/v$version/$name" -OutFile (Join-Path $tmp $name)
            }
            catch {
                $response = $_.Exception.Response
                if ($response -and [int] $response.StatusCode -eq 404) {
                    Fail "release v$version carries no $name. Its build attaches the archives in the minutes after the release is published: try again shortly, or set `$env:VION_VERSION to an earlier release. Nothing was installed."
                }
                Fail "could not download ${name}: $($_.Exception.Message) Nothing was installed."
            }
        }

        $expected = ((Get-Content -Raw (Join-Path $tmp "$archive.sha256")).Trim() -split '\s+')[0].ToLowerInvariant()
        $actual = (Get-FileHash -Algorithm SHA256 (Join-Path $tmp $archive)).Hash.ToLowerInvariant()
        if ($expected -ne $actual) {
            Fail "$archive does not match its .sha256: the checksum file says $expected, the download hashes to $actual. Nothing was installed."
        }

        Expand-Archive -Path (Join-Path $tmp $archive) -DestinationPath (Join-Path $tmp 'x')
        $binary = Join-Path $tmp 'x\vion.exe'
        if (-not (Test-Path -LiteralPath $binary)) {
            Fail "$archive holds no vion.exe. Nothing was installed."
        }

        try {
            New-Item -ItemType Directory -Force -Path $dir | Out-Null
            $dir = (Resolve-Path -LiteralPath $dir).ProviderPath
            $target = Join-Path $dir 'vion.exe'
            $staged = Join-Path $dir ".vion.$([Guid]::NewGuid().ToString('N')).tmp"
            Copy-Item -LiteralPath $binary -Destination $staged
        }
        catch {
            Fail "could not write to ${dir}: $($_.Exception.Message) Set `$env:VION_INSTALL_DIR to a folder you own. Nothing was installed."
        }

        # A vion.exe still running cannot be overwritten or deleted, but it can be renamed: an
        # earlier one is moved aside, and `vion update` removes the aside file later.
        Get-ChildItem -LiteralPath $dir -Filter 'vion.exe*.old' -ErrorAction SilentlyContinue |
            ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }
        if (Test-Path -LiteralPath $target) {
            $aside = "$target.old"
            for ($n = 1; Test-Path -LiteralPath $aside; $n++) { $aside = "$target.$n.old" }
            Move-Item -LiteralPath $target -Destination $aside
        }
        Move-Item -LiteralPath $staged -Destination $target

        New-Item -ItemType Directory -Force -Path $vionHome | Out-Null
        $record = [ordered]@{
            path        = $target
            version     = $version
            installedBy = 'install.ps1'
            installedAt = [DateTime]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", [Globalization.CultureInfo]::InvariantCulture)
        }
        $recordPath = Join-Path $vionHome 'install.json'
        $recordTemp = "$recordPath.$([Guid]::NewGuid().ToString('N')).tmp"
        [IO.File]::WriteAllText($recordTemp, ($record | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))
        Move-Item -LiteralPath $recordTemp -Destination $recordPath -Force

        Write-Host "installed vion $version at $target"

        # First on the user's PATH, so it runs before a winget link or an older copy there; the
        # folder holds vion alone, so nothing else is shadowed. The running session gets it too.
        # The value is read and written unexpanded, as the registry holds it: an entry such as
        # %USERPROFILE%\bin stays one, where [Environment]::SetEnvironmentVariable would write it out
        # expanded. Setting and clearing a variable of its own afterwards is what tells Explorer, and
        # so every terminal started from it, that the environment changed.
        if ($env:VION_NO_MODIFY_PATH) {
            Write-Host "left PATH as it is (VION_NO_MODIFY_PATH); add $dir to it to run vion by name"
        }
        else {
            $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment', $true)
            try {
                $userPath = [string] $key.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                $entries = @($userPath -split ';' | Where-Object { $_ -and ($_.TrimEnd('\') -ine $dir.TrimEnd('\')) })
                if (-not $userPath -or ($userPath -split ';')[0].TrimEnd('\') -ine $dir.TrimEnd('\')) {
                    $key.SetValue('Path', ((@($dir) + $entries) -join ';'), [Microsoft.Win32.RegistryValueKind]::ExpandString)
                    [Environment]::SetEnvironmentVariable('VION_INSTALL_PATH_CHANGED', '1', 'User')
                    [Environment]::SetEnvironmentVariable('VION_INSTALL_PATH_CHANGED', $null, 'User')
                    Write-Host "put $dir first on your PATH; a new terminal finds vion there"
                }
            }
            finally {
                $key.Close()
            }
            $env:Path = (@($dir) + @($env:Path -split ';' | Where-Object { $_ -and ($_.TrimEnd('\') -ine $dir.TrimEnd('\')) })) -join ';'
        }

        # A new terminal's PATH is the machine PATH, then the user PATH, so a copy found through the
        # machine PATH still runs first.
        $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
        foreach ($entry in @($machinePath -split ';' | Where-Object { $_ })) {
            $other = Join-Path ([Environment]::ExpandEnvironmentVariables($entry)) 'vion.exe'
            if (Test-Path -LiteralPath $other) {
                Write-Host "another vion, $other, comes first on PATH and runs instead of this one; remove it, from an administrator shell where its folder needs it"
                break
            }
        }

        Write-Host 'update it with: vion update'
    }
    finally {
        Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
