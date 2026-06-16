[CmdletBinding()]
param(
    [string]$RunId = $env:NANOAGENT_RUN_ID,
    [string]$Branch = $(if ($env:NANOAGENT_BRANCH) { $env:NANOAGENT_BRANCH } else { 'master' }),
    [string]$InstallDir = $(if ($env:NANOAGENT_INSTALL_DIR) { $env:NANOAGENT_INSTALL_DIR } elseif ($env:NanoAgent_INSTALL_DIR) { $env:NanoAgent_INSTALL_DIR } else { Join-Path $env:LOCALAPPDATA 'Programs\NanoAgent\bin' })
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Owner = 'rizwan3d'
$Repo = 'NanoAgent'
$WorkflowFile = 'ci.yml'
$AppName = 'NanoAgent.CLI'
$ExecutableName = 'NanoAgent.CLI.exe'
$CommandName = 'nanoai'
$ArtifactName = 'cli-win-x64-test-build'
$TotalSteps = 8
$CurrentStep = 0
$InstallActivity = "Installing latest $AppName test build"
$GitHubApiVersion = '2026-03-10'

function Write-Status {
    param([string]$Message)

    Write-Host "[$AppName] $Message"
}

function Fail-Install {
    param([string]$Message)

    throw "[$AppName] $Message"
}

function Test-ProgressEnabled {
    $value = if ($env:NANOAGENT_NO_PROGRESS) { $env:NANOAGENT_NO_PROGRESS } else { $env:NanoAgent_NO_PROGRESS }
    if ($value -in @('1', 'true', 'TRUE', 'True', 'yes', 'YES', 'Yes')) {
        return $false
    }

    try {
        return -not [Console]::IsOutputRedirected
    }
    catch {
        return $true
    }
}

function Format-ByteSize {
    param([long]$Bytes)

    if ($Bytes -ge 1GB) {
        return '{0:N1} GiB' -f ($Bytes / 1GB)
    }

    if ($Bytes -ge 1MB) {
        return '{0:N1} MiB' -f ($Bytes / 1MB)
    }

    if ($Bytes -ge 1KB) {
        return '{0:N1} KiB' -f ($Bytes / 1KB)
    }

    return "$Bytes B"
}

function Write-InstallerProgress {
    param(
        [string]$Activity = $script:InstallActivity,
        [string]$Status = 'Working...',
        [int]$PercentComplete = 0,
        [switch]$Completed
    )

    if (-not (Test-ProgressEnabled)) {
        return
    }

    $previousProgressPreference = $script:ProgressPreference

    try {
        $script:ProgressPreference = 'Continue'

        if ($Completed) {
            Write-Progress -Activity $Activity -Completed
        }
        else {
            Write-Progress -Activity $Activity -Status $Status -PercentComplete $PercentComplete
        }
    }
    finally {
        $script:ProgressPreference = $previousProgressPreference
    }
}

function Start-InstallStep {
    param([string]$Message)

    $script:CurrentStep++
    $percent = [Math]::Min(99, [Math]::Floor((($script:CurrentStep - 1) / $script:TotalSteps) * 100))

    Write-Status "[$script:CurrentStep/$script:TotalSteps] $Message"
    Write-InstallerProgress -Status $Message -PercentComplete $percent
}

function Complete-InstallStep {
    param([string]$Message)

    $percent = [Math]::Min(100, [Math]::Floor(($script:CurrentStep / $script:TotalSteps) * 100))

    if (-not [string]::IsNullOrWhiteSpace($Message)) {
        Write-Status "    $Message"
    }

    $status = if ([string]::IsNullOrWhiteSpace($Message)) { 'Working...' } else { $Message }
    Write-InstallerProgress -Status $status -PercentComplete $percent
}

function Complete-InstallerProgress {
    Write-InstallerProgress -Completed
}

function Get-GitHubToken {
    foreach ($name in @('NANOAGENT_GITHUB_TOKEN', 'NanoAgent_GITHUB_TOKEN', 'GITHUB_TOKEN', 'GH_TOKEN')) {
        $value = [Environment]::GetEnvironmentVariable($name, 'Process')
        if ([string]::IsNullOrWhiteSpace($value)) {
            $value = [Environment]::GetEnvironmentVariable($name, 'User')
        }

        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value.Trim()
        }
    }

    $ghCommand = Get-Command gh -ErrorAction SilentlyContinue
    if ($null -ne $ghCommand) {
        try {
            $token = (& $ghCommand.Source auth token 2>$null | Out-String).Trim()
            if (-not [string]::IsNullOrWhiteSpace($token)) {
                return $token
            }
        }
        catch {
        }
    }

    Fail-Install "A GitHub token with Actions read access is required to download test-build artifacts. Set NANOAGENT_GITHUB_TOKEN, GITHUB_TOKEN, or GH_TOKEN, or sign in with GitHub CLI."
}

function Get-ApiHeaders {
    param([string]$Token)

    return @{
        'Accept' = 'application/vnd.github+json'
        'Authorization' = "Bearer $Token"
        'User-Agent' = "$AppName-test-installer"
        'X-GitHub-Api-Version' = $GitHubApiVersion
    }
}

function Invoke-GitHubJson {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url,

        [Parameter(Mandatory = $true)]
        [string]$Token
    )

    try {
        return Invoke-RestMethod -Uri $Url -Headers (Get-ApiHeaders -Token $Token) -ErrorAction Stop
    }
    catch {
        Fail-Install "GitHub API request failed for $Url. $($_.Exception.Message)"
    }
}

function Save-UrlToFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url,

        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Token,

        [string]$Activity = 'Downloading',

        [switch]$ShowProgress
    )

    $lastError = $null

    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $previousProgressPreference = $script:ProgressPreference

        try {
            $script:ProgressPreference = if ($ShowProgress -and (Test-ProgressEnabled)) { 'Continue' } else { 'SilentlyContinue' }
            $request = @{
                Uri = $Url
                OutFile = $Path
                Headers = (Get-ApiHeaders -Token $Token)
                ErrorAction = 'Stop'
            }

            if ($PSVersionTable.PSVersion.Major -lt 6) {
                $request.UseBasicParsing = $true
            }

            Invoke-WebRequest @request
            return
        }
        catch {
            $lastError = $_
            Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue

            if ($attempt -lt 3) {
                Write-Status "Download attempt $attempt failed; retrying..."
                Start-Sleep -Seconds (2 * $attempt)
            }
        }
        finally {
            $script:ProgressPreference = $previousProgressPreference
            if ($ShowProgress -and (Test-ProgressEnabled)) {
                Write-InstallerProgress -Activity $Activity -Completed
            }
        }
    }

    throw $lastError
}

function Get-LatestSuccessfulRun {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Token,

        [Parameter(Mandatory = $true)]
        [string]$TargetBranch
    )

    $encodedBranch = [Uri]::EscapeDataString($TargetBranch)
    $apiUrl = "https://api.github.com/repos/$Owner/$Repo/actions/workflows/$WorkflowFile/runs?branch=$encodedBranch&event=push&status=success&exclude_pull_requests=true&per_page=20"
    $response = Invoke-GitHubJson -Url $apiUrl -Token $Token
    $run = @($response.workflow_runs) | Select-Object -First 1

    if ($null -eq $run) {
        Fail-Install "No successful '$WorkflowFile' push runs were found for branch '$TargetBranch'."
    }

    return $run
}

function Get-ArtifactMetadata {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Token,

        [Parameter(Mandatory = $true)]
        [string]$TargetRunId
    )

    $apiUrl = "https://api.github.com/repos/$Owner/$Repo/actions/runs/$TargetRunId/artifacts?per_page=100"
    $response = Invoke-GitHubJson -Url $apiUrl -Token $Token
    $artifact = @($response.artifacts) | Where-Object { $_.name -eq $ArtifactName } | Select-Object -First 1

    if ($null -eq $artifact) {
        Fail-Install "Artifact '$ArtifactName' was not found for workflow run $TargetRunId."
    }

    if ($artifact.expired) {
        Fail-Install "Artifact '$ArtifactName' for workflow run $TargetRunId has expired."
    }

    return $artifact
}

function Test-ArchiveSha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ArchivePath,

        [Parameter(Mandatory = $true)]
        [string]$Digest
    )

    if (-not $Digest.StartsWith('sha256:', [System.StringComparison]::OrdinalIgnoreCase)) {
        Fail-Install "GitHub did not return a valid SHA256 digest for '$ArtifactName'."
    }

    $expectedSha256 = $Digest.Substring(7).ToLowerInvariant()
    if ($expectedSha256 -notmatch '^[0-9a-f]{64}$') {
        Fail-Install "GitHub returned an invalid SHA256 digest for '$ArtifactName'."
    }

    $actualSha256 = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualSha256 -ne $expectedSha256) {
        Fail-Install "SHA256 verification failed for '$ArtifactName'. Expected $expectedSha256, got $actualSha256."
    }

    Write-Status "Verified SHA256 checksum for '$ArtifactName'."
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

Write-Status 'NanoAgent CLI Test-Build Installer'
Start-InstallStep 'Checking system requirements...'
$architecture = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
if ($architecture -notin @('AMD64', 'x86_64')) {
    Fail-Install "Unsupported Windows architecture '$architecture'. This installer supports Windows x64 only."
}

Complete-InstallStep "Detected Windows $architecture."

Start-InstallStep 'Resolving GitHub authentication...'
$gitHubToken = Get-GitHubToken
Complete-InstallStep 'GitHub token is available.'

Start-InstallStep 'Resolving workflow run...'
$workflowRun = if ([string]::IsNullOrWhiteSpace($RunId)) {
    Get-LatestSuccessfulRun -Token $gitHubToken -TargetBranch $Branch
}
else {
    [pscustomobject]@{ id = $RunId; head_branch = $Branch; head_sha = $null }
}

$resolvedRunId = [string]$workflowRun.id
Complete-InstallStep "Using workflow run $resolvedRunId."

Start-InstallStep 'Locating test-build artifact...'
$artifact = Get-ArtifactMetadata -Token $gitHubToken -TargetRunId $resolvedRunId
Complete-InstallStep "Found '$ArtifactName'."

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "$AppName-test-install-$([System.Guid]::NewGuid().ToString('N'))"
$archivePath = Join-Path $tempRoot "$ArtifactName.zip"
$extractDir = Join-Path $tempRoot 'extract'
$destinationPath = Join-Path $InstallDir "$CommandName.exe"

try {
    Start-InstallStep 'Preparing install directory...'
    Write-Status "Install directory: $InstallDir"
    New-Item -ItemType Directory -Path $tempRoot, $extractDir, $InstallDir -Force | Out-Null
    Complete-InstallStep 'Workspace ready.'

    Start-InstallStep "Downloading $ArtifactName..."
    try {
        Save-UrlToFile -Url ([string]$artifact.archive_download_url) -Path $archivePath -Token $gitHubToken -Activity "Downloading $ArtifactName" -ShowProgress
    }
    catch {
        Fail-Install "Download failed for '$ArtifactName'. $($_.Exception.Message)"
    }

    $downloadedSize = (Get-Item -LiteralPath $archivePath).Length
    Complete-InstallStep "Downloaded $ArtifactName ($(Format-ByteSize -Bytes $downloadedSize))."

    Start-InstallStep 'Verifying download...'
    Test-ArchiveSha256 -ArchivePath $archivePath -Digest ([string]$artifact.digest)
    Complete-InstallStep 'Checksum verification passed.'

    Start-InstallStep 'Extracting and installing command...'
    Expand-Archive -Path $archivePath -DestinationPath $extractDir -Force

    $sourcePath = Get-ChildItem -Path $extractDir -Filter $ExecutableName -Recurse -File | Select-Object -ExpandProperty FullName -First 1
    if ([string]::IsNullOrWhiteSpace($sourcePath)) {
        Fail-Install "Expected executable '$ExecutableName' was not found in '$ArtifactName'."
    }

    try {
        Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
    }
    catch {
        Fail-Install "Unable to replace '$destinationPath'. Close any running NanoAgent sessions and try again. $($_.Exception.Message)"
    }

    Write-Status "Installed '$CommandName.exe' to $destinationPath"

    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    if (-not (Test-PathContainsDirectory -PathValue $userPath -Directory $InstallDir)) {
        $newUserPath = if ([string]::IsNullOrWhiteSpace($userPath)) {
            $InstallDir
        }
        else {
            "$userPath;$InstallDir"
        }

        [Environment]::SetEnvironmentVariable('Path', $newUserPath, 'User')
        Write-Status "Added '$InstallDir' to your user PATH. Restart your shell to use the new PATH entry."
    }
    else {
        Write-Status 'The install directory is already on your user PATH.'
    }

    Complete-InstallStep 'Installation finished.'
    Complete-InstallerProgress
    Write-Status "Done. Run '$CommandName' to start the latest test build."
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
