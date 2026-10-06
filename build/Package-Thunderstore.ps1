param(
    [Parameter(Mandatory = $true)][string] $ProjectDirectory,
    [Parameter(Mandatory = $true)][string] $TargetPath,
    [Parameter(Mandatory = $true)][string] $Configuration,
    [Parameter(Mandatory = $true)][string] $OutputDirectory,
    [switch] $ValidateOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (!$ValidateOnly -and $Configuration -ne 'Release') {
    throw 'Thunderstore ZIPs are only created by Release builds.'
}

$project = (Resolve-Path -LiteralPath $ProjectDirectory).Path
$dll = (Resolve-Path -LiteralPath $TargetPath).Path
$metadata = [Reflection.AssemblyName]::GetAssemblyName($dll)
if ($metadata.Name -ne 'ReferToCompendium') { throw 'Unexpected plugin assembly name.' }
$version = $metadata.Version.ToString(3)
$packageName = '{0}_v{1}.zip' -f $metadata.Name, $version
$destination = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) $packageName

$manifestPath = Join-Path $project 'Thunderstore\manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.name -ne $metadata.Name) { throw 'Manifest name must match the plugin assembly name.' }
if ([string]::IsNullOrWhiteSpace($manifest.description)) { throw 'Manifest description is empty.' }
if (@($manifest.dependencies) -notcontains 'denikson-BepInExPack_Valheim-5.4.2351') {
    throw 'Manifest is missing denikson-BepInExPack_Valheim-5.4.2351.'
}
# The built DLL is the version source. Do not rewrite the source manifest while building.
$manifest.version_number = $version
$utf8 = New-Object System.Text.UTF8Encoding($false)
$manifestBytes = $utf8.GetBytes(($manifest | ConvertTo-Json -Depth 10) + [Environment]::NewLine)

$iconBytes = [IO.File]::ReadAllBytes((Join-Path $project 'Thunderstore\icon.png'))
$pngSignature = [byte[]]@(137, 80, 78, 71, 13, 10, 26, 10)
if ($iconBytes.Length -lt 24) { throw 'icon.png is not a valid PNG.' }
for ($i = 0; $i -lt 8; $i++) {
    if ($iconBytes[$i] -ne $pngSignature[$i]) { throw 'icon.png must be a PNG.' }
}
if ([Text.Encoding]::ASCII.GetString($iconBytes, 12, 4) -ne 'IHDR') { throw 'Missing PNG image header.' }
$width = $iconBytes[16] * 16777216L + $iconBytes[17] * 65536L + $iconBytes[18] * 256L + $iconBytes[19]
$height = $iconBytes[20] * 16777216L + $iconBytes[21] * 65536L + $iconBytes[22] * 256L + $iconBytes[23]
if ($width -ne 256 -or $height -ne 256) { throw "Thunderstore icon.png must be 256x256; found ${width}x${height}." }

# An explicit list includes the English translation while keeping reference DLLs,
# user configs, saved layouts and PDBs out of the ZIP.
$files = [ordered]@{
    'manifest.json' = $manifestBytes
    'README.md' = [IO.File]::ReadAllBytes((Join-Path $project 'README.md'))
    'CHANGELOG.md' = [IO.File]::ReadAllBytes((Join-Path $project 'Thunderstore\CHANGELOG.md'))
    'LICENSE' = [IO.File]::ReadAllBytes((Join-Path $project 'LICENSE'))
    'ThirdPartyNotices.txt' = [IO.File]::ReadAllBytes((Join-Path $project 'ThirdPartyNotices.txt'))
    'icon.png' = $iconBytes
    'ReferToCompendium.dll' = [IO.File]::ReadAllBytes($dll)
    'ReferToCompendium.English.yml' = [IO.File]::ReadAllBytes((Join-Path $project 'translations\English.yml'))
}
foreach ($name in $files.Keys) {
    if ($files[$name].Length -eq 0) { throw "Package input is empty: $name" }
}
if ($ValidateOnly) {
    Write-Host "Validated $packageName inputs ($($files.Count) entries): $($files.Keys -join ', '); icon 256x256. No ZIP written."
    return
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$output = [IO.Path]::GetDirectoryName($destination)
[IO.Directory]::CreateDirectory($output) | Out-Null
# Never expose a partial .zip to a directory watcher. Only the checked archive is
# renamed into its final path; an existing ZIP remains intact if preparation fails.
$temporary = Join-Path $output ('.' + $packageName + '.' + [Guid]::NewGuid().ToString('N') + '.tmp')
try {
    $stream = [IO.File]::Open($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $zip = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($name in $files.Keys) {
                $entry = $zip.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
                $entryStream = $entry.Open()
                try { $entryStream.Write($files[$name], 0, $files[$name].Length) }
                finally { $entryStream.Dispose() }
            }
        }
        finally { $zip.Dispose() }
        $stream.Flush($true)
    }
    finally { $stream.Dispose() }

    $zip = [IO.Compression.ZipFile]::OpenRead($temporary)
    try {
        if ($zip.Entries.Count -ne $files.Count) { throw 'Unexpected ZIP entry count.' }
        foreach ($name in $files.Keys) {
            $entry = $zip.GetEntry($name)
            if ($null -eq $entry) { throw "ZIP is missing $name." }
            $entryStream = $entry.Open()
            $sha = [Security.Cryptography.SHA256]::Create()
            try {
                $archivedHash = [Convert]::ToBase64String($sha.ComputeHash($entryStream))
                $inputHash = [Convert]::ToBase64String($sha.ComputeHash([byte[]]$files[$name]))
                if ($archivedHash -ne $inputHash) { throw "ZIP content differs from its input: $name" }
            }
            finally { $sha.Dispose(); $entryStream.Dispose() }
        }
    }
    finally { $zip.Dispose() }

    # Windows PowerShell converts $null to an empty string for this overload.
    if ([IO.File]::Exists($destination)) { [IO.File]::Replace($temporary, $destination, [NullString]::Value) }
    else { [IO.File]::Move($temporary, $destination) }
    Write-Host "Thunderstore package: $destination ($($files.Count) entries verified)"
}
finally {
    if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
}
