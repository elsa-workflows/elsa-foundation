# Shared configuration and assertions for owned Rules acceptance hosts.

function Set-RulesFeatureConfiguration {
    param(
        [Parameter(Mandatory)][string] $ContentRoot,
        [Parameter(Mandatory)][string] $HostRoot,
        [Parameter(Mandatory)][string] $HostDepsPath,
        [Parameter(Mandatory)][string] $DeploymentReceiptPath,
        [string] $DatabasePath
    )

    $settingsPath = Join-Path $ContentRoot 'shells.json'
    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    $features = $settings.CShells.Shells.default.Features
    if (-not $features) { throw 'The copied Workbench shell configuration has no default feature map.' }

    $rulesDatabase = if ($DatabasePath) { $DatabasePath } else { Join-Path $ContentRoot 'rules-runtime.db' }
    $featureSettings = @{
        RulesRuntime = @{
            HostRoot = $HostRoot
            HostDepsPath = $HostDepsPath
            DeploymentReceiptPath = $DeploymentReceiptPath
            MaxConcurrentAttempts = 1
            PollIntervalMilliseconds = 10
        }
        RulesRuntimeEntityFrameworkCore = @{
            Provider = 'Sqlite'
            ConnectionString = "Data Source=$rulesDatabase;Pooling=False"
            Pooling = $false
            WorkLeaseSeconds = 60
            DeliveryLeaseSeconds = 60
        }
        RulesWorkflowsDesign = @{}
        RulesWorkflowsRuntime = @{
            MaxConcurrentDeliveries = 1
            PollIntervalMilliseconds = 10
        }
    }

    foreach ($featureName in $featureSettings.Keys) {
        $features | Add-Member -MemberType NoteProperty -Name $featureName -Value $featureSettings[$featureName] -Force
    }
    $settings | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $settingsPath -Encoding utf8
}

function Get-RequiredNodeExecution {
    param([Parameter(Mandatory)] $Instance, [Parameter(Mandatory)][string] $NodeId)
    $matches = @($Instance.activities | Where-Object { $_.executableNodeId -ceq $NodeId })
    if ($matches.Count -ne 1) {
        throw "Expected exactly one activity execution for '$NodeId'; found $($matches.Count)."
    }
    return $matches[0]
}
