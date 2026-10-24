param([string]$Repository = 'Laficia/HuntModelSwapCN')
$ErrorActionPreference = 'Stop'
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Invalid repository name.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$release = Join-Path $taskRoot 'src/HuntModelSwapCN/bin/Release'
$manifest = Get-Content -LiteralPath (Join-Path $release 'HuntModelSwapCN.json') -Raw | ConvertFrom-Json
if ($manifest.InternalName -ne 'HuntModelSwapCN' -or $manifest.DalamudApiLevel -ne 15) { throw 'Unexpected manifest.' }
$version = $manifest.AssemblyVersion
if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Invalid version.' }
$staging = Join-Path $taskRoot "dist/release-staging/v$version"
New-Item -ItemType Directory -Path $staging -Force | Out-Null
$download = "https://github.com/$Repository/releases/download/v$version/latest.zip"
$index = $manifest | ConvertTo-Json -Depth 10 | ConvertFrom-Json
foreach ($property in @{
    RepoUrl = "https://github.com/$Repository"
    DownloadLinkInstall = $download
    DownloadLinkUpdate = $download
    DownloadLinkTesting = $download
    LastUpdate = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    IsHide = $false
    IsTestingExclusive = $false
}.GetEnumerator()) { $index | Add-Member NoteProperty $property.Key $property.Value -Force }
$indexText = ConvertTo-Json -InputObject @($index) -Depth 10
[IO.File]::WriteAllText((Join-Path $taskRoot 'pluginmaster.json'), $indexText, [Text.UTF8Encoding]::new($false))

Add-Type -AssemblyName System.IO.Compression.FileSystem
function Write-Archive([string]$Destination, [hashtable]$Files) {
    $stream = [IO.File]::Open($Destination, [IO.FileMode]::Create)
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $Files.GetEnumerator() | Sort-Object Key) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $entry.Value, $entry.Key,
                [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $zip.Dispose(); $stream.Dispose() }
}
$packageFiles = @{}
foreach ($name in @('HuntModelSwapCN.dll','HuntModelSwapCN.json','HuntModelSwapCN.deps.json')) {
    $packageFiles[$name] = Join-Path $release $name
}
foreach ($name in @('README.md','RESEARCH.md','LICENSE','THIRD_PARTY_NOTICES.md')) { $packageFiles[$name] = Join-Path $taskRoot $name }
Write-Archive (Join-Path $staging 'latest.zip') $packageFiles

$sourceFiles = @{}
foreach ($name in @('.gitignore','Directory.Build.props','LICENSE','README.md','RESEARCH.md','THIRD_PARTY_NOTICES.md',
    'pluginmaster.json','release-notes.zh-CN.md')) { $sourceFiles[$name] = Join-Path $taskRoot $name }
foreach ($folder in @('src','tests','tools','scripts','.github')) {
    Get-ChildItem -LiteralPath (Join-Path $taskRoot $folder) -Recurse -File -Force |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj|\.git|\.vs)[\\/]' } |
        ForEach-Object {
            $relative = [IO.Path]::GetRelativePath($taskRoot, $_.FullName).Replace('\','/')
            $sourceFiles[$relative] = $_.FullName
        }
}
Write-Archive (Join-Path $staging 'source.zip') $sourceFiles
$checksums = foreach ($name in @('latest.zip','source.zip')) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $staging $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name"
}
[IO.File]::WriteAllText((Join-Path $taskRoot 'SHA256SUMS.txt'), ($checksums -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
foreach ($name in @('pluginmaster.json','SHA256SUMS.txt','README.md','LICENSE','release-notes.zh-CN.md')) {
    Copy-Item -LiteralPath (Join-Path $taskRoot $name) -Destination $staging -Force
}
[pscustomobject]@{ Version = $version; Staging = $staging; SourceFiles = $sourceFiles.Count
    Subscription = "https://raw.githubusercontent.com/$Repository/main/pluginmaster.json" }
