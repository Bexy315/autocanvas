#Requires -Version 7
<#
.SYNOPSIS
Installs AutoCanvas with docker compose: the server with its web UI and a runner, in a directory of
its own. Run it again on the same directory to update; its .env is kept.

.DESCRIPTION
Until the first release the images are built from this checkout. docs/user/installation.md explains
every step, also for installing without the script. Needs Docker Desktop (or Docker Engine) with
Compose v2.

.EXAMPLE
deploy/install.ps1 -Dir C:\autocanvas -Postgres
#>
param(
    [string]$Dir = './autocanvas',
    [switch]$Postgres,
    [int]$Port = 5080,
    [int]$RunnerPort = 5081
)
$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    Write-Error "install: $Message" -ErrorAction Continue
    exit 1
}

function Invoke-Docker {
    & docker @args
    if ($LASTEXITCODE -ne 0) { Fail "'docker $($args -join ' ')' failed." }
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { Fail 'Docker is needed: https://docs.docker.com/desktop/' }
docker compose version *> $null
if ($LASTEXITCODE -ne 0) { Fail "Docker Compose v2 is needed ('docker compose')." }
docker info *> $null
if ($LASTEXITCODE -ne 0) { Fail 'Docker is not running, or this user may not use it.' }

$here = $PSScriptRoot
$repo = Split-Path $here -Parent
if (-not (Test-Path (Join-Path $repo 'src/AutoCanvas.Server/Dockerfile'))) {
    Fail 'Run this script from a checkout of AutoCanvas: published images come with the first release.'
}

Write-Host 'Building the images (this takes a few minutes the first time)...'
Invoke-Docker build --quiet -f (Join-Path $repo 'src/AutoCanvas.Server/Dockerfile') -t autocanvas-server:local $repo | Out-Null
Invoke-Docker build --quiet -f (Join-Path $repo 'src/AutoCanvas.Runner/Dockerfile') -t autocanvas-runner:local $repo | Out-Null

New-Item -ItemType Directory -Force $Dir | Out-Null
$Dir = (Resolve-Path $Dir).Path
foreach ($file in 'compose.yaml', 'compose.postgres.yaml', '.env.example') {
    Copy-Item (Join-Path $here "compose/$file") $Dir -Force
}
Push-Location $Dir
try {
    if (-not (Test-Path .env)) {
        $project = ((Split-Path $Dir -Leaf).ToLowerInvariant() -replace '[^a-z0-9_-]', '-')
        $files = 'compose.yaml'
        $password = ''
        if ($Postgres) {
            # Compose separates the files of COMPOSE_FILE with ';' on Windows.
            $files = 'compose.yaml;compose.postgres.yaml'
            $password = -join ([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(24) | ForEach-Object { $_.ToString('x2') })
        }
        $gid = (docker run --rm -v /var/run/docker.sock:/s alpine:3.22 stat -c %g /s).Trim()
        if ($LASTEXITCODE -ne 0) { Fail 'Could not read the group of the Docker socket.' }
        @(
            '# Written by deploy/install.ps1; see .env.example for every setting.'
            "COMPOSE_PROJECT_NAME=$project"
            "COMPOSE_FILE=$files"
            "AUTOCANVAS_PORT=$Port"
            "AUTOCANVAS_RUNNER_PORT=$RunnerPort"
            'AUTOCANVAS_SERVER_IMAGE=autocanvas-server:local'
            'AUTOCANVAS_RUNNER_IMAGE=autocanvas-runner:local'
            "AUTOCANVAS_DOCKER_GID=$gid"
            "AUTOCANVAS_POSTGRES_PASSWORD=$password"
        ) | Set-Content -Encoding utf8NoBOM .env

        # The runner's volume must exist to know where it is on the Docker host.
        Invoke-Docker compose create | Out-Null
        $volume = (docker volume inspect -f '{{ .Mountpoint }}' "${project}_runner").Trim()
        Add-Content -Encoding utf8NoBOM .env "AUTOCANVAS_RUNNER_HOST_DATA=$volume"
    }

    Write-Host 'Starting AutoCanvas...'
    Invoke-Docker compose up -d --remove-orphans

    # The server logs the setup address once it listens; an installation set up already logs none.
    $Port = [int]((Get-Content .env | Where-Object { $_ -like 'AUTOCANVAS_PORT=*' }) -replace '^AUTOCANVAS_PORT=', '')
    $token = $null
    $started = 0
    for ($i = 0; $i -lt 120 -and -not $token; $i++) {
        $logs = (docker compose logs --no-color server 2>$null) -join "`n"
        $found = [regex]::Matches($logs, 'setup\?token=([A-Za-z0-9_-]+)')
        if ($found.Count -gt 0) {
            $token = $found[$found.Count - 1].Groups[1].Value
            break
        }
        if ($logs -match 'Application started') {
            $started++
            if ($started -ge 3) { break }
        }
        Start-Sleep -Seconds 2
    }

    Write-Host ''
    Write-Host "AutoCanvas is running in $Dir."
    if ($token) {
        Write-Host "Open http://<this machine's address>:$Port/setup in a browser and enter the setup token:"
        Write-Host ''
        Write-Host "  $token"
        Write-Host ''
        Write-Host "The token changes when the server restarts; 'docker compose logs server' in $Dir shows it again."
    }
    elseif ($started -ge 3) {
        Write-Host "It is set up already; it was updated and restarted. Open http://<this machine's address>:$Port"
    }
    else {
        Fail "The server did not start in time. See 'docker compose logs server' in $Dir."
    }
}
finally {
    Pop-Location
}
