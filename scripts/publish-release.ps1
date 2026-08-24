param(
    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'FavGCalSchedulerClone.App\FavGCalSchedulerClone.App.csproj'
$publishDirectory = Join-Path $repositoryRoot 'publish'
$intermediateDirectory = Join-Path $env:TEMP 'FavGCalSchedulerClone-publish-intermediate'
$intermediateOutputDirectory = Join-Path $intermediateDirectory 'bin'
$intermediateObjectDirectory = Join-Path $intermediateDirectory 'obj'
# Keep staged release output on the same volume as the final publish directory so
# Directory.Move remains a same-volume rename even when the repository is not on C:.
$stagingPublishDirectory = Join-Path $repositoryRoot ("publish.staging." + [Guid]::NewGuid().ToString('N'))
$selfContainedValue = $SelfContained.ToString().ToLowerInvariant()

try
{
    if (Test-Path -LiteralPath $intermediateDirectory)
    {
        [System.IO.Directory]::Delete($intermediateDirectory, $true)
    }

    dotnet restore $projectPath -r win-x64 "-p:BaseIntermediateOutputPath=$intermediateObjectDirectory\"
    if ($LASTEXITCODE -ne 0)
    {
        throw "Restore failed with exit code $LASTEXITCODE."
    }

    dotnet publish $projectPath `
        -c Release `
        -r win-x64 `
        --self-contained:$selfContainedValue `
        --no-restore `
        -o $stagingPublishDirectory `
        "-p:BaseOutputPath=$intermediateOutputDirectory\" `
        "-p:BaseIntermediateOutputPath=$intermediateObjectDirectory\"

    if ($LASTEXITCODE -ne 0)
    {
        throw "Publish failed with exit code $LASTEXITCODE."
    }

    if (Test-Path -LiteralPath $publishDirectory)
    {
        [System.IO.Directory]::Delete($publishDirectory, $true)
    }
    [System.IO.Directory]::Move($stagingPublishDirectory, $publishDirectory)
}
finally
{
    if (Test-Path -LiteralPath $stagingPublishDirectory)
    {
        [System.IO.Directory]::Delete($stagingPublishDirectory, $true)
    }
    if (Test-Path -LiteralPath $intermediateDirectory)
    {
        [System.IO.Directory]::Delete($intermediateDirectory, $true)
    }
}

Write-Host "Release artifact: $publishDirectory\FavGCalSchedulerClone.App.exe"
