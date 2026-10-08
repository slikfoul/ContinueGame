param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'

if (-not $SkipBuild) {
    & dotnet build (Join-Path $PSScriptRoot 'ContinueGame.csproj') -c Release -consoleLoggerParameters:ErrorsOnly
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}

$metadata = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
foreach ($field in @('name', 'version_number', 'website_url', 'description', 'dependencies')) {
    if ($metadata.PSObject.Properties.Name -notcontains $field) { throw "Missing manifest field: $field" }
}
if ($metadata.name -cne 'ContinueGame' -or $metadata.name -notmatch '^[A-Za-z0-9_]{1,128}$') { throw 'Invalid package name.' }
if ($metadata.version_number -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') { throw 'Invalid version.' }
if ($metadata.description.Length -gt 250 -or $metadata.description.Length -eq 0) { throw 'Invalid description length.' }
$changelogPath = & (Join-Path $PSScriptRoot 'SelectPackageChangelog.ps1') -Version $metadata.version_number
if (@($metadata.dependencies).Count -ne 1 -or $metadata.dependencies[0] -cne 'denikson-BepInExPack_Valheim-5.4.2351') {
    throw 'The only package dependency must be BepInExPack Valheim.'
}

$assembly = Join-Path $PSScriptRoot 'bin\Release\ContinueGame.dll'
if (-not (Test-Path -LiteralPath $assembly)) { throw 'Build the DLL first.' }
$version = $metadata.version_number
if ([Reflection.AssemblyName]::GetAssemblyName($assembly).Version.ToString(3) -cne $version) { throw 'Manifest and assembly versions differ.' }
$pluginSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'ContinueGamePlugin.cs') -Raw
if ($pluginSource -notmatch ('\[BepInPlugin\(PluginId, "ContinueGame", "' + [Regex]::Escape($version) + '"\)\]')) {
    throw 'Manifest and plugin versions differ.'
}

# Read PNG signature and IHDR without a platform-specific graphics dependency.
$icon = Join-Path $PSScriptRoot 'icon.png'
$png = [IO.File]::ReadAllBytes($icon)
if ($png.Length -lt 24 -or [BitConverter]::ToString($png[0..7]) -cne '89-50-4E-47-0D-0A-1A-0A') { throw 'The icon must be PNG.' }
$width = [uint32]$png[16] * 16777216 + [uint32]$png[17] * 65536 + [uint32]$png[18] * 256 + $png[19]
$height = [uint32]$png[20] * 16777216 + [uint32]$png[21] * 65536 + [uint32]$png[22] * 256 + $png[23]
if ($width -ne 256 -or $height -ne 256) { throw 'The icon must be exactly 256x256.' }

$distribution = Join-Path $PSScriptRoot 'dist'
$packageRoot = Join-Path $distribution "ContinueGame-$version"
$pluginDirectory = Join-Path $packageRoot 'BepInEx\plugins\ContinueGame'
New-Item -ItemType Directory -Path $pluginDirectory -Force | Out-Null
$sources = [ordered]@{
    'manifest.json' = Join-Path $PSScriptRoot 'manifest.json'
    'README.md' = Join-Path $PSScriptRoot 'README.Thunderstore.md'
    'CHANGELOG.md' = $changelogPath
    'icon.png' = $icon
    'BepInEx/plugins/ContinueGame/ContinueGame.dll' = $assembly
}
foreach ($relative in $sources.Keys) {
    Copy-Item -LiteralPath $sources[$relative] -Destination (Join-Path $packageRoot $relative) -Force
}
$archive = Join-Path $distribution "Slikfoul-ContinueGame-$version.zip"
Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $archive -Force

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    $files = @($zip.Entries | Where-Object { -not $_.FullName.EndsWith('/') })
    if ($files.Count -ne $sources.Count) { throw 'Unexpected files in package.' }
    foreach ($relative in $sources.Keys) {
        $entry = $zip.GetEntry($relative)
        if ($null -eq $entry) { throw "Missing archive entry: $relative" }
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $archiveHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $sha.Dispose(); $stream.Dispose() }
        if ($archiveHash -cne (Get-FileHash -LiteralPath $sources[$relative] -Algorithm SHA256).Hash) {
            throw "Archive content does not match source: $relative"
        }
    }
}
finally { $zip.Dispose() }

Write-Output 'Thunderstore package verified: manifest, versions, changelog order, icon, exact entries and all content hashes.'
Get-Item -LiteralPath $archive | Select-Object FullName, Length
