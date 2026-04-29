[CmdletBinding()]
param(
    [string]$Tag = $env:NanoAgent_TAG,
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\NanoAgent\bin'),
    [string]$WaitForProcessId = $env:NanoAgent_WAIT_FOR_PROCESS_ID
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Owner = 'rizwan3d'
$Repo = 'NanoAgent'
$AppName = 'NanoAgent.CLI'
$ExecutableName = 'NanoAgent.CLI'
$CommandName = 'nanoai'
$AssetName = "$ExecutableName-win-x64.zip"
$ChecksumsName = 'SHA256SUMS'
$TotalSteps = 7
$CurrentStep = 0
$InstallActivity = "Installing $AppName"

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

function Save-UrlToFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url,

        [Parameter(Mandatory = $true)]
        [string]$Path,

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
                Headers = @{ 'User-Agent' = "$AppName-installer" }
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

function Test-Sha256Required {
    $value = $env:NANOAGENT_REQUIRE_SHA256

    return $value -in @('1', 'true', 'TRUE', 'True', 'yes', 'YES', 'Yes')
}

function Get-ExpectedSha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ChecksumsPath,

        [Parameter(Mandatory = $true)]
        [string]$FileName
    )

    foreach ($line in Get-Content -LiteralPath $ChecksumsPath) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $parts = $line.Trim() -split '\s+', 2
        if ($parts.Count -ne 2) {
            continue
        }

        $hash = $parts[0].ToLowerInvariant()
        $name = $parts[1].TrimStart([char]'*')
        if ($name.StartsWith('./', [StringComparison]::Ordinal)) {
            $name = $name.Substring(2)
        }

        if ($name -eq $FileName) {
            return $hash
        }
    }

    return $null
}

function Get-ReleaseAssetSha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tag,

        [Parameter(Mandatory = $true)]
        [string]$FileName
    )

    $metadataUrl = "https://api.github.com/repos/$Owner/$Repo/releases/tags/$Tag"

    try {
        $release = Invoke-RestMethod -Uri $metadataUrl -Headers @{ 'User-Agent' = "$AppName-installer" }
    }
    catch {
        return $null
    }

    foreach ($asset in @($release.assets)) {
        if ([string]$asset.name -ne $FileName) {
            continue
        }

        $digest = [string]$asset.digest
        if ($digest.StartsWith('sha256:', [System.StringComparison]::OrdinalIgnoreCase)) {
            return $digest.Substring(7).ToLowerInvariant()
        }
    }

    return $null
}

function Test-ArchiveSha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Tag,

        [Parameter(Mandatory = $true)]
        [string]$ArchivePath,

        [Parameter(Mandatory = $true)]
        [string]$TempRoot
    )

    $checksumsUrl = "https://github.com/$Owner/$Repo/releases/download/$Tag/$ChecksumsName"
    $checksumsPath = Join-Path $TempRoot $ChecksumsName
    $expectedSha256 = $null

    Write-Status "Downloading $ChecksumsName..."
    try {
        Save-UrlToFile -Url $checksumsUrl -Path $checksumsPath
    }
    catch {
        $expectedSha256 = Get-ReleaseAssetSha256 -Tag $Tag -FileName $AssetName

        if ([string]::IsNullOrWhiteSpace($expectedSha256)) {
            if (Test-Sha256Required) {
                Fail-Install "Unable to download $ChecksumsName from $checksumsUrl, and no GitHub release metadata digest was found. $($_.Exception.Message)"
            }

            Write-Status "$ChecksumsName was not found for $Tag; continuing without checksum verification. Set NANOAGENT_REQUIRE_SHA256=1 to require it."
            return
        }

        Write-Status "Using SHA256 digest from GitHub release metadata for $AssetName."
    }

    if ([string]::IsNullOrWhiteSpace($expectedSha256)) {
        $expectedSha256 = Get-ExpectedSha256 -ChecksumsPath $checksumsPath -FileName $AssetName
    }

    if ([string]::IsNullOrWhiteSpace($expectedSha256)) {
        Fail-Install "$ChecksumsName does not contain a checksum for $AssetName."
    }

    if ($expectedSha256 -notmatch '^[0-9a-f]{64}$') {
        Fail-Install "$ChecksumsName contains an invalid SHA256 checksum for $AssetName."
    }

    $actualSha256 = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualSha256 -ne $expectedSha256) {
        Fail-Install "SHA256 verification failed for $AssetName. Expected $expectedSha256, got $actualSha256."
    }

    Write-Status "Verified SHA256 checksum for $AssetName."
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

function ConvertTo-PowerShellLiteral {
    param([string]$Value)

    return "'" + $Value.Replace("'", "''") + "'"
}

function Get-ValidProcessId {
    param([string]$Value)

    $parsedProcessId = 0
    if (
        -not [string]::IsNullOrWhiteSpace($Value) -and
        [int]::TryParse($Value, [ref]$parsedProcessId) -and
        $parsedProcessId -gt 0
    ) {
        return $parsedProcessId
    }

    return 0
}

function Test-SamePath {
    param(
        [string]$Left,
        [string]$Right
    )

    if ([string]::IsNullOrWhiteSpace($Left) -or [string]::IsNullOrWhiteSpace($Right)) {
        return $false
    }

    try {
        $normalizedLeft = [System.IO.Path]::GetFullPath($Left).TrimEnd('\')
        $normalizedRight = [System.IO.Path]::GetFullPath($Right).TrimEnd('\')
    }
    catch {
        return $false
    }

    return $normalizedLeft -ieq $normalizedRight
}

function Get-ParentProcessId {
    param([int]$ProcessId)

    try {
        $processInfo = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId = $ProcessId" -ErrorAction Stop
        if ($null -ne $processInfo -and $processInfo.ParentProcessId -gt 0) {
            return [int]$processInfo.ParentProcessId
        }
    }
    catch {
    }

    return 0
}

function Resolve-WaitForProcessId {
    param(
        [string]$RequestedProcessId,
        [string]$DestinationPath
    )

    $requested = Get-ValidProcessId -Value $RequestedProcessId
    if ($requested -gt 0) {
        return $requested
    }

    $ancestorProcessId = $PID
    for ($index = 0; $index -lt 5; $index++) {
        $parentProcessId = Get-ParentProcessId -ProcessId $ancestorProcessId
        if ($parentProcessId -le 0) {
            break
        }

        try {
            $parentProcess = Get-Process -Id $parentProcessId -ErrorAction Stop
            $parentPath = $null
            try {
                $parentPath = $parentProcess.Path
            }
            catch {
            }

            if (
                $parentProcess.ProcessName -ieq $CommandName -or
                $parentProcess.ProcessName -ieq $ExecutableName -or
                (Test-SamePath -Left $parentPath -Right $DestinationPath)
            ) {
                return $parentProcessId
            }
        }
        catch {
        }

        $ancestorProcessId = $parentProcessId
    }

    return 0
}

function Start-DeferredInstall {
    param(
        [string]$SourcePath,
        [string]$DestinationPath,
        [int]$ProcessId,
        [string]$CleanupRoot
    )

    $scriptPath = Join-Path $CleanupRoot 'complete-update.ps1'
    $logPath = Join-Path $CleanupRoot 'complete-update.log'
    $deferredScript = @'
param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,

    [Parameter(Mandatory = $true)]
    [string]$DestinationPath,

    [int]$WaitForProcessId,

    [Parameter(Mandatory = $true)]
    [string]$CleanupRoot,

    [Parameter(Mandatory = $true)]
    [string]$LogPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Log {
    param([string]$Message)

    $timestamp = [DateTimeOffset]::Now.ToString('o')
    Add-Content -LiteralPath $LogPath -Value "[$timestamp] $Message"
}

function Copy-WithRetry {
    param(
        [string]$SourcePath,
        [string]$DestinationPath
    )

    $deadline = [DateTimeOffset]::UtcNow.AddMinutes(10)
    while ($true) {
        try {
            New-Item -ItemType Directory -Path (Split-Path -Parent $DestinationPath) -Force | Out-Null
            Copy-Item -LiteralPath $SourcePath -Destination $DestinationPath -Force
            return
        }
        catch {
            if ([DateTimeOffset]::UtcNow -ge $deadline) {
                throw
            }

            Start-Sleep -Seconds 1
        }
    }
}

$completed = $false

try {
    Write-Log "Waiting for process $WaitForProcessId to exit before replacing $DestinationPath."

    if ($WaitForProcessId -gt 0) {
        try {
            Wait-Process -Id $WaitForProcessId -Timeout 86400 -ErrorAction SilentlyContinue
        }
        catch {
            Write-Log "Wait-Process warning: $($_.Exception.Message)"
        }
    }

    Copy-WithRetry -SourcePath $SourcePath -DestinationPath $DestinationPath
    Write-Log "Installed update to $DestinationPath."
    $completed = $true
}
catch {
    Write-Log "Update failed: $($_.Exception.Message)"
    exit 1
}
finally {
    if ($completed -and (Test-Path -LiteralPath $CleanupRoot)) {
        Remove-Item -LiteralPath $CleanupRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
'@

    Set-Content -LiteralPath $scriptPath -Value $deferredScript -Encoding UTF8

    $command = "& " +
        (ConvertTo-PowerShellLiteral -Value $scriptPath) +
        " -SourcePath " +
        (ConvertTo-PowerShellLiteral -Value $SourcePath) +
        " -DestinationPath " +
        (ConvertTo-PowerShellLiteral -Value $DestinationPath) +
        " -WaitForProcessId $ProcessId -CleanupRoot " +
        (ConvertTo-PowerShellLiteral -Value $CleanupRoot) +
        " -LogPath " +
        (ConvertTo-PowerShellLiteral -Value $logPath)
    $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))

    Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden -ArgumentList @(
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-EncodedCommand',
        $encodedCommand
    ) | Out-Null

    Write-Status "Update staged. Exit NanoAgent to finish replacing '$CommandName.exe'."
    Write-Status "Deferred update log: $logPath"
}

Write-Status 'NanoAgent CLI Installer'
Start-InstallStep 'Checking system requirements...'
$architecture = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
if ($architecture -notin @('AMD64', 'x86_64')) {
    Fail-Install "Unsupported Windows architecture '$architecture'. This installer supports Windows x64 only."
}

Complete-InstallStep "Detected Windows $architecture."

Start-InstallStep 'Resolving release...'
if ([string]::IsNullOrWhiteSpace($Tag)) {
    $Tag = Get-LatestTag
}

Complete-InstallStep "Using $AppName $Tag for win-x64."

$downloadUrl = "https://github.com/$Owner/$Repo/releases/download/$Tag/$AssetName"
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "$AppName-install-$([System.Guid]::NewGuid().ToString('N'))"
$archivePath = Join-Path $tempRoot $AssetName
$extractDir = Join-Path $tempRoot 'extract'
$destinationPath = Join-Path $InstallDir "$CommandName.exe"
$cleanupTempRoot = $true

try {
    Write-Status "Installing as '$CommandName'..."

    Start-InstallStep 'Preparing install directory...'
    Write-Status "Install directory: $InstallDir"

    New-Item -ItemType Directory -Path $tempRoot, $extractDir, $InstallDir -Force | Out-Null
    Complete-InstallStep 'Workspace ready.'

    Start-InstallStep "Downloading $AssetName..."
    try {
        Save-UrlToFile -Url $downloadUrl -Path $archivePath -Activity "Downloading $AssetName" -ShowProgress
    }
    catch {
        Fail-Install "Download failed from $downloadUrl. $($_.Exception.Message)"
    }

    $downloadedSize = (Get-Item -LiteralPath $archivePath).Length
    Complete-InstallStep "Downloaded $AssetName ($(Format-ByteSize -Bytes $downloadedSize))."

    Start-InstallStep 'Verifying download...'
    Test-ArchiveSha256 -Tag $Tag -ArchivePath $archivePath -TempRoot $tempRoot
    Complete-InstallStep 'Checksum verification passed.'

    Start-InstallStep 'Extracting archive...'
    Expand-Archive -Path $archivePath -DestinationPath $extractDir -Force

    $sourcePath = Join-Path $extractDir "$ExecutableName.exe"
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        Fail-Install "Expected executable '$ExecutableName.exe' was not found in $AssetName."
    }

    Complete-InstallStep "Found $ExecutableName.exe."

    Start-InstallStep 'Installing command...'
    $waitProcessId = Resolve-WaitForProcessId -RequestedProcessId $WaitForProcessId -DestinationPath $destinationPath
    if ($waitProcessId -gt 0) {
        Start-DeferredInstall -SourcePath $sourcePath -DestinationPath $destinationPath -ProcessId $waitProcessId -CleanupRoot $tempRoot
        $cleanupTempRoot = $false
    }
    else {
        try {
            Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
        }
        catch {
            Fail-Install "Unable to replace '$destinationPath'. Close any running NanoAgent sessions and try again. $($_.Exception.Message)"
        }

        Write-Status "Installed '$CommandName.exe' to $destinationPath"
    }

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

    Complete-InstallStep 'Installation finished.'
    Complete-InstallerProgress
    Write-Status "Done. Run '$CommandName' to start NanoAgent."
}
finally {
    if ($cleanupTempRoot -and (Test-Path -LiteralPath $tempRoot)) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
