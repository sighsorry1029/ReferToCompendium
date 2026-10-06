param(
    [string] $SnapshotRoot = 'C:\Users\blizz\.codex\references\valheim\snapshots\client-b25527674-windows-x64-20260925T211211Z',
    [string] $BepInExCore = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim\BepInEx\core',
    [string] $ModDll = "$PSScriptRoot\..\..\bin\Debug\ReferToCompendium.dll"
)
$ErrorActionPreference = 'Stop'
$ModDll = (Resolve-Path -LiteralPath $ModDll).Path
$SnapshotRoot = (Resolve-Path -LiteralPath $SnapshotRoot).Path
$game = Join-Path $SnapshotRoot 'original'
$managed = Join-Path $game 'valheim_Data\Managed'
$runtime = Join-Path $game 'MonoBleedingEdge\EmbedRuntime'
$config = Join-Path $game 'MonoBleedingEdge\etc'
$manifest = Get-Content -LiteralPath (Join-Path $SnapshotRoot 'manifest.json') -Raw | ConvertFrom-Json
Write-Host "Original snapshot: $($manifest.role) build $($manifest.buildId)"
foreach ($relative in @('valheim_Data/Managed/assembly_valheim.dll', 'valheim_Data/Managed/UnityEngine.CoreModule.dll', 'valheim_Data/Managed/mscorlib.dll', 'valheim_Data/Managed/System.Runtime.Serialization.dll', 'MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll')) {
    $entry = @($manifest.files | Where-Object { $_.path -eq $relative })
    if ($entry.Count -ne 1) { throw "Snapshot manifest entry is missing or ambiguous: $relative" }
    $hash = (Get-FileHash -LiteralPath (Join-Path $game $relative) -Algorithm SHA256).Hash
    if ($hash -ne $entry[0].sha256) { throw "Original snapshot hash mismatch: $relative" }
    Write-Host "$relative SHA256: $hash"
}
Write-Host "Mod: $ModDll; SHA256: $((Get-FileHash -LiteralPath $ModDll -Algorithm SHA256).Hash)"
Write-Host "Harmony SHA256: $((Get-FileHash -LiteralPath (Join-Path $BepInExCore '0Harmony.dll') -Algorithm SHA256).Hash)"
$framework = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$sdk = @(& dotnet --list-sdks | Select-Object -Last 1)[0]
if ($sdk -notmatch '^([^ ]+) \[(.+)\]$') { throw 'A .NET SDK is required.' }
$compiler = Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn\bincore\csc.dll'
$directory = Join-Path ([IO.Path]::GetTempPath()) ('ReferToCompendium-Mono-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory | Out-Null
$hostExe = Join-Path $directory 'CompatibilityMonoHost.exe'
$probe = Join-Path $directory 'CompatibilityProbe.dll'
$stateChecks = Join-Path $directory 'LayoutStateChecks.exe'
$translationChecks = Join-Path $directory 'TranslationChecks.exe'
$references = @('mscorlib.dll', 'System.dll', 'System.Core.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
& dotnet $compiler /nologo /noconfig /nostdlib+ /langversion:latest @references /target:exe /platform:x64 "/out:$hostExe" "$PSScriptRoot\CompatibilityMonoHost.cs"
if ($LASTEXITCODE) { throw 'Mono host compilation failed.' }
$gameReferences = @('assembly_valheim.dll', 'assembly_utils.dll', 'assembly_guiutils.dll', 'UnityEngine.CoreModule.dll', 'netstandard.dll') | ForEach-Object { '/reference:' + (Join-Path $managed $_) }
$harmonyReference = '/reference:' + (Join-Path $BepInExCore '0Harmony.dll')
& dotnet $compiler /nologo /noconfig /nostdlib+ /langversion:latest @references @gameReferences $harmonyReference /target:library "/out:$probe" "$PSScriptRoot\CompatibilityProbe.cs"
if ($LASTEXITCODE) { throw 'Mono probe compilation failed.' }
$serialization = '/reference:' + (Join-Path $framework 'System.Runtime.Serialization.dll')
$xml = '/reference:' + (Join-Path $framework 'System.Xml.dll')
& dotnet $compiler /nologo /noconfig /nostdlib+ /langversion:latest /nullable:enable /warnaserror+ @references $serialization $xml /target:exe "/out:$stateChecks" "$PSScriptRoot\StateCheckUsings.cs" "$PSScriptRoot\..\LayoutStateChecks\Program.cs" "$PSScriptRoot\..\..\LayoutState.cs" "$PSScriptRoot\..\..\LayoutStore.cs"
if ($LASTEXITCODE) { throw 'Layout state checks compilation failed.' }
& dotnet build "$PSScriptRoot\..\TranslationChecks\TranslationChecks.csproj" -c Debug -p:TreatWarningsAsErrors=true --nologo
if ($LASTEXITCODE) { throw 'Translation checks compilation failed.' }
# Copy only the test executable: YAML must resolve from the merged plugin itself.
Copy-Item -LiteralPath "$PSScriptRoot\..\TranslationChecks\bin\Debug\net48\TranslationChecks.exe" -Destination $translationChecks
$isolatedMod = Join-Path $directory 'ReferToCompendium.dll'
Copy-Item -LiteralPath $ModDll -Destination $isolatedMod
Get-ChildItem -LiteralPath $BepInExCore -Filter *.dll | Copy-Item -Destination $directory
$stdout = Join-Path $directory 'stdout.txt'
$stderr = Join-Path $directory 'stderr.txt'
$arguments = @($runtime, $managed, $config, $probe, $isolatedMod, $stateChecks, $translationChecks) | ForEach-Object { '"' + $_ + '"' }
$process = $null
try {
    $process = Start-Process -FilePath $hostExe -ArgumentList $arguments -WorkingDirectory $directory -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $null = $process.Handle
    if (!$process.WaitForExit(30000)) { $process.Kill(); $process.WaitForExit(); throw 'Mono probe timed out.' }
    Get-Content -LiteralPath $stdout, $stderr
    if ($process.ExitCode) { throw "Mono probe failed with exit code $($process.ExitCode)." }
}
finally {
    if ($null -ne $process) {
        if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $process.Dispose()
    }
    Write-Host "Test artifacts retained: $directory"
}
