param(
    [string]$Poe1Path,
    [string]$Poe2Path,
    [ValidateSet('both', 'poe1', 'poe2')]
    [string]$Game = 'both',
    [switch]$ValidateOnly
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
trap {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
$template = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'pob_import_bridge.lua') -Raw
if ([string]::IsNullOrWhiteSpace($template)) { throw 'The bundled bridge module is empty.' }
$utf8 = New-Object System.Text.UTF8Encoding($false)
$stamp = (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)

function Assert-Unlinked([string]$path) {
    if ((Test-Path -LiteralPath $path) -and
        ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Linked PoB folders or files are unsupported: $path. Select the real installation folder."
    }
}

function Assert-PobClosed([string]$root) {
    $launcherNames = @(Get-ChildItem -LiteralPath $root -File -Filter '*.exe' -Force | ForEach-Object { $_.Name })
    $launcherNames += @('Path of Building.exe', 'Path of Building Community.exe', 'PathOfBuilding.exe')
    $prefix = $root + '\'
    try { $processes = Get-CimInstance -ClassName Win32_Process -Property Name, ExecutablePath }
    catch { throw "Cannot verify PoB is closed. No repair can proceed: $($_.Exception.Message)" }
    foreach ($process in $processes) {
        if ($process.ExecutablePath -and $process.ExecutablePath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Close the running PoB program in $root and retry. The repair does not close programs."
        }
        if (-not $process.ExecutablePath -and $launcherNames -contains $process.Name) {
            throw "Cannot verify that $($process.Name) is outside the target folder. Close PoB normally and retry."
        }
    }
}

function Replace-Once([string]$text, [string]$old, [string]$new) {
    if ($text.Split(@($old), [StringSplitOptions]::None).Length -ne 2) {
        throw 'Unsupported PoB source; expected patch anchor missing or repeated. No files patched for this installation.'
    }
    return $text.Replace($old, $new)
}

function Assert-Once([string]$text, [string]$part) {
    [void](Replace-Once $text $part $part)
}

if ($Game -eq 'poe1') { $Poe2Path = $null }
if ($Game -eq 'poe2') { $Poe1Path = $null }
if (-not $Poe1Path -and -not $Poe2Path) { throw 'Select at least one PoB folder with -Poe1Path or -Poe2Path.' }
if ($Poe1Path -and $Poe2Path -and [IO.Path]::GetFullPath($Poe1Path).TrimEnd('\') -eq [IO.Path]::GetFullPath($Poe2Path).TrimEnd('\')) {
    throw 'Use different folders for PoB1 and PoB2.'
}

foreach ($entry in @(@{ Path = $Poe1Path; Game = 'poe1'; Port = 49171 }, @{ Path = $Poe2Path; Game = 'poe2'; Port = 49172 })) {
    if (-not $entry.Path) { continue }
    if ($entry.Path -notmatch '^[A-Za-z]:[\\/]' -or $entry.Path -match '["\x00-\x1f]') {
        throw 'Choose an absolute local Windows folder for Path of Building.'
    }
    $root = [IO.Path]::GetFullPath($entry.Path).TrimEnd('\')
    if ($root.Length -le 2 -or -not (Test-Path -LiteralPath $root -PathType Container)) {
        throw "Choose an existing PoB installation folder: $($entry.Path)"
    }
    for ($dir = New-Object IO.DirectoryInfo($root); $null -ne $dir; $dir = $dir.Parent) { Assert-Unlinked $dir.FullName }
    $mainPath = Join-Path $root 'Modules\Main.lua'
    $buildPath = Join-Path $root 'Modules\Build.lua'
    $modulePath = Join-Path $root 'Modules\CompanionBridge.lua'
    $keyPath = Join-Path $root 'Modules\CompanionBridge.key'
    $commonPath = Join-Path $root 'Modules\Common.lua'
    $shaPath = Join-Path $root 'lua\sha2.lua'
    foreach ($path in @((Join-Path $root 'Modules'), (Join-Path $root 'lua'), $mainPath, $buildPath,
            $modulePath, $keyPath, $commonPath, $shaPath)) { Assert-Unlinked $path }
    foreach ($path in @($mainPath, $buildPath, $commonPath, $shaPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing required PoB file: $path" }
    }
    foreach ($path in @($modulePath, $keyPath)) {
        if (Test-Path -LiteralPath $path -PathType Container) { throw "Expected a file, not a directory: $path" }
    }
    Assert-PobClosed $root
    $originalMain = (Get-FileHash -LiteralPath $mainPath).Hash
    $originalBuild = (Get-FileHash -LiteralPath $buildPath).Hash
    $main = [IO.File]::ReadAllText($mainPath)
    $build = [IO.File]::ReadAllText($buildPath)
    $common = [IO.File]::ReadAllText($commonPath)
    if (-not $common.Contains('function coroutine._list()') -or
        -not $common.Contains('active_coroutines[co] = true')) {
        throw "Unsupported PoB coroutine tracking at $root; no source files patched."
    }
    $expectedRoot = if ($entry.Game -eq 'poe1') { 'PathOfBuilding' } else { 'PathOfBuilding2' }
    $otherRoot = if ($entry.Game -eq 'poe1') { 'PathOfBuilding2' } else { 'PathOfBuilding' }
    if ($build.Contains('dbXML[1].elem ~= "' + $otherRoot + '"')) {
        throw "Wrong game: $root is not the selected $($entry.Game) installation."
    }
    Assert-Once $build ('dbXML[1].elem ~= "' + $expectedRoot + '"')
    $newline = if ($main.Contains("`r`n")) { "`r`n" } else { "`n" }
    $loaderAnchor = 'LoadModule("GameVersions")'
    $loader = 'local companionBridge = LoadModule("Modules/CompanionBridge", "' + $entry.Game + '")'
    $frameAnchor = 'self.viewPort = { x = 0, y = 0, width = self.screenW, height = self.screenH }'
    $frameHook = $frameAnchor + $newline + $newline +
        "`tcompanionBridge:Tick(self)" + $newline + "`tif companionBridge:IsBusy() then wipeTable(self.inputEvents) end"
    if (-not $main.Contains('-- Companion import bridge v1')) {
        if ($main.Contains('companionBridge') -or $main.Contains('Modules/CompanionBridge')) {
            throw "Unsupported partial bridge hooks at $mainPath. Restore official files for this PoB version before retrying."
        }
        $main = Replace-Once $main $loaderAnchor ('-- Companion import bridge v1' + $newline + $loader + $newline + $loaderAnchor)
        $main = Replace-Once $main $frameAnchor $frameHook
    } else {
        Assert-Once $main ('-- Companion import bridge v1' + $newline + $loader + $newline + $loaderAnchor)
        Assert-Once $main $frameHook
    }
    foreach ($anchor in @($loaderAnchor, $loader, $frameAnchor, 'companionBridge:Tick(self)', 'companionBridge:IsBusy()',
            'function main:Shutdown()')) { Assert-Once $main $anchor }
    $shutdownHook = 'function main:Shutdown()' + $newline + "`tcompanionBridge:Shutdown()"
    if (-not $main.Contains('companionBridge:Shutdown()')) {
        $main = Replace-Once $main 'function main:Shutdown()' $shutdownHook
    } else {
        Assert-Once $main 'companionBridge:Shutdown()'
        Assert-Once $main $shutdownHook
    }
    $bn = if ($build.Contains("`r`n")) { "`r`n" } else { "`n" }
    $old = "`tfile:write(xmlText)" + $bn + "`tfile:close()"
    $new = "`t-- Companion bridge: check disk write completion" + $bn +
        "`tlocal written = file:write(xmlText)" + $bn +
        "`tlocal closed = file:close()" + $bn +
        "`tif not written or not closed then" + $bn +
        "`t`tmain:OpenMessagePopup(`"Error`", `"Couldn't finish saving the build file.`")" + $bn +
        "`t`treturn true" + $bn +
        "`tend"
    if (-not $build.Contains('-- Companion bridge: check disk write completion')) {
        $build = Replace-Once $build $old $new
    } else {
        Assert-Once $build '-- Companion bridge: check disk write completion'
        Assert-Once $build $new
    }

    $existingKey = Test-Path -LiteralPath $keyPath -PathType Leaf
    $trustedSids = @([Security.Principal.WindowsIdentity]::GetCurrent().User.Value, 'S-1-5-18', 'S-1-5-32-544')
    if ($existingKey) {
        $acl = Get-Acl -LiteralPath $keyPath
        $unexpectedRules = @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]) |
            Where-Object { $_.AccessControlType -eq 'Allow' -and $_.IdentityReference.Value -notin $trustedSids })
        if (-not $acl.AreAccessRulesProtected -or $unexpectedRules.Count -gt 0 -or
            $acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin $trustedSids -or
            (Get-Item -LiteralPath $keyPath -Force).Length -notin @(64, 65, 66)) {
            throw "The existing bridge credential is not private or has an invalid size. It was not changed: $keyPath"
        }
    }
    if ($ValidateOnly) {
        Write-Output "Validated $($entry.Game) bridge compatibility."
        continue
    }
    $updates = @(@{ Path = $modulePath; Text = $template },
        @{ Path = $buildPath; Text = $build; Original = $originalBuild },
        @{ Path = $mainPath; Text = $main; Original = $originalMain })
    foreach ($update in $updates) {
        if (Test-Path -LiteralPath $update.Path -PathType Leaf) {
            try {
                $handle = [IO.File]::Open($update.Path, 'Open', 'ReadWrite', 'None')
                $handle.Dispose()
            } catch { throw "PoB file is locked or not writable: $($update.Path). $($_.Exception.Message)" }
        }
    }
    $applied = @()
    $newKey = $false
    try {
        # Stage and verify all backups before replacing any live files.
        foreach ($update in $updates) {
            $update.Temp = $update.Path + '.companion-stage-' + $stamp
            [IO.File]::WriteAllText($update.Temp, $update.Text, $utf8)
            if (Test-Path -LiteralPath $update.Path -PathType Leaf) {
                $update.Backup = $update.Path + '.companion-backup-' + $stamp
                [IO.File]::Copy($update.Path, $update.Backup, $false)
                $update.Hash = (Get-FileHash -LiteralPath $update.Backup).Hash
                if (($update.Original -and $update.Hash -ne $update.Original) -or
                    $update.Hash -ne (Get-FileHash -LiteralPath $update.Path).Hash) {
                    throw "Source changed during validation or backup: $($update.Path)"
                }
                Write-Output "Backup: $($update.Backup)"
            } elseif ($update.Original) {
                throw "Source disappeared during repair: $($update.Path)"
            }
        }
        Assert-PobClosed $root
        if (-not $existingKey) {
            $bytes = New-Object byte[] 32
            $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
            try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
            $keyBytes = $utf8.GetBytes([BitConverter]::ToString($bytes).Replace('-', '').ToLowerInvariant() + "`n")
            $acl = New-Object Security.AccessControl.FileSecurity
            $acl.SetAccessRuleProtection($true, $false)
            foreach ($value in $trustedSids) {
                $sid = New-Object Security.Principal.SecurityIdentifier($value)
                $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid, 'FullControl', 'Allow')))
            }
            $stream = New-Object IO.FileStream($keyPath, [IO.FileMode]::CreateNew,
                [Security.AccessControl.FileSystemRights]::Write, [IO.FileShare]::None, 4096, [IO.FileOptions]::None, $acl)
            $newKey = $true
            try { $stream.Write($keyBytes, 0, $keyBytes.Length) } finally { $stream.Dispose() }
        }
        foreach ($update in $updates) {
            if ($update.Backup) {
                if ($update.Hash -ne (Get-FileHash -LiteralPath $update.Path).Hash) {
                    throw "PoB source changed during repair: $($update.Path)"
                }
                try { [IO.File]::Replace($update.Temp, $update.Path, [NullString]::Value) }
                catch {
                    if (-not [IO.File]::Exists($update.Path) -or -not [IO.File]::Exists($update.Temp)) { $applied += $update }
                    throw
                }
            } else { [IO.File]::Move($update.Temp, $update.Path) }
            $applied += $update
        }
    } catch {
        $failure = $_.Exception.Message
        $rollbackFailures = @()
        for ($i = $applied.Count - 1; $i -ge 0; $i--) {
            $update = $applied[$i]
            try {
                if ($update.Backup) { [IO.File]::Copy($update.Backup, $update.Path, $true) }
                else { [IO.File]::Delete($update.Path) }
            } catch { $rollbackFailures += $update.Path }
        }
        if ($newKey -and $rollbackFailures.Count -eq 0) {
            try { [IO.File]::Delete($keyPath) } catch { $rollbackFailures += $keyPath }
        }
        if ($rollbackFailures.Count -gt 0) {
            throw "Repair failed and rollback was incomplete for: $($rollbackFailures -join ', '). Do not open PoB until restored from backups. $failure"
        }
        throw "Repair failed; no bridge changes remain. Close PoB and check folder permissions. $failure"
    } finally {
        foreach ($update in $updates) {
            if ($update.Temp -and (Test-Path -LiteralPath $update.Temp)) {
                Remove-Item -LiteralPath $update.Temp -ErrorAction Continue
            }
        }
    }
    Write-Output "Repair wrote bridge files for $($entry.Game). Restart PoB to activate 127.0.0.1:$($entry.Port), then check the authenticated handshake."
}
