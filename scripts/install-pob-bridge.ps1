param(
    [string]$Poe1Path,
    [string]$Poe2Path,
    [ValidateSet('both', 'poe1', 'poe2')]
    [string]$Game = 'both',
    [switch]$ValidateOnly
)
$ErrorActionPreference = 'Stop'
$template = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'pob_import_bridge.lua') -Raw
$utf8 = New-Object System.Text.UTF8Encoding($false)
$stamp = (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)

if ($Game -eq 'poe1') { $Poe2Path = $null }
if ($Game -eq 'poe2') { $Poe1Path = $null }
if (-not $Poe1Path -and -not $Poe2Path) {
    throw 'Select at least one PoB folder with -Poe1Path or -Poe2Path.'
}
if ($Poe1Path -and $Poe2Path -and
    [IO.Path]::GetFullPath($Poe1Path).TrimEnd('\') -eq [IO.Path]::GetFullPath($Poe2Path).TrimEnd('\')) {
    throw 'Use different folders for PoB1 and PoB2.'
}

$userSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
$trustedSids = @($userSid.Value, 'S-1-5-18', 'S-1-5-32-544')

function Replace-Once([string]$text, [string]$old, [string]$new) {
    if ($text.Split(@($old), [StringSplitOptions]::None).Length -ne 2) {
        throw 'Unsupported PoB source; expected patch anchor missing or repeated. No files patched for this installation.'
    }
    return $text.Replace($old, $new)
}

foreach ($entry in @(@{ Path = $Poe1Path; Game = 'poe1'; Port = 49171 }, @{ Path = $Poe2Path; Game = 'poe2'; Port = 49172 })) {
    if (-not $entry.Path) { continue }
    if ($entry.Path -notmatch '^[A-Za-z]:[\\/]') {
        throw 'Choose an absolute local Windows folder for Path of Building.'
    }
    $root = [IO.Path]::GetFullPath($entry.Path)
    $mainPath = Join-Path $root 'Modules\Main.lua'
    $buildPath = Join-Path $root 'Modules\Build.lua'
    $modulePath = Join-Path $root 'Modules\CompanionBridge.lua'
    $keyPath = Join-Path $root 'Modules\CompanionBridge.key'
    $shaPath = Join-Path $root 'lua\sha2.lua'
    $commonPath = Join-Path $root 'Modules\Common.lua'
    foreach ($path in @($root, (Join-Path $root 'Modules'), (Join-Path $root 'lua'), $mainPath, $buildPath,
            $modulePath, $keyPath, $shaPath, $commonPath)) {
        if ((Test-Path -LiteralPath $path) -and
            ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Linked PoB folders or module files are not supported. Choose the real installation folder.'
        }
    }
    if (-not (Test-Path -LiteralPath $mainPath -PathType Leaf) -or -not (Test-Path -LiteralPath $buildPath -PathType Leaf)) {
        throw 'Select the PoB installation folder containing Modules\Main.lua and Modules\Build.lua.'
    }
    $main = [IO.File]::ReadAllText($mainPath)
    $build = [IO.File]::ReadAllText($buildPath)
    if (-not (Test-Path -LiteralPath $shaPath -PathType Leaf)) {
        throw "Missing installed HMAC-SHA256 library at $root; no source files patched."
    }
    $common = [IO.File]::ReadAllText($commonPath)
    if (-not $common.Contains('function coroutine._list()') -or
        -not $common.Contains('active_coroutines[co] = true')) {
        throw "Unsupported PoB coroutine tracking at $root; no source files patched."
    }
    $expectedRoot = if ($entry.Game -eq 'poe1') { 'PathOfBuilding' } else { 'PathOfBuilding2' }
    $otherRoot = if ($entry.Game -eq 'poe1') { 'PathOfBuilding2' } else { 'PathOfBuilding' }
    if (-not $build.Contains('dbXML[1].elem ~= "' + $expectedRoot + '"') -or
        $build.Contains('dbXML[1].elem ~= "' + $otherRoot + '"')) { throw "Wrong game at $root" }
    $newline = if ($main.Contains("`r`n")) { "`r`n" } else { "`n" }
    if (-not $main.Contains('-- Companion import bridge v1')) {
        $main = Replace-Once $main 'LoadModule("GameVersions")' (
            '-- Companion import bridge v1' + $newline +
            'local companionBridge = LoadModule("Modules/CompanionBridge", "' + $entry.Game + '")' + $newline +
            'LoadModule("GameVersions")')
        $anchor = 'self.viewPort = { x = 0, y = 0, width = self.screenW, height = self.screenH }'
        $main = Replace-Once $main $anchor ($anchor + $newline + $newline +
            "`tcompanionBridge:Tick(self)" + $newline +
            "`tif companionBridge:IsBusy() then wipeTable(self.inputEvents) end")
    } elseif (-not $main.Contains('local companionBridge = LoadModule("Modules/CompanionBridge", "' + $entry.Game + '")') -or
        -not $main.Contains('companionBridge:Tick(self)')) {
        throw "Incomplete existing bridge hook at $mainPath; restore the original backup before reapplying."
    }
    if (-not $main.Contains('companionBridge:Shutdown()')) {
        $anchor = 'function main:Shutdown()'
        $main = Replace-Once $main $anchor ($anchor + $newline + "`tcompanionBridge:Shutdown()")
    }
    if (-not $build.Contains('-- Companion bridge: check disk write completion')) {
        $bn = if ($build.Contains("`r`n")) { "`r`n" } else { "`n" }
        $old = "`tfile:write(xmlText)" + $bn + "`tfile:close()"
        $new = "`t-- Companion bridge: check disk write completion" + $bn +
            "`tlocal written = file:write(xmlText)" + $bn +
            "`tlocal closed = file:close()" + $bn +
            "`tif not written or not closed then" + $bn +
            "`t`tmain:OpenMessagePopup(`"Error`", `"Couldn't finish saving the build file.`")" + $bn +
            "`t`treturn true" + $bn +
            "`tend"
        $build = Replace-Once $build $old $new
    }
    $existingKey = Test-Path -LiteralPath $keyPath
    if ($existingKey) {
        $acl = Get-Acl -LiteralPath $keyPath
        $unexpectedRules = @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]) |
            Where-Object { $_.AccessControlType -eq 'Allow' -and $_.IdentityReference.Value -notin $trustedSids })
        if (-not $acl.AreAccessRulesProtected -or $unexpectedRules.Count -gt 0 -or
            $acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin $trustedSids -or
            (Get-Item -LiteralPath $keyPath -Force).Length -gt 66 -or
            [IO.File]::ReadAllText($keyPath).Trim() -cnotmatch '^[a-f0-9]{64}$') {
            throw 'Existing bridge key is invalid or not private to this Windows user. Restore the original PoB files and remove the old bridge before retrying.'
        }
    }
    if ($ValidateOnly) {
        Write-Output "Validated $($entry.Game) bridge compatibility."
        continue
    }

    # Stage all writes and backups before replacing any live Lua source.
    $updates = @(@{ Path = $mainPath; Text = $main }, @{ Path = $buildPath; Text = $build }, @{ Path = $modulePath; Text = $template })
    $applied = @()
    $newKey = $false
    try {
        foreach ($update in $updates) {
            $update.Temp = $update.Path + '.companion-stage-' + $stamp
            [IO.File]::WriteAllText($update.Temp, $update.Text, $utf8)
            if (Test-Path -LiteralPath $update.Path) {
                $update.Backup = $update.Path + '.companion-backup-' + $stamp
                [IO.File]::Copy($update.Path, $update.Backup, $false)
                if ((Get-FileHash -LiteralPath $update.Backup).Hash -ne (Get-FileHash -LiteralPath $update.Path).Hash) {
                    throw "Source backup verification failed: $($update.Backup). No source files written for this installation."
                }
                Write-Output "Backup: $($update.Backup)"
            }
        }
        if (-not $existingKey) {
            $bytes = New-Object byte[] 32
            $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
            try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
            $key = [BitConverter]::ToString($bytes).Replace('-', '').ToLowerInvariant()
            $acl = New-Object Security.AccessControl.FileSecurity
            $acl.SetAccessRuleProtection($true, $false)
            foreach ($value in $trustedSids) {
                $sid = New-Object Security.Principal.SecurityIdentifier($value)
                $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid, 'FullControl', 'Allow')))
            }
            $stream = New-Object IO.FileStream($keyPath, [IO.FileMode]::CreateNew,
                [Security.AccessControl.FileSystemRights]::Write, [IO.FileShare]::None, 4096, [IO.FileOptions]::None, $acl)
            $newKey = $true
            try {
                $keyBytes = $utf8.GetBytes($key + "`n")
                $stream.Write($keyBytes, 0, $keyBytes.Length)
            } finally { $stream.Dispose() }
        }
        foreach ($update in @($updates[2], $updates[1], $updates[0])) {
            if ($update.Backup) { [IO.File]::Replace($update.Temp, $update.Path, [NullString]::Value) }
            else { [IO.File]::Move($update.Temp, $update.Path) }
            $applied += $update
        }
    } catch {
        $failure = $_.Exception.Message
        $rollbackFailed = $false
        for ($i = $applied.Count - 1; $i -ge 0; $i--) {
            $update = $applied[$i]
            try {
                if ($update.Backup) { [IO.File]::Copy($update.Backup, $update.Path, $true) }
                else { [IO.File]::Delete($update.Path) }
            } catch { $rollbackFailed = $true }
        }
        if ($newKey -and -not $rollbackFailed) {
            try { [IO.File]::Delete($keyPath) } catch { $rollbackFailed = $true }
        }
        if ($rollbackFailed) { throw "Bridge not installed. Rollback incomplete; restore the original PoB files before opening PoB. $failure" }
        throw "Bridge not installed. Close PoB and ensure you can write to its installation, or keep Setup app-only. $failure"
    } finally {
        foreach ($update in $updates) {
            if ($update.Temp -and (Test-Path -LiteralPath $update.Temp)) {
                Remove-Item -LiteralPath $update.Temp -ErrorAction Continue
            }
        }
    }
    Write-Output "Restart $($entry.Game) PoB to activate 127.0.0.1:$($entry.Port). Existing open builds were not touched."
}
