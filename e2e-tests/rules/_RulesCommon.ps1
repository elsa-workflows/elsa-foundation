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

function New-RulesActivityNode {
    param(
        [Parameter(Mandatory)][string] $NodeId,
        [Parameter(Mandatory)][string] $VersionId,
        [Parameter(Mandatory)][string] $Program,
        [Parameter(Mandatory)][string[]] $InitialFacts,
        [Parameter(Mandatory)][int] $MaxRounds
    )

    New-ActivityNode -NodeId $NodeId -VersionId $VersionId -Inputs @(
        (New-LiteralInput -ReferenceKey 'Program' -Value $Program),
        (New-LiteralInput -ReferenceKey 'InitialFacts' -Value $InitialFacts),
        (New-LiteralInput -ReferenceKey 'MaxRounds' -Value $MaxRounds)
    )
}

function New-RulesResultCaptureNode {
    param([Parameter(Mandatory)][string] $ProducerScopeId)

    $resultBinding = @{
        value = @{
            producerNodeId = 'rules'
            projectionKey = '$result'
            producerScopeId = $ProducerScopeId
        }
        expressionType = 'ActivityResult'
    }
    New-SetOutputNode -NodeId 'capture-rules-result' -OutputName 'rulesResult' `
        -Value $resultBinding -ValueAlias 'Elsa.Any'
}

function Get-FieldValue {
    param([AllowNull()] $Object, [Parameter(Mandatory)][string] $Name)
    if ($null -eq $Object) { return $null }
    if ($Object -is [System.Collections.IDictionary] -and $Object.Contains($Name)) { return $Object[$Name] }
    $property = $Object.PSObject.Properties[$Name]
    if ($property) { return $property.Value }
    return $null
}

function Convert-DiagnosticSnapshot {
    param([AllowNull()] $Snapshot)
    if ($null -eq $Snapshot) { return $null }

    $snapshotKind = Get-FieldValue -Object $Snapshot -Name 'kind'
    switch ($snapshotKind) {
        'object' {
            if ((Get-FieldValue -Object $Snapshot -Name 'truncated') -eq $true) {
                throw 'The native Rules result diagnostic snapshot was truncated.'
            }
            $properties = @(Get-FieldValue -Object $Snapshot -Name 'properties')
            $values = [ordered]@{}
            foreach ($property in $properties) {
                $name = [string](Get-FieldValue -Object $property -Name 'name')
                if ([string]::IsNullOrWhiteSpace($name) -or $name -eq '...') {
                    throw 'The native Rules result diagnostic snapshot omitted an object property.'
                }
                $values[$name] = Convert-DiagnosticSnapshot (Get-FieldValue -Object $property -Name 'value')
            }
            return [pscustomobject]$values
        }
        'array' {
            if ((Get-FieldValue -Object $Snapshot -Name 'truncated') -eq $true) {
                throw 'The native Rules result diagnostic snapshot truncated its fact array.'
            }
            $items = [System.Collections.Generic.List[object]]::new()
            foreach ($item in @(Get-FieldValue -Object $Snapshot -Name 'items')) {
                $items.Add((Convert-DiagnosticSnapshot $item))
            }
            $itemCount = Get-FieldValue -Object $Snapshot -Name 'itemCount'
            if ($null -ne $itemCount -and [int]$itemCount -ne $items.Count) {
                throw 'The native Rules result diagnostic snapshot did not retain every array item.'
            }
            return ,$items.ToArray()
        }
        'string' {
            if ((Get-FieldValue -Object $Snapshot -Name 'truncated') -eq $true) {
                throw 'A string in the native Rules result diagnostic snapshot was truncated.'
            }
            return Get-FieldValue -Object $Snapshot -Name 'preview'
        }
        { $_ -in @('number', 'scalar') } { return Get-FieldValue -Object $Snapshot -Name 'value' }
        'null' { return $null }
        default { return $Snapshot }
    }
}

function Get-NativeResultSummary {
    param([Parameter(Mandatory)] $Instance)

    $outputProperty = @($Instance.outputs.PSObject.Properties | Where-Object { $_.Name -ceq 'rulesResult' })
    if ($outputProperty.Count -ne 1) { throw 'The public workflow-instance response did not contain exactly one rulesResult output.' }
    $output = $outputProperty[0].Value
    if ((Get-FieldValue -Object $output -Name 'isRedacted') -ne $false) {
        throw 'The public workflow-instance API did not expose the native Rules result inline.'
    }

    $payload = Convert-DiagnosticSnapshot (Get-FieldValue -Object $output -Name 'value')
    $kindValue = Get-FieldValue -Object $payload -Name 'kind'
    $kindText = [string]$kindValue
    $kind = switch ($kindText) {
        { $_ -in @('Succeeded', '0') } { 'Succeeded'; break }
        { $_ -in @('Faulted', '1') } { 'Faulted'; break }
        { $_ -in @('Cancelled', '2') } { 'Cancelled'; break }
        { $_ -in @('Refused', '3') } { 'Refused'; break }
        default { throw "The public Rules result had an unknown terminal kind '$kindText'." }
    }
    $facts = @(Get-FieldValue -Object $payload -Name 'resultFacts')
    $atoms = @($facts | ForEach-Object { [string](Get-FieldValue -Object $_ -Name 'atom') } | Sort-Object -CaseSensitive)
    if ($atoms | Where-Object { [string]::IsNullOrWhiteSpace($_) }) {
        throw 'The public Rules result contained an empty fact atom.'
    }

    $diagnostic = Get-FieldValue -Object $payload -Name 'diagnostic'
    [pscustomobject]@{
        Kind = $kind
        CommittedRounds = [int](Get-FieldValue -Object $payload -Name 'committedRounds')
        ResultSchemaVersion = [int](Get-FieldValue -Object $payload -Name 'resultSchemaVersion')
        ResultFacts = $atoms
        DiagnosticCode = Get-FieldValue -Object $diagnostic -Name 'code'
        FinalFactsSha256 = [string](Get-FieldValue -Object $payload -Name 'finalFactsSha256')
        IsRedacted = $false
        OutputType = Get-FieldValue -Object $output -Name 'type'
    }
}
