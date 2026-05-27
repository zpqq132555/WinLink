[CmdletBinding(PositionalBinding = $false)]
param (
    [string]$Version = "",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipTests,
    [switch]$CreateTag,
    [switch]$PushTag,
    [switch]$CreateRelease,
    [string]$ReleaseNotes = ""
)

<#
.SYNOPSIS
Build a portable WinLink ZIP and optionally create a Git tag and GitHub Release.

.DESCRIPTION
The default flow only runs tests, dotnet publish, ZIP packaging, and SHA256 hashing.
Git and GitHub actions run only when -CreateTag, -PushTag, or -CreateRelease is provided.

.EXAMPLE
.\release.ps1 -Version 1.0.0

.EXAMPLE
.\release.ps1 -Version 1.0.0 -CreateTag -PushTag -CreateRelease
#>

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path $PSScriptRoot
$projectPath = Join-Path $repoRoot "src/WinLink.App/WinLink.App.csproj"
$artifactsRoot = Join-Path $repoRoot "artifacts"
$testArtifactsRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("WinLink-release-test-" + [Guid]::NewGuid().ToString("N"))
$publishArtifactsRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("WinLink-release-publish-" + [Guid]::NewGuid().ToString("N"))

function Write-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    Write-Output ""
    Write-Output "==> $Message"
}

function Resolve-Version {
    param(
        [string]$RequestedVersion,
        [switch]$IsCreatingTag
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedVersion)) {
        return $RequestedVersion.Trim().TrimStart("v")
    }

    if ($IsCreatingTag) {
        throw "When using -CreateTag, you must also provide -Version."
    }

    $detectedTag = git describe --tags --abbrev=0 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($detectedTag)) {
        throw "No Git tag was found. Provide -Version or create a version tag first."
    }

    return $detectedTag.Trim().TrimStart("v")
}

function Assert-CommandExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CommandName
    )

    if (-not (Get-Command $CommandName -ErrorAction SilentlyContinue)) {
        throw "Command '$CommandName' was not found. Install it and make sure it is on PATH."
    }
}

function Assert-CleanWorkingTree {
    $statusLines = git status --porcelain
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to read Git working tree status."
    }

    if ($statusLines) {
        throw "The working tree has uncommitted changes. Commit or clean them before creating a release tag."
    }
}

function Ensure-TagExistsLocally {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TagName
    )

    git rev-parse --verify $TagName 1>$null 2>$null
    return $LASTEXITCODE -eq 0
}

function Ensure-TagExistsOnRemote {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TagName
    )

    $remoteMatch = git ls-remote --tags origin $TagName
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to check remote tag state. Make sure origin is reachable."
    }

    return -not [string]::IsNullOrWhiteSpace($remoteMatch)
}

Assert-CommandExists -CommandName "git"
Assert-CommandExists -CommandName "dotnet"

$Version = Resolve-Version -RequestedVersion $Version -IsCreatingTag:$CreateTag
$tagName = "v$Version"
$publishDir = Join-Path $artifactsRoot "publish/$Version/$Runtime"
$zipName = "WinLink-$Version-portable-$Runtime.zip"
$zipPath = Join-Path $artifactsRoot $zipName

Write-Output "Repository : $repoRoot"
Write-Output "Project    : $projectPath"
Write-Output "Version    : $Version"
Write-Output "Config     : $Configuration"
Write-Output "Runtime    : $Runtime"
Write-Output "ZIP        : $zipPath"

if (-not (Test-Path $projectPath)) {
    throw "Project file was not found: $projectPath"
}

if ($CreateTag) {
    Write-Step "Checking tag prerequisites"
    Assert-CleanWorkingTree

    if (Ensure-TagExistsLocally -TagName $tagName) {
        throw "Local tag $tagName already exists. Use another version or remove the existing tag first."
    }
}

if ($PushTag -and -not (Ensure-TagExistsLocally -TagName $tagName) -and -not $CreateTag) {
    throw "Local tag $tagName does not exist. Use -CreateTag first or create it manually before -PushTag."
}

if ($CreateRelease) {
    Assert-CommandExists -CommandName "gh"
}

if (-not (Test-Path $artifactsRoot)) {
    New-Item -ItemType Directory -Path $artifactsRoot | Out-Null
}

Write-Step "Cleaning previous publish output"
if (Test-Path $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

Write-Step "Running tests"
if (-not $SkipTests) {
    dotnet test (Join-Path $repoRoot "tests/WinLink.App.Tests/WinLink.App.Tests.csproj") `
        --configuration $Configuration `
        --artifacts-path $testArtifactsRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed. Release process stopped."
    }
} else {
    Write-Output "Tests were skipped."
}

Write-Step "Publishing application"
dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained false `
    --artifacts-path $publishArtifactsRoot `
    --output $publishDir `
    -p:Version=$Version

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

$publishedExe = Join-Path $publishDir "WinLink.App.exe"
if (-not (Test-Path $publishedExe)) {
    throw "WinLink.App.exe was not found in publish output: $publishedExe"
}

Write-Step "Creating ZIP package"
if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $publishDir,
    $zipPath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false)

if (-not (Test-Path $zipPath)) {
    throw "ZIP creation failed: $zipPath"
}

$zipItem = Get-Item -LiteralPath $zipPath
$zipHash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()

Write-Output ""
Write-Output "ZIP created: $($zipItem.FullName)"
Write-Output ("ZIP size: {0:N2} MB" -f ($zipItem.Length / 1MB))
Write-Output "SHA256: $zipHash"

if ($CreateTag) {
    Write-Step "Creating local Git tag"
    git tag $tagName
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create tag $tagName."
    }
}

if ($PushTag) {
    Write-Step "Pushing Git tag to origin"
    git push origin $tagName
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to push tag $tagName."
    }
}

if ($CreateRelease) {
    Write-Step "Checking remote tag and creating GitHub Release"
    if (-not (Ensure-TagExistsOnRemote -TagName $tagName)) {
        throw "Remote origin does not contain tag $tagName. Push the tag first or provide -PushTag."
    }

    $releaseTitle = "WinLink $tagName"
    $releaseNotesText = $ReleaseNotes
    if ([string]::IsNullOrWhiteSpace($releaseNotesText)) {
        $releaseNotesText = "Windows portable build for WinLink $tagName"
    }

    gh release create $tagName $zipPath --title $releaseTitle --notes $releaseNotesText
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create GitHub Release."
    }
}

Write-Output ""
Write-Output "Release flow completed."
Write-Output "Version: $Version"
Write-Output "Artifact: $zipPath"
