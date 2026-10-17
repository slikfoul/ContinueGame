param([string]$Profile = 'Default', [string]$DataRoot = (Join-Path $env:APPDATA 'r2modmanPlus-local\Valheim'))
$ErrorActionPreference = 'Stop'
if ($Profile -notmatch '^[A-Za-z0-9 _-]+$') { throw 'Invalid profile name.' }
function AssertClosed {
    # Overwolf may keep running in the tray after r2modman and the Thunderstore app are closed.
    $blockingProcesses = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match '^(valheim|r2modman.*|Thunderstore.*)$' })
    if ($blockingProcesses.Count) {
        $names = ($blockingProcesses.ProcessName | Sort-Object -Unique) -join ', '
        throw ('Close Valheim and r2modman (and Thunderstore Mod Manager if running) before updating the registered local mod. Still running: ' + $names)
    }
}
AssertClosed
if (-not [IO.Path]::IsPathRooted($DataRoot)) { throw 'DataRoot must be an absolute Valheim manager data directory.' }
$dataRoot = [IO.Path]::GetFullPath($DataRoot)
$profileRoot = Join-Path $dataRoot ('profiles\' + $Profile)
$registryPath = Join-Path $profileRoot 'mods.yml'
if (-not (Test-Path -LiteralPath $registryPath)) { throw 'The existing profile registry was not found.' }
$originalRegistry = [IO.File]::ReadAllBytes($registryPath)
$registryText = [Text.Encoding]::UTF8.GetString($originalRegistry).TrimStart([char]0xFEFF)
$blocks = [regex]::Matches($registryText, '(?ms)^- manifestVersion:.*?(?=^- manifestVersion:|\z)')
$existing = @($blocks | Where-Object { $_.Value -match '(?m)^  name: Slikfoul-ContinueGame\r?$' })
if ($existing.Count -gt 1) { throw 'Duplicate ContinueGame entries exist; leave the registry untouched.' }
$enabled = $existing.Count -eq 0 -or $existing[0].Value -notmatch '(?m)^  enabled: false\r?$'
$trusted = $existing.Count -eq 1 -and $existing[0].Value -match '(?m)^  trustedPackage: true\r?$'
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
$version = $manifest.version_number
if ($existing.Count) {
    $installed = [regex]::Match($existing[0].Value, '(?m)^  versionNumber:\r?\n    major: (\d+)\r?\n    minor: (\d+)\r?\n    patch: (\d+)')
    if (-not $installed.Success) { throw 'Cannot determine installed version; leave the profile untouched.' }
    $installedVersion = [version]('{0}.{1}.{2}' -f $installed.Groups[1].Value,$installed.Groups[2].Value,$installed.Groups[3].Value)
    if ($installedVersion -gt [version]$version) { throw 'Refusing to downgrade a newer local version.' }
}
# Keep manager package links separate from the archive project website.
$managerWebsite = 'https://thunderstore.io/c/valheim/p/Slikfoul/' + $manifest.name + '/'
$archivePath = Join-Path $PSScriptRoot "dist\Slikfoul-ContinueGame-$version.zip"
if (-not (Test-Path -LiteralPath $archivePath)) { throw 'Build the Thunderstore package first.' }
$pluginsRoot = [IO.Path]::GetFullPath((Join-Path $profileRoot 'BepInEx\plugins'))
$manualDirectory = [IO.Path]::GetFullPath((Join-Path $pluginsRoot 'ContinueGame'))
$managerDirectory = [IO.Path]::GetFullPath((Join-Path $pluginsRoot 'Slikfoul-ContinueGame'))
$managedPluginDirectory = [IO.Path]::GetFullPath((Join-Path $managerDirectory 'ContinueGame'))
$cacheRoot = [IO.Path]::GetFullPath((Join-Path $dataRoot 'cache\Slikfoul-ContinueGame'))
$cacheDirectory = [IO.Path]::GetFullPath((Join-Path $cacheRoot $version))
$cacheTrusted = $trusted
$cacheMetadataPath = Join-Path $cacheDirectory 'mm_v2_manifest.json'
if (Test-Path -LiteralPath $cacheMetadataPath) {
    $cacheMetadata = Get-Content -LiteralPath $cacheMetadataPath -Raw | ConvertFrom-Json
    if ($cacheMetadata.PSObject.Properties['trustedPackage']) {
        if ($cacheMetadata.trustedPackage -isnot [bool]) { throw 'Invalid existing cache trust state; leave it untouched.' }
        $cacheTrusted = $cacheMetadata.trustedPackage
    }
}
foreach ($target in @($manualDirectory, $managerDirectory, $managedPluginDirectory)) {
    if (-not $target.StartsWith($pluginsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Resolved plugin path escaped the intended profile.'
    }
}
if (-not $cacheDirectory.StartsWith($cacheRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Resolved cache path escaped this mod cache.'
}
if (Test-Path -LiteralPath $manualDirectory) {
    $manualEntries = @(Get-ChildItem -LiteralPath $manualDirectory -Force)
    if (@($manualEntries | Where-Object { $_.PSIsContainer -or $_.Name -notin @('ContinueGame.dll','ContinueGame.dll.config') }).Count) {
        throw 'The old manual directory has unexpected files; leave it untouched.'
    }
}
$backupDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ('backups\before-local-import-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))))
if (-not $backupDirectory.StartsWith([IO.Path]::GetFullPath($PSScriptRoot) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Backup path escaped the project.'
}
New-Item -ItemType Directory -Path $backupDirectory | Out-Null
[IO.File]::WriteAllBytes((Join-Path $backupDirectory 'mods.yml'), $originalRegistry)
$stagingDirectory = Join-Path $backupDirectory 'new-cache'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $stagingDirectory)
$required = @('manifest.json','README.md','CHANGELOG.md','icon.png','BepInEx/plugins/ContinueGame/ContinueGame.dll')
if (@(Get-ChildItem -LiteralPath $stagingDirectory -Recurse -File).Count -ne $required.Count) { throw 'The archive has unexpected contents.' }
foreach ($relative in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $stagingDirectory $relative))) { throw "Missing archive content: $relative" }
}
foreach ($fileName in @('ContinueGame.dll')) {
    if ((Get-FileHash -LiteralPath (Join-Path $stagingDirectory ('BepInEx\plugins\ContinueGame\' + $fileName))).Hash -ne
        (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot ('bin\Release\' + $fileName))).Hash) {
        throw 'The archive and current build differ.'
    }
}
$parts = $version.Split('.')
$installedAt = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
$managerManifest = [ordered]@{
    manifestVersion = 1
    name = 'Slikfoul-ContinueGame'
    authorName = 'Slikfoul'
    websiteUrl = $managerWebsite
    displayName = 'ContinueGame'
    description = $manifest.description
    gameVersion = '0'
    networkMode = 'both'
    packageType = 'other'
    installMode = 'managed'
    installedAtTime = $installedAt
    loaders = @()
    dependencies = @($manifest.dependencies)
    incompatibilities = @()
    optionalDependencies = @()
    versionNumber = @{ major = [int]$parts[0]; minor = [int]$parts[1]; patch = [int]$parts[2] }
    enabled = $enabled
    icon = Join-Path $cacheDirectory 'icon.png'
    onlineSource = $true
    trustedPackage = $cacheTrusted
}
[IO.File]::WriteAllText((Join-Path $stagingDirectory 'mm_v2_manifest.json'), ($managerManifest | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding $false))
$description = $manifest.description.Replace("'", "''")
$enabledYaml = $enabled.ToString().ToLowerInvariant()
$trustedYaml = $trusted.ToString().ToLowerInvariant()
$entry = @"
- manifestVersion: 1
  name: Slikfoul-ContinueGame
  authorName: Slikfoul
  websiteUrl: '$managerWebsite'
  displayName: ContinueGame
  description: '$description'
  gameVersion: '0'
  networkMode: both
  packageType: other
  installMode: managed
  installedAtTime: $installedAt
  loaders: []
  dependencies:
    - denikson-BepInExPack_Valheim-5.4.2351
  incompatibilities: []
  optionalDependencies: []
  versionNumber:
    major: $($parts[0])
    minor: $($parts[1])
    patch: $($parts[2])
  enabled: $enabledYaml
  onlineSource: true
  trustedPackage: $trustedYaml
"@
if ($existing.Count) {
    $match = $existing[0]
    $updatedRegistry = $registryText.Substring(0,$match.Index) + $entry + [Environment]::NewLine + $registryText.Substring($match.Index + $match.Length)
} else {
    $updatedRegistry = $registryText.TrimEnd() + [Environment]::NewLine + $entry + [Environment]::NewLine
}
$backupManual = Join-Path $backupDirectory 'previous-manual'
$backupManager = Join-Path $backupDirectory 'previous-managed'
$backupCache = Join-Path $backupDirectory 'previous-cache'
$tempRegistryPath = Join-Path $profileRoot ('mods.yml.continue-import-' + [Guid]::NewGuid().ToString('N'))
$registryChanged = $false
$newManagerCreated = $false
$newCacheInstalled = $false
try {
    AssertClosed
    if ((Get-FileHash -LiteralPath $registryPath).Hash -ne (Get-FileHash -LiteralPath (Join-Path $backupDirectory 'mods.yml')).Hash) { throw 'The registry changed during preparation.' }
    # Recursive moves use absolute paths verified above and only this mod's directories.
    if (Test-Path -LiteralPath $manualDirectory) { Move-Item -LiteralPath $manualDirectory -Destination $backupManual }
    if (Test-Path -LiteralPath $managerDirectory) { Move-Item -LiteralPath $managerDirectory -Destination $backupManager }
    if (Test-Path -LiteralPath $cacheDirectory) { Move-Item -LiteralPath $cacheDirectory -Destination $backupCache }
    New-Item -ItemType Directory -Path $cacheRoot -Force | Out-Null
    Move-Item -LiteralPath $stagingDirectory -Destination $cacheDirectory
    $newCacheInstalled = $true
    New-Item -ItemType Directory -Path $managerDirectory | Out-Null
    $newManagerCreated = $true
    foreach ($document in @('manifest.json','README.md','CHANGELOG.md','icon.png')) {
        Copy-Item -LiteralPath (Join-Path $cacheDirectory $document) -Destination (Join-Path $managerDirectory $document)
    }
    Copy-Item -LiteralPath (Join-Path $cacheDirectory 'BepInEx\plugins\ContinueGame') -Destination $managedPluginDirectory -Recurse
    if (-not $enabled) {
        foreach ($file in Get-ChildItem -LiteralPath $managerDirectory -Recurse -File) {
            Rename-Item -LiteralPath $file.FullName -NewName ($file.Name + '.old')
        }
    }
    [IO.File]::WriteAllText($tempRegistryPath, $updatedRegistry, (New-Object Text.UTF8Encoding $false))
    # PowerShell can convert a null string argument to an empty path; use a real backup path.
    [IO.File]::Replace($tempRegistryPath, $registryPath, (Join-Path $backupDirectory 'atomic-registry-backup.yml'))
    $registryChanged = $true
    foreach ($fileName in @('ContinueGame.dll')) {
        $installedFileName = if ($enabled) { $fileName } else { $fileName + '.old' }
        if ((Get-FileHash -LiteralPath (Join-Path $managedPluginDirectory $installedFileName)).Hash -ne
            (Get-FileHash -LiteralPath (Join-Path $cacheDirectory ('BepInEx\plugins\ContinueGame\' + $fileName))).Hash) {
            throw 'Registered plugin differs from the archive.'
        }
    }
    $savedRegistry = [IO.File]::ReadAllText($registryPath)
    $newBlocks = [regex]::Matches($savedRegistry, '(?ms)^- manifestVersion:.*?(?=^- manifestVersion:|\z)')
    $othersBefore = @($blocks | Where-Object { $_.Value -notmatch '(?m)^  name: Slikfoul-ContinueGame\r?$' } | ForEach-Object { $_.Value.TrimEnd() })
    $othersAfter = @($newBlocks | Where-Object { $_.Value -notmatch '(?m)^  name: Slikfoul-ContinueGame\r?$' } | ForEach-Object { $_.Value.TrimEnd() })
    if (($othersBefore -join [Environment]::NewLine) -cne ($othersAfter -join [Environment]::NewLine)) { throw 'Other registry entries changed.' }
    if ([regex]::Matches($savedRegistry, '(?m)^  name: Slikfoul-ContinueGame\r?$').Count -ne 1) { throw 'Expected exactly one manager entry.' }
    if (Test-Path -LiteralPath $manualDirectory) { throw 'The old unregistered copy still exists.' }
    Write-Output "Registered ContinueGame $version as a local mod in $Profile; version, icon, cache and installed files verified."
}
catch {
    if ($registryChanged) { [IO.File]::WriteAllBytes($registryPath, $originalRegistry) }
    if ($newManagerCreated) { Move-Item -LiteralPath $managerDirectory -Destination (Join-Path $backupDirectory 'failed-managed') }
    if ($newCacheInstalled) { Move-Item -LiteralPath $cacheDirectory -Destination (Join-Path $backupDirectory 'failed-cache') }
    if (Test-Path -LiteralPath $backupManager) { Move-Item -LiteralPath $backupManager -Destination $managerDirectory }
    if (Test-Path -LiteralPath $backupManual) { Move-Item -LiteralPath $backupManual -Destination $manualDirectory }
    if (Test-Path -LiteralPath $backupCache) { Move-Item -LiteralPath $backupCache -Destination $cacheDirectory }
    throw
}
finally {
    if (Test-Path -LiteralPath $tempRegistryPath) { Remove-Item -LiteralPath $tempRegistryPath }
}
