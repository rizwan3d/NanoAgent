[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$NanoAiArgs
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent (Split-Path -Parent $scriptDir)
$imageTag = if ($env:NANOAGENT_DOCKER_IMAGE) { $env:NANOAGENT_DOCKER_IMAGE } else { 'nanoagent-cli-debug:test' }
$forceRebuild = $env:NANOAGENT_DOCKER_FORCE_REBUILD -in @('1', 'true', 'TRUE', 'yes', 'YES')
$envCandidates = @(
    (Join-Path $scriptDir 'nanoai-ubuntu-debug.env'),
    (Join-Path $scriptDir '.env')
)

foreach ($envFile in $envCandidates) {
    if (-not (Test-Path $envFile)) {
        continue
    }

    foreach ($line in Get-Content $envFile) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith('#')) {
            continue
        }

        if ($trimmed -match '^(?:export\s+)?([A-Za-z_][A-Za-z0-9_]*)=(.*)$') {
            $name = $matches[1]
            $value = $matches[2].Trim()

            if (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'"))) {
                $value = $value.Substring(1, $value.Length - 2)
            }

            [Environment]::SetEnvironmentVariable($name, $value, 'Process')
        }
    }
}

if (-not $env:NANOAGENT_API_KEY) {
    throw 'Set NANOAGENT_API_KEY before running this script.'
}

if ($forceRebuild) {
    & docker build -f (Join-Path $repoRoot 'Dockerfile.ubuntu-cli-debug') -t $imageTag $repoRoot
    if ($LASTEXITCODE -ne 0) {
        throw 'Docker build failed.'
    }
}
else {
    $imageExists = $false
    $existingImageId = ((& docker image ls -q $imageTag) | Out-String).Trim()
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($existingImageId)) {
        $imageExists = $true
    }

    if (-not $imageExists) {
        & docker build -f (Join-Path $repoRoot 'Dockerfile.ubuntu-cli-debug') -t $imageTag $repoRoot
        if ($LASTEXITCODE -ne 0) {
            throw 'Docker build failed.'
        }
    }
}

if (-not $NanoAiArgs -or $NanoAiArgs.Count -eq 0) {
    $NanoAiArgs = @('--interactive')
}

$dockerArgs = @('run', '--rm')

if (-not [Console]::IsInputRedirected -and -not [Console]::IsOutputRedirected) {
    $dockerArgs += @('-it')
}

$dockerArgs += @(
    '-e', ('NANOAGENT_PROVIDER=' + $(if ($env:NANOAGENT_PROVIDER) { $env:NANOAGENT_PROVIDER } else { 'openrouter' })),
    '-e', ('NANOAGENT_MODEL=' + $(if ($env:NANOAGENT_MODEL) { $env:NANOAGENT_MODEL } else { 'poolside/laguna-m.1:free' })),
    '-e', ('NANOAGENT_THINKING=' + $(if ($env:NANOAGENT_THINKING) { $env:NANOAGENT_THINKING } else { 'on' })),
    '-e', ('NANOAGENT_REASONING=' + $(if ($env:NANOAGENT_REASONING) { $env:NANOAGENT_REASONING } else { 'high' })),
    '-e', ('NANOAGENT_API_KEY=' + $env:NANOAGENT_API_KEY),
    '-v', ($repoRoot + ':/workspace'),
    $imageTag
)

$dockerArgs += $NanoAiArgs

& docker @dockerArgs
exit $LASTEXITCODE
