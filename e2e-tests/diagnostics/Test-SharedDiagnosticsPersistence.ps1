# Disposable PostgreSQL proof for the explicit diagnostics resource layout.
# The C# fixture owns Workbench, both databases, restart and same-source CLI checks.
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '../../tests/essentials/Persistence/EntityFrameworkCore/SharedResources/Tests/Elsa.Persistence.EntityFrameworkCore.SharedResources.Tests.csproj'
$previous = [Environment]::GetEnvironmentVariable('ELSA_SHARED_PERSISTENCE_REQUIRE_POSTGRESQL', 'Process')

try {
    [Environment]::SetEnvironmentVariable('ELSA_SHARED_PERSISTENCE_REQUIRE_POSTGRESQL', '1', 'Process')
    & dotnet test $project --configuration $Configuration --filter 'FullyQualifiedName~Explicit_diagnostics_bindings_place_both_histories_on_the_second_target_after_restart|FullyQualifiedName~Split_diagnostics_targets_refuse_before_any_database_migration|FullyQualifiedName~Separate_diagnostics_resource_keeps_representative_workflow_on_primary_after_restart' --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        throw "Shared diagnostics persistence journey failed with exit code $LASTEXITCODE."
    }
}
finally {
    [Environment]::SetEnvironmentVariable('ELSA_SHARED_PERSISTENCE_REQUIRE_POSTGRESQL', $previous, 'Process')
}
