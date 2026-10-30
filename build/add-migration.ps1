# Adds an EF Core migration for every database provider at once (ADR-0010, skill add-migration).
# Usage: build/add-migration.ps1 <MigrationName>
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Z][A-Za-z0-9]+$')]
    [string] $Name
)

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet tool restore | Out-Null

foreach ($provider in 'Postgres', 'Sqlite') {
    $project = "src/AutoCanvas.Persistence.$provider"
    Write-Host "== $provider"
    dotnet ef migrations add $Name --project $project --startup-project $project --output-dir Migrations
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
