param(
    [switch] $Check
)

$generatorProject = Join-Path $PSScriptRoot "../maps/Elsa.Maps.Generator"
$command = if ($Check) { "solution-filters-check" } else { "solution-filters" }

dotnet run --project $generatorProject -- $command
exit $LASTEXITCODE
