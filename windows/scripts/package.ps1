[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',

    [ValidateSet('Release')]
    [string]$Configuration = 'Release',

    [string]$OutputDirectory,

    [switch]$ValidateManifest
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$windowsRoot = Join-Path $repoRoot 'windows'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $windowsRoot 'artifacts'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$projectPath = Join-Path $windowsRoot 'src\PokeTokenBar.App\PokeTokenBar.App.csproj'
$noticePath = Join-Path $windowsRoot 'THIRD-PARTY-NOTICES.md'
$licensePath = Join-Path $repoRoot 'LICENSE'

[xml]$project = Get-Content -LiteralPath $projectPath
$projectVersion = [string]$project.Project.PropertyGroup.Version
if ($projectVersion -ne $Version) {
    throw "Package version $Version does not match project version $projectVersion."
}

$publishDirectory = Join-Path $OutputDirectory "publish\$Runtime"
$zipPath = Join-Path $OutputDirectory "PokeTokenBar-$Runtime.zip"
$checksumPath = "$zipPath.sha256"

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
if (Test-Path -LiteralPath $publishDirectory) {
    $resolvedPublish = [IO.Path]::GetFullPath($publishDirectory)
    $resolvedOutput = $OutputDirectory.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedPublish.StartsWith($resolvedOutput, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove publish directory outside the selected output directory."
    }
    Remove-Item -LiteralPath $resolvedPublish -Recurse -Force
}

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $publishDirectory `
    -p:Version=$Version `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$executable = Join-Path $publishDirectory 'PokeTokenBar.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw 'Published application executable was not created.'
}

Copy-Item -LiteralPath $licensePath -Destination (Join-Path $publishDirectory 'LICENSE.txt')
Copy-Item -LiteralPath $noticePath -Destination (Join-Path $publishDirectory 'THIRD-PARTY-NOTICES.md')

foreach ($oldFile in @($zipPath, $checksumPath)) {
    if (Test-Path -LiteralPath $oldFile) {
        Remove-Item -LiteralPath $oldFile -Force
    }
}
Add-Type -AssemblyName System.IO.Compression
$zipStream = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew)
$archive = [IO.Compression.ZipArchive]::new(
    $zipStream,
    [IO.Compression.ZipArchiveMode]::Create,
    $false)
try {
    $fixedTimestamp = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
    $relativeOffset = $publishDirectory.TrimEnd([IO.Path]::DirectorySeparatorChar).Length + 1
    foreach ($file in Get-ChildItem -LiteralPath $publishDirectory -File -Recurse | Sort-Object FullName) {
        $relativePath = $file.FullName.Substring($relativeOffset).Replace('\', '/')
        $entry = $archive.CreateEntry($relativePath, [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = $fixedTimestamp
        $input = $file.OpenRead()
        $output = $entry.Open()
        try {
            $input.CopyTo($output)
        }
        finally {
            $output.Dispose()
            $input.Dispose()
        }
    }
}
finally {
    $archive.Dispose()
    $zipStream.Dispose()
}

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Value "$hash  $(Split-Path $zipPath -Leaf)" -Encoding ascii

if ($ValidateManifest) {
    $manifestPath = Join-Path $windowsRoot 'scoop\poke-token-bar.json'
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    $manifestHash = [string]$manifest.architecture.'64bit'.hash
    if ([string]$manifest.version -ne $Version -or $manifestHash -ne $hash) {
        throw "Scoop manifest version/hash does not match the generated package."
    }
}

[pscustomobject]@{
    Version = $Version
    Runtime = $Runtime
    Zip = $zipPath
    Sha256 = $hash
    SizeBytes = (Get-Item -LiteralPath $zipPath).Length
}
