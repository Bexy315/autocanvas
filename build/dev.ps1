# Runs AutoCanvas for development: the server (Development settings, SQLite in
# src/AutoCanvas.Server/data) and the web UI's Vite dev server, which sends /api to the server.
# Open http://localhost:5173. Ctrl+C stops both.
#
# The first start creates the admin below, who must choose a new password at the first sign-in.
# Later starts keep the existing accounts. Delete src/AutoCanvas.Server/data to start over.
$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$adminUser = 'admin'
$adminPassword = 'autocanvas-dev-admin'

Push-Location $root
try {
    if (-not (Test-Path 'src/web/node_modules')) {
        Push-Location 'src/web'
        try { npm ci } finally { Pop-Location }
    }

    $server = Start-Process -FilePath 'dotnet' -NoNewWindow -PassThru -ArgumentList @(
        'run', '--project', 'src/AutoCanvas.Server', '--',
        "--Bootstrap:AdminUserName=$adminUser", "--Bootstrap:AdminPassword=$adminPassword"
    )

    Write-Host ''
    Write-Host '  Web UI: http://localhost:5173 (API on http://localhost:5080)'
    Write-Host "  First start: sign in as '$adminUser' with '$adminPassword', then choose a new password."
    Write-Host ''

    Push-Location 'src/web'
    try { npm run dev } finally { Pop-Location }
}
finally {
    if ($server -and -not $server.HasExited) {
        # dotnet run starts the server as a child process; stop the whole tree.
        & taskkill /PID $server.Id /T /F | Out-Null
    }
    Pop-Location
}
