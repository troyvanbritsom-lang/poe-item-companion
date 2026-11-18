param([string]$Setup)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
if (-not $Setup) { $Setup = Join-Path $root "bin\installer\PoE-Item-Companion-Setup-v$version-x64.exe" }
$Setup = (Resolve-Path -LiteralPath $Setup).Path
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{32B6D653-B474-4E34-A27B-855CE884BBDB}_is1'
if (Test-Path -LiteralPath $uninstallKey) {
    throw 'Use a Windows account without an existing installed copy for the installer smoke test.'
}
$test = Join-Path $env:TEMP ('PoE Companion setup test ' + [Guid]::NewGuid().ToString('N'))
$app = Join-Path $test 'Installed app'
$pob1 = Join-Path $test 'PoB for Exile 1'
$pob2 = Join-Path $test 'PoB for Exile 2'
$uninstall = Join-Path $app 'unins000.exe'
$originalLocalAppData = $env:LOCALAPPDATA
$env:LOCALAPPDATA = Join-Path $test 'Fixture LocalAppData'
$options = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOICONS /DIR=`"$app`""
$data = Join-Path $env:LOCALAPPDATA 'PoeItemCompanion\UserData'
$config = Join-Path $data 'config.json'

function Assert($condition, [string]$message) { if (-not $condition) { throw $message } }
function Run([string]$file, [string]$arguments) {
    (Start-Process -FilePath $file -ArgumentList $arguments -Wait -PassThru).ExitCode
}
function SetupWith([string]$name, [string]$arguments) {
    $script:setupLog = Join-Path $test "$name.log"
    Run $Setup "$options /LOG=`"$script:setupLog`" $arguments"
}

$passed = $false
try {
    foreach ($fixture in @(@{ Path = $pob1; BuildRoot = 'PathOfBuilding' }, @{ Path = $pob2; BuildRoot = 'PathOfBuilding2' })) {
        $modules = Join-Path $fixture.Path 'Modules'
        $null = New-Item -ItemType Directory -Force $modules, (Join-Path $fixture.Path 'lua')
        @'
LoadModule("GameVersions")
function main:Shutdown()
end
function main:OnFrame()
    self.viewPort = { x = 0, y = 0, width = self.screenW, height = self.screenH }
end
'@ | Set-Content -LiteralPath (Join-Path $modules 'Main.lua')
        ("dbXML[1].elem ~= `"$($fixture.BuildRoot)`"`r`n`tfile:write(xmlText)`r`n`tfile:close()") |
            Set-Content -LiteralPath (Join-Path $modules 'Build.lua')
        "function coroutine._list()`nactive_coroutines[co] = true" | Set-Content -LiteralPath (Join-Path $modules 'Common.lua')
        '' | Set-Content -LiteralPath (Join-Path $fixture.Path 'lua\sha2.lua')
    }

    $main1 = Join-Path $pob1 'Modules\Main.lua'
    $main2 = Join-Path $pob2 'Modules\Main.lua'
    $original1 = (Get-FileHash -LiteralPath $main1).Hash
    $original2 = (Get-FileHash -LiteralPath $main2).Hash
    $exitCode = SetupWith 'wrong-game' "/POB1=`"$pob2`""
    Assert ($exitCode -ne 0 -and -not (Test-Path -LiteralPath $app)) 'Setup accepted the wrong game.'
    $build1 = Join-Path $pob1 'Modules\Build.lua'
    $originalBuild1 = (Get-FileHash -LiteralPath $build1).Hash
    $locked = [IO.File]::Open($main1, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try { $exitCode = SetupWith 'locked-module' "/POB1=`"$pob1`"" }
    finally { $locked.Dispose() }
    Assert ($exitCode -eq 10 -and (Test-Path -LiteralPath $uninstall)) 'Setup did not report partial success for a locked PoB module.'
    Assert ((Get-FileHash -LiteralPath $main1).Hash -eq $original1 -and (Get-FileHash -LiteralPath $build1).Hash -eq $originalBuild1 -and
        -not (Test-Path -LiteralPath (Join-Path $pob1 'Modules\CompanionBridge.lua')) -and
        -not (Test-Path -LiteralPath (Join-Path $pob1 'Modules\CompanionBridge.key'))) 'A partial bridge installation was not rolled back.'
    Assert ((Run $uninstall '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART') -eq 0) 'Uninstall failed after a partial bridge setup.'
    $exitCode = SetupWith 'poe2-only' "/POB2=`"$pob2`""
    Assert ($exitCode -eq 0 -and -not (Test-Path -LiteralPath (Join-Path $pob1 'Modules\CompanionBridge.key'))) 'PoB2-only install failed or touched PoB1.'
    Assert ((Select-String -LiteralPath $script:setupLog -SimpleMatch 'PoB for PoE2: bridge installed.').Count -ne 0) 'PoB2-only setup did not confirm the bridge.'
    Assert ((Run $uninstall '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART') -eq 0) 'Uninstall failed after PoB2-only setup.'
    $arguments = "/POB1=`"$pob1`" /POB2=`"$pob2`""
    Assert ((SetupWith 'both-games' $arguments) -eq 0) 'Installer failed for both games.'
    $profile = Get-Content -LiteralPath $config -Raw | ConvertFrom-Json
    Assert ($profile.Profiles.Poe1.PobPath -eq $pob1 -and $profile.Profiles.Poe2.PobPath -eq $pob2) 'Selected paths were not saved to the correct profiles.'
    Assert ((Select-String -LiteralPath $script:setupLog -Pattern 'PoB for PoE[12]: bridge installed\.').Count -eq 2) 'Setup did not confirm both bridges.'
    $expected = @('data\campaign_quests.json','data\ggg_passive_ids.tsv','data\poe1_gem_acquisition.json','data\poenavi-LICENSE.txt',
        'installed.mode','NOTICE.md','Poe2ItemCompanion.exe','README.md','scripts\install-pob-bridge.ps1',
        'scripts\pob_import_bridge.lua','scripts\pob_wrapper.lua','unins000.dat','unins000.exe','USER-GUIDE.txt')
    $actual = @(Get-ChildItem -LiteralPath $app -File -Recurse | ForEach-Object { $_.FullName.Substring($app.Length + 1) })
    Assert (-not (Compare-Object $expected $actual)) 'Installed package contents do not match the reviewed allowlist.'
    $guide = Join-Path $app 'USER-GUIDE.txt'
    Assert ((Get-FileHash -LiteralPath $guide).Hash -eq (Get-FileHash -LiteralPath (Join-Path $root 'USER-GUIDE.txt')).Hash) 'Installed User Guide differs from the reviewed file.'
    $exe = Get-Item -LiteralPath (Join-Path $app 'Poe2ItemCompanion.exe')
    Assert ($exe.VersionInfo.ProductVersion -eq $version) 'Installed executable version differs from VERSION.'
    $setupVersion = (Get-Item -LiteralPath $Setup).VersionInfo
    Assert ($setupVersion.FileVersion.Trim() -eq "$version.0" -and $setupVersion.ProductVersion.Trim() -eq $version) 'Installer version differs from VERSION.'
    Assert ((Run $exe.FullName '--test-updates') -eq 0) 'Installed update comparison regression failed.'

    $keyHashes = @{}
    foreach ($fixturePath in @($pob1, $pob2)) {
        $modules = Join-Path $fixturePath 'Modules'
        $key = Join-Path $modules 'CompanionBridge.key'
        $keyHashes[$key] = (Get-FileHash -LiteralPath $key).Hash
        $acl = Get-Acl -LiteralPath $key
        $allowed = @([Security.Principal.WindowsIdentity]::GetCurrent().User.Value,'S-1-5-18','S-1-5-32-544')
        $rules = @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]) |
            Where-Object { $_.AccessControlType -eq 'Allow' })
        Assert ($acl.AreAccessRulesProtected -and -not ($rules | Where-Object { $_.IdentityReference.Value -notin $allowed })) 'The bridge key ACL is not private.'
        $hook = Get-Content -LiteralPath (Join-Path $modules 'Main.lua') -Raw
        Assert ([regex]::Matches($hook, 'companionBridge:Tick\(self\)').Count -eq 1) 'Bridge hook is missing or duplicated.'
        $backupHashes = @(Get-ChildItem -LiteralPath $modules -Filter 'Main.lua.companion-backup-*' | Get-FileHash | ForEach-Object Hash)
        $original = if ($fixturePath -eq $pob1) { $original1 } else { $original2 }
        Assert ($backupHashes -contains $original) 'The original source is missing from the backups.'
        $payload = Join-Path $modules 'CompanionBridge.lua'
        Assert ((Get-FileHash -LiteralPath $payload).Hash -eq (Get-FileHash -LiteralPath (Join-Path $app 'scripts\pob_import_bridge.lua')).Hash) 'Installed bridge payload differs from the bundled file.'
    }

    $dummyBuild = Join-Path $pob2 'Saved build fixture.xml'
    '<PathOfBuilding2/>' | Set-Content -LiteralPath $dummyBuild
    $buildHash = (Get-FileHash -LiteralPath $dummyBuild).Hash
    $configHash = (Get-FileHash -LiteralPath $config).Hash
    $poe1Backups = @(Get-ChildItem -LiteralPath (Split-Path -Parent $main1) -Filter '*.companion-backup-*').Count
    Assert ((SetupWith 'app-only-upgrade' "/COMPANIONUPDATE=1 /POB1=`"$pob1`"") -eq 0) 'App-only upgrade failed.'
    Assert ((Get-FileHash -LiteralPath $config).Hash -eq $configHash) 'App-only upgrade modified existing settings.'
    Assert (@(Get-ChildItem -LiteralPath (Split-Path -Parent $main1) -Filter '*.companion-backup-*').Count -eq $poe1Backups) 'In-app upgrade reapplied a PoB bridge.'
    Assert ((SetupWith 'reapply' $arguments) -eq 0) 'Bridge reapply failed.'
    Assert ((Select-String -LiteralPath $script:setupLog -Pattern 'PoB for PoE[12]: bridge installed\.').Count -eq 2) 'Reapplying the bridges did not succeed.'
    foreach ($key in $keyHashes.Keys) {
        Assert ((Get-FileHash -LiteralPath $key).Hash -eq $keyHashes[$key]) 'Reapplying the bridge rotated an existing private key.'
    }
    Assert ((Run $uninstall '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART') -eq 0) 'Uninstall failed.'
    Assert (-not (Test-Path -LiteralPath $exe.FullName) -and -not (Test-Path -LiteralPath $uninstallKey)) 'Uninstaller left its executable or registration behind.'
    Assert ((Get-FileHash -LiteralPath $config).Hash -eq $configHash -and (Get-FileHash -LiteralPath $dummyBuild).Hash -eq $buildHash) 'Uninstall changed user settings or build.'
    foreach ($key in $keyHashes.Keys) {
        Assert ((Get-FileHash -LiteralPath $key).Hash -eq $keyHashes[$key]) 'Uninstall removed the bridge key from PoB.'
        Assert (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $key) 'CompanionBridge.lua')) 'Uninstall left a PoB hook pointing to a missing module.'
    }
    $passed = $true
    Write-Output 'Installer smoke test passed: wrong game, rollback on locked file, PoB2 only, both games, paths with spaces, exact payload, upgrade, reapply, private ACL, settings/build preservation and uninstall.'
} finally {
    if (Test-Path -LiteralPath $uninstall) {
        $null = Run $uninstall '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART'
    }
    $env:LOCALAPPDATA = $originalLocalAppData
    if ($passed) { Remove-Item -LiteralPath $test -Recurse -Force }
    else { Write-Warning "Fixture and logs retained at $test" }
}
