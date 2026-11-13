param(
    [string]$Poe1Path = 'D:\Path of Building Community',
    [string]$Poe2Path = 'D:\Path of Building Community (PoE2)'
)
$ErrorActionPreference = 'Stop'
$template = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'pob_import_bridge.lua') -Raw
$utf8 = New-Object System.Text.UTF8Encoding($false)
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'

function Replace-Once([string]$text, [string]$old, [string]$new) {
    if ($text.Split(@($old), [StringSplitOptions]::None).Length -ne 2) {
        throw 'Unsupported PoB source; expected patch anchor missing or repeated. No files patched for this installation.'
    }
    return $text.Replace($old, $new)
}

foreach ($entry in @(@{ Path = $Poe1Path; Game = 'poe1'; Port = 49171 }, @{ Path = $Poe2Path; Game = 'poe2'; Port = 49172 })) {
    $root = [IO.Path]::GetFullPath($entry.Path)
    $mainPath = Join-Path $root 'Modules\Main.lua'
    $buildPath = Join-Path $root 'Modules\Build.lua'
    $modulePath = Join-Path $root 'Modules\CompanionBridge.lua'
    $keyPath = Join-Path $root 'Modules\CompanionBridge.key'
    $main = [IO.File]::ReadAllText($mainPath)
    $build = [IO.File]::ReadAllText($buildPath)
    if (-not (Test-Path -LiteralPath (Join-Path $root 'lua\sha2.lua'))) {
        throw "Missing installed HMAC-SHA256 library at $root; no source files patched."
    }
    $expectedRoot = if ($entry.Game -eq 'poe1') { 'PathOfBuilding' } else { 'PathOfBuilding2' }
    if (-not $build.Contains('dbXML[1].elem ~= "' + $expectedRoot + '"')) { throw "Wrong game at $root" }
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
    # Every preexisting source is backed up before the first write to this installation.
    $updates = @(@{ Path = $mainPath; Text = $main }, @{ Path = $buildPath; Text = $build }, @{ Path = $modulePath; Text = $template })
    foreach ($update in $updates) {
        if (Test-Path -LiteralPath $update.Path) {
            $backup = $update.Path + '.companion-backup-' + $stamp
            Copy-Item -LiteralPath $update.Path -Destination $backup
            Write-Output "Backup: $backup"
        }
    }
    if (-not (Test-Path -LiteralPath $keyPath)) {
        $bytes = New-Object byte[] 32
        $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
        $key = [BitConverter]::ToString($bytes).Replace('-', '').ToLowerInvariant()
        # Set a private ACL on an empty file before writing the secret.
        $stream = [IO.File]::Open($keyPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $stream.Dispose()
        $acl = New-Object Security.AccessControl.FileSecurity
        $acl.SetAccessRuleProtection($true, $false)
        foreach ($sid in @([Security.Principal.WindowsIdentity]::GetCurrent().User,
            (New-Object Security.Principal.SecurityIdentifier('S-1-5-18')),
            (New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')))) {
            $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid, 'FullControl', 'Allow')))
        }
        Set-Acl -LiteralPath $keyPath -AclObject $acl
        [IO.File]::WriteAllText($keyPath, $key + "`n", $utf8)
    }
    foreach ($update in $updates) {
        [IO.File]::WriteAllText($update.Path, $update.Text, $utf8)
        Write-Output "Installed: $($update.Path)"
    }
    Write-Output "Restart $($entry.Game) PoB to activate 127.0.0.1:$($entry.Port). Existing open builds were not touched."
}
