<#
.SYNOPSIS
    Shared helpers for the runtime-only artifact deployment suite (spec 151, T126).
.DESCRIPTION
    Sibling of ../artifact-deployment, which proves the same round trip on Elsa.Workbench. That host
    composes design, publishing and the compiler in one shell and environment variables can override a
    configuration value but never remove a key, so it can only prove "an engine whose store never held
    these definitions". This suite proves the stronger claim by building the importing engine out of
    packages: two Elsa.Foundation.Host processes, each with its own content root, feed, port and
    database, composed from a shells.json this suite writes.

    Elsa.Foundation.Host compiles in no Elsa feature. Every feature arrives as a .nupkg through a
    Nuplane directory feed, so the importing engine's feature surface IS its feed, and a feed with no
    Elsa.Workflows.Design.*, Elsa.Workflows.Publishing* or Elsa.Activities.Design.* package is an
    engine that cannot compile a workflow - structurally, not by configuration.

    THREE THINGS LEARNED THE HARD WAY, ALL LOAD-BEARING:

    1. Two feeds, not one. Nuplane never requests packages the host runtime already provides
       (CShells.*, Microsoft.Extensions.*), but every other declared dependency must resolve from some
       feed, and ONE unresolvable package fails the entire reconciliation cycle and aborts startup.
       Feed 1 holds the Elsa packages this suite packs; feed 2 holds the third-party packages those
       nuspecs actually ask for, copied out of the global NuGet cache. Both are directory feeds, so
       the run is offline and deterministic.

    2. Some of those third-party packages shadow the shared framework. Elsa.Api.FastEndpoints declares
       Microsoft.AspNetCore.Http.Abstractions 2.3.10 - the last standalone release of an assembly that
       is now part of the ASP.NET Core shared framework (Directory.Packages.props explains the pin).
       Nuplane resolves and loads the 2.3.10 copy into the shell load context, and every feature
       assembly that touches the modern surface then dies with
       "Could not load type 'Microsoft.AspNetCore.Builder.IEndpointConventionBuilder'". The fix is
       Nuplane:Loading:SharedAssemblies - the package still has to be present for resolution, but the
       loader takes the host's copy. See $script:SharedAssemblies.

    3. The CLR activity scanner reads a FOLDER of assemblies, and this host's base directory has none.
       Elsa.Workbench populates its design catalog for free because its bin folder contains every
       activity assembly. Here the assemblies live inside .nupkg files, so the suite extracts
       lib/net10.0 out of the packed Elsa.Activities.* packages into a flat scan folder and points
       ClrActivityReconciliation:Options:FolderPath at it.
#>

. "$PSScriptRoot/../_ElsaCommon.ps1"

$ErrorActionPreference = 'Stop'

$script:RepoRoot = (Resolve-Path "$PSScriptRoot/../..").Path
$script:HostProjectDir = Join-Path $script:RepoRoot "src/Apps/Elsa.Foundation.Host"
$script:HostDll = Join-Path $script:HostProjectDir "bin/Debug/net10.0/Elsa.Foundation.Host.dll"
$script:NuGetCache = Join-Path $env:USERPROFILE ".nuget/packages"

# The assembly-name prefixes a runtime-only engine must not carry. Mirrors
# tests/Elsa/Architecture/RuntimeOnlyArtifactCompositionTests.cs; Elsa.Workflows.Publishing has no
# trailing dot on purpose - the engine assembly itself is the one that must stay out.
$script:ForbiddenPrefixes = @('Elsa.Workflows.Design', 'Elsa.Workflows.Publishing', 'Elsa.Activities.Design')

# Package id prefixes Nuplane never requests because the Foundation.Host runtime already provides
# them. Putting them in a feed is how the earlier attempt ended up with a 200-package graph.
$script:HostProvidedPrefixes = @(
    'Microsoft.Extensions.', 'CShells.', 'Nuplane', 'FastEndpoints', 'NuGet.',
    'Newtonsoft.Json', 'JetBrains.Annotations', 'Microsoft.Bcl.', 'Elsa.Api.AspNetCore'
)

# Assemblies the feed supplies but the HOST must win for: framework assemblies whose standalone
# package froze at an old version (see note 2 above), plus the CShells contracts feed features
# implement against. Without these the shell loads a netstandard2.0 shim over the shared framework.
$script:SharedAssemblies = @(
    @{ Name = 'CShells.Abstractions'; PublicKeyToken = $null; MajorVersion = 0 },
    @{ Name = 'CShells.AspNetCore.Abstractions'; PublicKeyToken = $null; MajorVersion = 0 },
    @{ Name = 'CShells.FastEndpoints.Abstractions'; PublicKeyToken = $null; MajorVersion = 0 },
    @{ Name = 'Microsoft.AspNetCore.Http.Abstractions'; PublicKeyToken = 'adb9793829ddae60'; MajorVersion = 10 },
    @{ Name = 'Microsoft.AspNetCore.Http.Features'; PublicKeyToken = 'adb9793829ddae60'; MajorVersion = 10 },
    @{ Name = 'System.Text.Encodings.Web'; PublicKeyToken = 'cc7b13ffcd2ddd51'; MajorVersion = 10 },
    @{ Name = 'System.Memory'; PublicKeyToken = 'cc7b13ffcd2ddd51'; MajorVersion = 10 }
)

# --- the two compositions ---------------------------------------------------------------------
#
# Instance B is the point of this suite, and every entry below was added because a boot failed
# without it. The column after each feature is the assertion it serves; a feature that cannot be
# mapped to one does not belong here.

$script:RuntimeOnlyProjects = @(
    'src/Elsa/Api/FastEndpoints/Elsa.Api.FastEndpoints.csproj'                                  # ApiSecurity
    'src/Elsa/Serialization/SystemText/Elsa.Serialization.SystemText.csproj'                    # Serialization
    'src/Elsa/Events/Elsa.Events.csproj'                                                        # Events
    'src/Elsa/Mediator/Elsa.Mediator.csproj'                                                    # Mediator
    'src/Elsa/Tasks/Elsa.Tasks.csproj'                                                          # Tasks
    'src/Elsa/Locking/FileSystem/Elsa.Locking.FileSystem.csproj'                                # FileSystemDistributedLocking
    'src/Elsa/Persistence/Groundwork/Sqlite/Elsa.Persistence.Groundwork.Sqlite.csproj'          # GroundworkRuntimePersistenceSqlite
    'src/Elsa/Workflows/Runtime/Api/Elsa.Workflows.Runtime.Api.csproj'                          # WorkflowsRuntimeApi + Triggers
    'src/Elsa/Workflows/Runtime/Resumption/Elsa.Workflows.Runtime.Resumption.csproj'            # WorkflowsRuntimeResumption
    'src/Elsa/Workflows/Runtime/Scheduling/Elsa.Workflows.Runtime.Scheduling.csproj'            # WorkflowsRuntimeRecurringTriggers
    'src/Elsa/Workflows/Runtime/Reconciliation/Elsa.Workflows.Runtime.Reconciliation.csproj'    # JsonWorkflowArtifactReconciliation
    'src/Elsa/Activities/Runtime/Elsa.Activities.Runtime.csproj'                                # ActivitiesRuntime
    'src/Elsa/Activities/Primitives/Elsa.Activities.Primitives.csproj'                          # ActivitiesPrimitives
    'src/Elsa/Activities/Sequence/Runtime/Elsa.Activities.Sequence.Runtime.csproj'              # ActivitiesSequenceRuntime
    'src/Elsa/Activities/DispatchWorkflow/Runtime/Elsa.Activities.DispatchWorkflow.Runtime.csproj' # ActivitiesDispatchWorkflowRuntime
)

function New-RuntimeOnlyFeatures {
    param([Parameter(Mandatory)][string] $DatabasePath, [Parameter(Mandatory)][string] $MountPath,
          [Parameter(Mandatory)][string] $SourceId, [switch] $WithoutLocking)
    # [ordered] so the emitted shells.json reads in composition order rather than hash order.
    $features = [ordered]@{
        # Maps the runtime API the suite reads (activation slots, executables, execute).
        FastEndpoints = @{ EndpointRoutePrefix = "" }
        # Lets the suite read those endpoints without composing an identity stack on a runtime-only engine.
        ApiSecurity = @{ AllowAnonymous = $true }
        # Decodes the mounted closure. Without it nothing can be imported at all.
        Serialization = @{}
        # DEMANDED BY A FAILED BOOT: Serialization's JsonPayloadConvertersInitializingStartupTask
        # takes IInlineEventPublisher, so startup tasks refuse to run without it.
        Events = @{}
        # DEMANDED BY A FAILED BOOT: the runtime API endpoints take IRequestSender; endpoint mapping
        # failed shell activation without it.
        Mediator = @{}
        # Runs the reconciler's startup task; the reconciliation feature declares DependsOn Tasks.
        Tasks = @{}
        # The reconcile pass is a [SingleNodeTask] guarded by IDistributedLockProvider. Phase F removes
        # this one entry and asserts the engine refuses to start.
        FileSystemDistributedLocking = @{}
        # Durable runtime lane. Idempotency (phase C) and latest-wins (phase D) are only meaningful
        # across a restart, which needs a store that survives one. NOT the Unified package: that one
        # reaches Elsa.Workflows.Publishing.Core through ReferenceComposition, which is where the
        # compiler lives, and would silently give this engine the thing it is meant to lack.
        GroundworkRuntimePersistenceSqlite = @{ ConnectionString = "Data Source=$DatabasePath" }
        # Declared DependsOn of the persistence feature above.
        WorkflowsRuntimeResumption = @{}
        # Serves every HTTP assertion in phases B-D and the runtime half of phase E.
        WorkflowsRuntimeApi = @{}
        # Declared DependsOn of JsonWorkflowArtifactReconciliation - the trigger spine the activation
        # coordinator refuses to activate without.
        WorkflowsRuntimeTriggers = @{}
        # DEMANDED BY A FAILED PUBLISH/ACTIVATION: with a durable runtime store, activation throws
        # "found an IRecurringTriggerScheduleStore with no IRecurringTriggerScheduleProjectionPreparer".
        WorkflowsRuntimeRecurringTriggers = @{}
        # The feature under test.
        JsonWorkflowArtifactReconciliation = @{ Options = @{ SourceId = $SourceId; FolderPath = $MountPath } }
        # CLR activity registry; the reconciler's startup task declares [TaskDependency] on its scan,
        # and the import gate checks activity-type presence against it.
        ActivitiesRuntime = @{}
        # WriteLine - the activity whose output is this suite's execution evidence.
        ActivitiesPrimitives = @{}
        # The parent artifact is Sequence-rooted, which is T128's proof condition: a probe-leaf-only
        # workflow would never exercise the composite package split.
        ActivitiesSequenceRuntime = @{}
        # The parent waits on a DispatchWorkflow of the pinned child - the portability claim.
        ActivitiesDispatchWorkflowRuntime = @{}
    }
    if ($WithoutLocking) { $features.Remove('FileSystemDistributedLocking') }
    return $features
}

# Instance A exists only to author, publish and export. It is a Foundation.Host too so the suite is
# self-contained: it never touches the developer's server, port, or database, and it needs no
# Groundwork deploy step (AutoApplySchemaOnStartup defaults true).
$script:PublishCapableProjects = @(
    'src/Elsa/Api/FastEndpoints/Elsa.Api.FastEndpoints.csproj'
    'src/Elsa/Api/Capabilities/Elsa.Api.Capabilities.csproj'
    'src/Elsa/Serialization/SystemText/Elsa.Serialization.SystemText.csproj'
    'src/Elsa/Events/Elsa.Events.csproj'
    'src/Elsa/Mediator/Elsa.Mediator.csproj'
    'src/Elsa/Primitives/Hosting/Elsa.Primitives.Hosting.csproj'
    'src/Elsa/Tasks/Elsa.Tasks.csproj'
    'src/Elsa/Expressions/Elsa.Expressions.csproj'
    'src/Elsa/Locking/FileSystem/Elsa.Locking.FileSystem.csproj'
    'src/Elsa/Persistence/Groundwork/Sqlite/Unified/Elsa.Persistence.Groundwork.Sqlite.Unified.csproj'
    'src/Elsa/Workflows/Design/Api/Elsa.Workflows.Design.Api.csproj'
    'src/Elsa/Workflows/Design/Persistence/Groundwork/Elsa.Workflows.Design.Persistence.Groundwork.csproj'
    'src/Elsa/Workflows/Design/Validations/Elsa.Workflows.Design.Validations.csproj'
    'src/Elsa/Activities/Design/Api/Elsa.Activities.Design.Api.csproj'
    'src/Elsa/Activities/Design/Persistence/Groundwork/Elsa.Activities.Design.Persistence.Groundwork.csproj'
    'src/Elsa/Activities/Design/Reconciliation/Elsa.Activities.Design.Reconciliation.csproj'
    'src/Elsa/Activities/Design/Reconciliation/Clr/Elsa.Activities.Design.Reconciliation.Clr.csproj'
    'src/Elsa/Workflows/Publishing/Elsa.Workflows.Publishing.csproj'
    'src/Elsa/Workflows/Publishing/Api/Elsa.Workflows.Publishing.Api.csproj'
    'src/Elsa/Workflows/Publishing/Persistence/Groundwork/Elsa.Workflows.Publishing.Persistence.Groundwork.csproj'
    'src/Elsa/Workflows/Runtime/Api/Elsa.Workflows.Runtime.Api.csproj'
    'src/Elsa/Workflows/Runtime/Resumption/Elsa.Workflows.Runtime.Resumption.csproj'
    'src/Elsa/Workflows/Runtime/Scheduling/Elsa.Workflows.Runtime.Scheduling.csproj'
    'src/Elsa/Activities/Runtime/Elsa.Activities.Runtime.csproj'
    'src/Elsa/Activities/Primitives/Elsa.Activities.Primitives.csproj'
    'src/Elsa/Activities/Sequence/Runtime/Elsa.Activities.Sequence.Runtime.csproj'
    'src/Elsa/Activities/Sequence/Design/Elsa.Activities.Sequence.Design.csproj'
    'src/Elsa/Activities/DispatchWorkflow/Runtime/Elsa.Activities.DispatchWorkflow.Runtime.csproj'
    'src/Elsa/Activities/DispatchWorkflow/Design/Elsa.Activities.DispatchWorkflow.Design.csproj'
)

function New-PublishCapableFeatures {
    param([Parameter(Mandatory)][string] $DatabasePath, [Parameter(Mandatory)][string] $ScanFolder)
    [ordered]@{
        FastEndpoints = @{ EndpointRoutePrefix = "" }
        ApiSecurity = @{ AllowAnonymous = $true }
        ApiCapabilities = @{}
        Serialization = @{}
        Events = @{}
        Mediator = @{}
        # DEMANDED BY A FAILED BOOT: the design-side Groundwork commands take ISystemClock.
        Primitives = @{}
        Tasks = @{}
        Expressions = @{}
        FileSystemDistributedLocking = @{}
        GroundworkUnifiedPersistenceSqlite = @{ ConnectionString = "Data Source=$DatabasePath" }
        WorkflowsRuntimeResumption = @{}
        WorkflowsDesignApi = @{}
        # DEMANDED BY A FAILED PUBLISH: without IExpressionDraftSemanticValidator the publish endpoint
        # answers 503 expression-validation-unavailable before it ever reaches the compiler.
        WorkflowDesignValidations = @{}
        ActivitiesDesignApi = @{}
        ActivitiesDesignReconciliation = @{}
        # Populates the design activity catalog by scanning a folder (see note 3 in the file header).
        ClrActivityReconciliation = @{ Options = @{ FolderPath = $ScanFolder; SourceId = 'e2e-runtime-only-clr' } }
        WorkflowsPublishing = @{}
        WorkflowsPublishingApi = @{}
        WorkflowsPublishingGroundwork = @{}
        WorkflowsRuntimeApi = @{}
        WorkflowsRuntimeTriggers = @{}
        WorkflowsRuntimeRecurringTriggers = @{}
        ActivitiesRuntime = @{}
        ActivitiesPrimitives = @{}
        ActivitiesSequenceRuntime = @{}
        ActivitiesSequenceDesign = @{}
        ActivitiesDispatchWorkflowRuntime = @{}
        ActivitiesDispatchWorkflowDesign = @{}
    }
}

# --- filesystem -------------------------------------------------------------------------------

# Nuplane extracts installed packages under <contentRoot>/packages/.installed, whose paths exceed
# MAX_PATH; Remove-Item cannot delete them and the run would leak a directory per phase.
function Remove-Tree {
    param([Parameter(Mandatory)][string] $Path)
    if (-not (Test-Path $Path)) { return }
    & cmd /c rmdir /s /q "$($Path -replace '/', '\')" 2>&1 | Out-Null
    if (Test-Path $Path) { Remove-Item -Recurse -Force $Path -ErrorAction SilentlyContinue }
}

# --- feed construction ------------------------------------------------------------------------

function Get-ProjectClosure {
    param([Parameter(Mandatory)][string[]] $Roots)
    $seen = @{}
    $queue = New-Object System.Collections.Queue
    foreach ($root in $Roots) { $queue.Enqueue([System.IO.Path]::GetFullPath((Join-Path $script:RepoRoot $root))) }
    while ($queue.Count -gt 0) {
        $project = $queue.Dequeue()
        if ($seen.ContainsKey($project)) { continue }
        if (-not (Test-Path $project)) { throw "Project not found: $project" }
        $seen[$project] = $true
        $projectDir = Split-Path $project
        foreach ($node in ([xml](Get-Content $project -Raw)).SelectNodes("//ProjectReference")) {
            $include = $node.GetAttribute("Include")
            if (-not $include) { continue }
            $referenced = [System.IO.Path]::GetFullPath((Join-Path $projectDir ($include -replace '\\', '/')))
            if (-not $seen.ContainsKey($referenced)) { $queue.Enqueue($referenced) }
        }
    }
    return @($seen.Keys | Sort-Object)
}

# Pack every project in the union of both closures into one staging folder. Packing is unconditional
# (T126, Joey): a feed holding packages from an earlier build would let instance B run stale code and
# pass - or fail - for the wrong reason. It is also the slowest step in the suite, which is why the
# two feeds are filled by copying out of one staging pack rather than packing the overlap twice.
function New-PackedStage {
    param([Parameter(Mandatory)][string] $Stage, [Parameter(Mandatory)][string[]] $Projects)
    Remove-Tree $Stage
    New-Item -ItemType Directory -Force $Stage | Out-Null
    $index = 0
    foreach ($project in $Projects) {
        $index++
        $name = [System.IO.Path]::GetFileNameWithoutExtension($project)
        Write-Host ("  [pack {0,3}/{1}] {2}" -f $index, $Projects.Count, $name) -ForegroundColor DarkGray
        # No version flags, ever: overriding the version cascades into the nuspec's dependency
        # versions and yields packages whose dependencies point at versions no feed contains
        # (ADR 0067). Each csproj owns its own version.
        & dotnet pack $project -c Release -o $Stage --nologo -v q 2>&1 |
            Where-Object { $_ -match 'error' } | ForEach-Object { Write-Host "      $_" -ForegroundColor Red }
        if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed for $name" }
    }
}

function Copy-FeedSubset {
    param([Parameter(Mandatory)][string] $Stage, [Parameter(Mandatory)][string] $Feed,
          [Parameter(Mandatory)][string[]] $Projects)
    Remove-Tree $Feed
    New-Item -ItemType Directory -Force $Feed | Out-Null
    foreach ($project in $Projects) {
        $name = [System.IO.Path]::GetFileNameWithoutExtension($project)
        $package = Get-ChildItem $Stage -Filter "$name.*.nupkg" |
            Where-Object { $_.BaseName -match ('^' + [regex]::Escape($name) + '\.\d') } | Select-Object -First 1
        if (-not $package) { throw "Packed package missing for $name" }
        Copy-Item $package.FullName (Join-Path $Feed $package.Name) -Force
    }
}

function Get-NuspecDependencies {
    param([Parameter(Mandatory)][string] $NupkgPath)
    $archive = [System.IO.Compression.ZipFile]::OpenRead($NupkgPath)
    try {
        $entry = $archive.Entries | Where-Object { $_.FullName -like '*.nuspec' -and $_.FullName -notlike '*/*' } | Select-Object -First 1
        if (-not $entry) { return @() }
        $reader = New-Object System.IO.StreamReader($entry.Open())
        $xml = [xml]$reader.ReadToEnd()
        $reader.Dispose()
        # Only the nearest-framework dependency group counts. Walking every group drags in the
        # android/ios/tvos SQLitePCLRaw variants and netstandard.library, which this host never loads.
        $groups = @($xml.GetElementsByTagName("group"))
        $chosen = $null
        if ($groups.Count -gt 0) {
            foreach ($tfm in @('net10.0', 'net9.0', 'net8.0', 'net7.0', 'net6.0', 'netstandard2.1', 'netstandard2.0', '')) {
                $chosen = $groups | Where-Object { $_.GetAttribute("targetFramework") -eq $tfm } | Select-Object -First 1
                if ($chosen) { break }
            }
            if (-not $chosen) { $chosen = $groups[0] }
        }
        $nodes = if ($chosen) { $chosen.ChildNodes } else { $xml.GetElementsByTagName("dependency") }
        $dependencies = @()
        foreach ($node in $nodes) {
            if ($node.LocalName -ne 'dependency') { continue }
            $id = $node.GetAttribute("id")
            if (-not $id) { continue }
            $dependencies += [pscustomobject]@{ Id = $id; Version = ($node.GetAttribute("version") -replace '^\[|\]$|,.*$', '') }
        }
        return $dependencies
    }
    finally { $archive.Dispose() }
}

# Walk the packed nuspecs and copy every genuinely third-party dependency out of the global NuGet
# cache into a second directory feed. Nothing is downloaded: the packages are already there because
# the solution restored them.
function New-ThirdPartyFeed {
    param([Parameter(Mandatory)][string] $ElsaFeed, [Parameter(Mandatory)][string] $Feed)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    Remove-Tree $Feed
    New-Item -ItemType Directory -Force $Feed | Out-Null

    $elsaIds = @{}
    foreach ($package in (Get-ChildItem $ElsaFeed -Filter *.nupkg)) {
        $elsaIds[[regex]::Match($package.BaseName, '^(?<id>.+?)\.\d').Groups['id'].Value] = $true
    }

    $copied = @{}
    $missing = @()
    $pending = New-Object System.Collections.Queue
    foreach ($package in (Get-ChildItem $ElsaFeed -Filter *.nupkg)) {
        foreach ($dependency in (Get-NuspecDependencies $package.FullName)) { $pending.Enqueue($dependency) }
    }
    while ($pending.Count -gt 0) {
        $dependency = $pending.Dequeue()
        if ($elsaIds.ContainsKey($dependency.Id)) { continue }
        $hostProvided = $false
        foreach ($prefix in $script:HostProvidedPrefixes) { if ($dependency.Id -like "$prefix*") { $hostProvided = $true; break } }
        if ($hostProvided) { continue }
        $key = "$($dependency.Id)/$($dependency.Version)"
        if ($copied.ContainsKey($key)) { continue }
        $folder = Join-Path $script:NuGetCache ("{0}/{1}" -f $dependency.Id.ToLowerInvariant(), $dependency.Version)
        $source = Get-ChildItem $folder -Filter *.nupkg -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $source) { $missing += $key; continue }
        $copied[$key] = $true
        Copy-Item $source.FullName (Join-Path $Feed $source.Name) -Force
        foreach ($nested in (Get-NuspecDependencies $source.FullName)) { $pending.Enqueue($nested) }
    }
    # Reported, not thrown: a package missing from the cache is only fatal if Nuplane asks for it, and
    # it names the one it wanted loudly enough that guessing here would be noise.
    if ($missing.Count -gt 0) { Write-Host ("  [feed] not in the NuGet cache (only fatal if Nuplane asks): " + ($missing -join ', ')) -ForegroundColor DarkGray }
    return $copied.Count
}

# Flatten lib/net10.0 out of the packed Elsa.Activities.* packages so the reflection-only CLR scanner
# has a folder to read. See note 3 in the file header.
function New-ActivityScanFolder {
    param([Parameter(Mandatory)][string] $Feed, [Parameter(Mandatory)][string] $ScanFolder)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    Remove-Tree $ScanFolder
    New-Item -ItemType Directory -Force $ScanFolder | Out-Null
    foreach ($package in (Get-ChildItem $Feed -Filter 'Elsa.Activities.*.nupkg')) {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
        try {
            foreach ($entry in $archive.Entries) {
                if ($entry.FullName -like 'lib/net10.0/*.dll') {
                    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $ScanFolder $entry.Name), $true)
                }
            }
        }
        finally { $archive.Dispose() }
    }
    return (Get-ChildItem $ScanFolder -Filter *.dll).Count
}

# --- instance content roots -------------------------------------------------------------------

# Each instance gets its own content root holding just appsettings.json and shells.json. The host
# binary is shared and launched by absolute path; ASPNETCORE_CONTENTROOT points it here, so no file
# in the repository is touched and the two instances cannot see each other's configuration.
function New-InstanceRoot {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $ElsaFeed,
        [Parameter(Mandatory)][string] $ThirdPartyFeed,
        [Parameter(Mandatory)] $Features
    )
    Remove-Tree $Path
    New-Item -ItemType Directory -Force $Path | Out-Null

    $appsettings = @{
        Logging = @{
            LogLevel = @{
                Default = "Information"
                CShells = "Information"
                # Nuplane at Information is thousands of lines of resolution chatter, but phase E's
                # "what did this process actually load" assertion reads PackageLoader's one line per
                # loaded package - so that single category stays on and the rest is quiet.
                Nuplane = "Warning"
                "Nuplane.Loading.PackageLoader" = "Information"
                "Microsoft.AspNetCore" = "Warning"
            }
        }
        AllowedHosts = "*"
        Nuplane = @{
            Setup = @{
                AutomaticReconciliation = $true
                PollInterval = "01:00:00"
                Feeds = @(
                    @{ Name = "elsa-packages"; DirectoryPath = $ElsaFeed; IncludePatterns = @("*"); Directory = @{ Watch = $false } },
                    @{ Name = "thirdparty-packages"; DirectoryPath = $ThirdPartyFeed; IncludePatterns = @("*"); Directory = @{ Watch = $false } }
                )
            }
            Loading = @{ Enabled = $true; SharedAssemblies = $script:SharedAssemblies }
        }
        Elsa = @{
            Boot = @{ EagerShellActivation = @{ Enabled = $true } }
            Shells = @{ ReloadOnPackageChange = $false }
            ModuleManagement = @{ Enabled = $false; ApiKey = "" }
        }
    }
    $shells = @{ CShells = @{ Shells = @{ default = @{ Name = "default"; Features = $Features; Configuration = @{ WebRouting = @{ Path = "" } } } } } }

    ($appsettings | ConvertTo-Json -Depth 12) | Set-Content (Join-Path $Path "appsettings.json") -Encoding UTF8
    ($shells | ConvertTo-Json -Depth 12) | Set-Content (Join-Path $Path "shells.json") -Encoding UTF8
    return $Path
}

# --- server lifecycle -------------------------------------------------------------------------

$script:HostLogs = @{}

function Get-ListenerPid {
    param([Parameter(Mandatory)][int] $Port)
    if (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue) {
        return (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1).OwningProcess
    }
    if (Get-Command lsof -ErrorAction SilentlyContinue) {
        return (& lsof '-nP' "-iTCP:$Port" '-sTCP:LISTEN' '-t' 2>$null | Select-Object -First 1)
    }
    throw "Cannot identify the process listening on port ${Port}: neither Get-NetTCPConnection nor lsof is available."
}

function Stop-FoundationHost {
    param([Parameter(Mandatory)][int] $Port, [int] $TimeoutSec = 20)
    $listener = Get-ListenerPid -Port $Port
    if (-not $listener) { return }
    Write-Host "  [host] stopping pid $listener on port $Port"
    Stop-Process -Id $listener -Force -ErrorAction SilentlyContinue
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-ListenerPid -Port $Port) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 300 }
    Start-Sleep -Milliseconds 800   # let SQLite and the package store release their handles
}

# Every start gets its own log pair: the reconcile pass happens once, at activation, leaves no HTTP
# trace, and is the only place the import's own diagnostics are written. A shared log would be
# overwritten by the next phase and the evidence for a failed import would be gone.
function Start-FoundationHost {
    param(
        [Parameter(Mandatory)][string] $ContentRoot,
        [Parameter(Mandatory)][int] $Port,
        [Parameter(Mandatory)][string] $Label,
        [string] $LogRoot = $env:TEMP
    )
    if (-not (Test-Path $script:HostDll)) { throw "Elsa.Foundation.Host.dll not found at $script:HostDll - build the solution first (dotnet build)." }
    $out = Join-Path $LogRoot "elsa-runtimeonly-$Label.out.log"
    $err = Join-Path $LogRoot "elsa-runtimeonly-$Label.err.log"
    Remove-Item $out, $err -ErrorAction SilentlyContinue
    $script:HostLogs[$Label] = @{ Out = $out; Err = $err }

    $env:ASPNETCORE_URLS = "http://localhost:$Port"
    # Development plus ApiSecurity.AllowAnonymous is what removes the identity closure entirely.
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:ASPNETCORE_CONTENTROOT = $ContentRoot
    Write-Host "  [host] starting '$Label' on $Port (content root: $ContentRoot)"
    $start = @{
        FilePath = 'dotnet'
        ArgumentList = $script:HostDll
        WorkingDirectory = $ContentRoot
        RedirectStandardOutput = $out
        RedirectStandardError = $err
    }
    if ($env:OS -eq 'Windows_NT') { $start.WindowStyle = 'Hidden' }
    Start-Process @start | Out-Null
    Remove-Item Env:ASPNETCORE_CONTENTROOT -ErrorAction SilentlyContinue
    return $Label
}

# /health/ready turns 200 only after shell activation, which includes the artifact reconcile pass.
# Returns $false rather than throwing so phase F can assert on a host that must never become ready.
#
# The budget is generous on purpose. A first boot against a freshly packed feed installs and loads
# ~100 packages and applies the Groundwork schema, and it runs on a machine that has just finished
# ~100 Release builds; the publish-capable instance has been observed taking over four minutes.
# The heartbeat exists so a slow boot is visibly a slow boot rather than an apparent hang.
function Wait-FoundationReady {
    param([Parameter(Mandatory)][int] $Port, [int] $TimeoutSec = 420)
    $started = Get-Date
    $deadline = $started.AddSeconds($TimeoutSec)
    $nextHeartbeat = $started.AddSeconds(60)
    while ((Get-Date) -lt $deadline) {
        try {
            if ((Invoke-WebRequest "http://localhost:$Port/health/ready" -TimeoutSec 5 -UseBasicParsing).StatusCode -eq 200) {
                Write-Host ("  [ready] port {0} -> /health/ready 200 after {1:n0}s" -f $Port, ((Get-Date) - $started).TotalSeconds)
                return $true
            }
        }
        catch {}
        if ((Get-Date) -gt $nextHeartbeat) {
            Write-Host ("  [ready] still activating on port {0} ({1:n0}s elapsed of {2}s) ..." -f $Port, ((Get-Date) - $started).TotalSeconds, $TimeoutSec) -ForegroundColor DarkGray
            $nextHeartbeat = (Get-Date).AddSeconds(60)
        }
        Start-Sleep -Seconds 2
    }
    return $false
}

function Get-HostLog {
    param([Parameter(Mandatory)][string] $Label)
    $log = $script:HostLogs[$Label]
    if (-not $log -or -not (Test-Path $log.Out)) { return @() }
    return @(Get-Content $log.Out)
}

function Show-HostDiagnostics {
    param([Parameter(Mandatory)][string] $Label, [string] $Pattern = 'WorkflowArtifactReconcil|JsonWorkflowArtifactReconciliationSource|Committed runtime feature catalog', [int] $First = 10)
    $lines = @(Get-HostLog -Label $Label | Select-String -Pattern $Pattern | Select-Object -ExpandProperty Line -First $First)
    if ($lines.Count -eq 0) { Write-Host "  [$Label] nothing matched '$Pattern' in the host log" -ForegroundColor Yellow; return }
    foreach ($line in $lines) { Write-Host ("  [$Label] " + $line.Trim()) -ForegroundColor DarkGray }
}

function Show-HostLogTail {
    param([Parameter(Mandatory)][string] $Label, [int] $Lines = 30)
    $log = $script:HostLogs[$Label]
    if (-not $log) { return }
    foreach ($path in @($log.Err, $log.Out)) {
        if (-not (Test-Path $path)) { continue }
        Write-Host "  [log] --- tail of $path ---" -ForegroundColor DarkGray
        Get-Content $path -Tail $Lines | Where-Object { $_.Length -lt 400 } | ForEach-Object { Write-Host "  [log] $_" -ForegroundColor DarkGray }
    }
}

# --- HTTP -------------------------------------------------------------------------------------

# ApiSecurity.AllowAnonymous means there is no login endpoint to call, but _ElsaCommon's helpers all
# pass -WebSession, so hand them a real (empty) session object rather than $null.
function New-AnonymousContext {
    param([Parameter(Mandatory)][string] $BaseUrl)
    return @{ Session = (New-Object Microsoft.PowerShell.Commands.WebRequestSession); BaseUrl = $BaseUrl }
}

# Returns the HTTP status of a request that is expected to fail, so phase E can assert that a route
# is absent rather than merely that a call threw.
function Get-HttpStatus {
    param([Parameter(Mandatory)][string] $Url, [string] $Method = 'Get', [string] $Body)
    try {
        $arguments = @{ Uri = $Url; Method = $Method; TimeoutSec = 20; UseBasicParsing = $true }
        if ($Body) { $arguments.Body = $Body; $arguments.ContentType = 'application/json' }
        return [int](Invoke-WebRequest @arguments).StatusCode
    }
    catch {
        try { return [int]$_.Exception.Response.StatusCode } catch { return -1 }
    }
}

# --- assertion tally --------------------------------------------------------------------------

$script:APass = 0
$script:ATotal = 0

function Assert-That {
    param([Parameter(Mandatory)][string] $Label, [Parameter(Mandatory)][bool] $Condition, [string] $Detail = "")
    $script:ATotal++
    if ($Condition) {
        $script:APass++
        Write-Host ("  OK   {0}" -f $Label) -ForegroundColor Green
    }
    else {
        Write-Host ("  FAIL {0}  {1}" -f $Label, $Detail) -ForegroundColor Red
    }
}

function Complete-RuntimeOnlySuite {
    param([string] $Area = "runtime-only artifact deployment")
    Write-Host ""
    if ($script:APass -eq $script:ATotal) {
        Write-Host ("== {0}: {1}/{2} passed ==" -f $Area, $script:APass, $script:ATotal) -ForegroundColor Green
        exit 0
    }
    Write-Host ("== {0}: {1}/{2} passed ==" -f $Area, $script:APass, $script:ATotal) -ForegroundColor Red
    exit 1
}
