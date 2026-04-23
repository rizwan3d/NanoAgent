[CmdletBinding()]
param(
    [string]$Tag = $env:NanoAgent_TAG,
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\NanoAgent\bin')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Owner = 'rizwan3d'
$Repo = 'NanoAgent'
$AppName = 'NanoAgent'
$CommandName = 'nano'
$AssetName = "$AppName-win-x64.zip"

function Write-Status {
    param([string]$Message)

    Write-Host "[$AppName] $Message"
}

function Fail-Install {
    param([string]$Message)

    throw "[$AppName] $Message"
}

function Get-LatestTag {
    $apiUrl = "https://api.github.com/repos/$Owner/$Repo/releases/latest"

    try {
        $response = Invoke-RestMethod -Uri $apiUrl -Headers @{ 'User-Agent' = "$AppName-installer" }
    }
    catch {
        Fail-Install "Unable to resolve the latest release tag from $apiUrl. Set NanoAgent_TAG and try again. $($_.Exception.Message)"
    }

    if ([string]::IsNullOrWhiteSpace($response.tag_name)) {
        Fail-Install 'GitHub did not return a release tag.'
    }

    return [string]$response.tag_name
}

function Test-PathContainsDirectory {
    param(
        [string]$PathValue,
        [string]$Directory
    )

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return $false
    }

    $normalizedTarget = [System.IO.Path]::GetFullPath($Directory).TrimEnd('\')

    foreach ($entry in ($PathValue -split ';')) {
        if ([string]::IsNullOrWhiteSpace($entry)) {
            continue
        }

        try {
            $normalizedEntry = [System.IO.Path]::GetFullPath($entry).TrimEnd('\')
        }
        catch {
            continue
        }

        if ($normalizedEntry -ieq $normalizedTarget) {
            return $true
        }
    }

    return $false
}

$architecture = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
if ($architecture -notin @('AMD64', 'x86_64')) {
    Fail-Install "Unsupported Windows architecture '$architecture'. This installer supports Windows x64 only."
}

if ([string]::IsNullOrWhiteSpace($Tag)) {
    $Tag = Get-LatestTag
}

$downloadUrl = "https://github.com/$Owner/$Repo/releases/download/$Tag/$AssetName"
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "$AppName-install-$([System.Guid]::NewGuid().ToString('N'))"
$archivePath = Join-Path $tempRoot $AssetName
$extractDir = Join-Path $tempRoot 'extract'
$destinationPath = Join-Path $InstallDir "$CommandName.exe"

try {
    Write-Status "Installing $AppName $Tag for win-x64 as '$CommandName'..."
    Write-Status "Install directory: $InstallDir"

    New-Item -ItemType Directory -Path $tempRoot, $extractDir, $InstallDir -Force | Out-Null

    Write-Status "Downloading $AssetName..."
    try {
        Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath -Headers @{ 'User-Agent' = "$AppName-installer" }
    }
    catch {
        Fail-Install "Download failed from $downloadUrl. $($_.Exception.Message)"
    }

    Write-Status 'Extracting archive...'
    Expand-Archive -Path $archivePath -DestinationPath $extractDir -Force

    $sourcePath = Join-Path $extractDir "$AppName.exe"
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        Fail-Install "Expected executable '$AppName.exe' was not found in $AssetName."
    }

    Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
    Write-Status "Installed '$CommandName.exe' to $destinationPath"

    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $pathUpdated = $false

    if (-not (Test-PathContainsDirectory -PathValue $userPath -Directory $InstallDir)) {
        $newUserPath = if ([string]::IsNullOrWhiteSpace($userPath)) {
            $InstallDir
        }
        else {
            "$userPath;$InstallDir"
        }

        [Environment]::SetEnvironmentVariable('Path', $newUserPath, 'User')
        $pathUpdated = $true
    }

    if ($pathUpdated) {
        Write-Status "Added '$InstallDir' to your user PATH. Restart your shell to use the new PATH entry."
    }
    else {
        Write-Status 'The install directory is already on your user PATH.'
    }

    Write-Status 'Done.'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
