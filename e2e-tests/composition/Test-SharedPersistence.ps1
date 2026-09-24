# A disposable PostgreSQL, rebuilt Workbench, HTTP, restart, and same-target CLI journey.
# The C# fixture owns both databases and processes; requiring PostgreSQL makes Docker failures red.
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '../../tests/essentials/Persistence/EntityFrameworkCore/SharedResources/Tests/Elsa.Persistence.EntityFrameworkCore.SharedResources.Tests.csproj'
$previous = [Environment]::GetEnvironmentVariable('ELSA_SHARED_PERSISTENCE_REQUIRE_POSTGRESQL', 'Process')

try {
    [Environment]::SetEnvironmentVariable('ELSA_SHARED_PERSISTENCE_REQUIRE_POSTGRESQL', '1', 'Process')
    & dotnet test $project --configuration $Configuration --filter 'FullyQualifiedName~SharedPersistenceJourneyTests' --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        throw "Shared persistence end-to-end journey failed with exit code $LASTEXITCODE."
    }
}
finally {
    [Environment]::SetEnvironmentVariable('ELSA_SHARED_PERSISTENCE_REQUIRE_POSTGRESQL', $previous, 'Process')
}
