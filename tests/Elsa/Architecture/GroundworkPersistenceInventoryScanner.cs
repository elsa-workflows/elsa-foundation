using System.Text.RegularExpressions;

namespace Elsa.Architecture.Tests;

internal sealed class GroundworkPersistenceInventoryScanner(string repositoryRoot)
{
    private static readonly Regex DurableContractPattern = new(
        @"\bpublic\s+interface\s+(?<contract>I[A-Za-z0-9_]*(?:Store|Repository|Transport|Scanner|OwnershipService|WorkQueue|Writer))\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex RegistrationPattern = new(
        @"\b[A-Za-z_][A-Za-z0-9_]*\s*\.\s*(?:Try)?Add(?:Keyed)?(?:Singleton|Scoped|Transient)\s*<(?<generic>[^>]+)>\s*\((?<factory>.*?)\)\s*;",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);
    private static readonly Regex RegistrationLikePattern = new(
        @"\b[A-Za-z_][A-Za-z0-9_]*\s*\.\s*(?:Try)?Add[A-Za-z0-9_]*\s*(?:<[^;]+?>)?\s*\([^;]*?\)\s*;",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);
    private static readonly Regex FactoryImplementationPattern = new(
        @"(?:new\s+|GetRequired(?:Keyed)?Service<)\s*(?<implementation>(?:global::)?(?:[A-Za-z_][A-Za-z0-9_]*\.)*[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ServiceDescriptorInvocationPattern = new(
        @"\b(?:new\s+)?ServiceDescriptor\s*(?:\.\s*[A-Za-z_][A-Za-z0-9_]*\s*(?:<(?<generic>[^>]+)>)?)?\s*\((?<arguments>.*?)\)\s*;",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);
    private static readonly Regex TypeAliasPattern = new(
        @"\b(?:var|(?:System\s*\.\s*)?Type)\s+(?<variable>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*typeof\s*\(\s*(?<type>(?:global::)?(?:[A-Za-z_][A-Za-z0-9_]*\.)*[A-Za-z_][A-Za-z0-9_]*)\s*\)\s*;",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UnitHelperStorageUnitPattern = new(
        @"\b(?:Unit|Scoped|Global)\s*\(\s*(?:[A-Za-z_][A-Za-z0-9_]*\.)*(?<unit>[A-Za-z0-9_]+DocumentKind)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex StorageUnitIdentityPattern = new(
        @"\bStorageUnitIdentity\s*\(\s*(?:[A-Za-z_][A-Za-z0-9_]*\.)*(?<unit>[A-Za-z0-9_]+DocumentKind)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex StorageUnitDeclarePattern = new(
        @"\bStorageUnit\s*\.\s*Declare\s*\(\s*(?:[A-Za-z_][A-Za-z0-9_]*\.)*(?<unit>[A-Za-z0-9_]*UnitId)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DocumentKindDeclarationPattern = new(
        @"\b(?:const\s+string|static\s+readonly\s+string)\s+(?<unit>[A-Za-z0-9_]+DocumentKind)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex UnitIdDeclarationPattern = new(
        @"\b(?:const\s+string|static\s+readonly\s+string)\s+(?<unit>[A-Za-z0-9_]*UnitId)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DocumentKindInvocationPattern = new(
        @"\b(?<factory>[A-Za-z_][A-Za-z0-9_]*)\s*\(\s*(?:[A-Za-z_][A-Za-z0-9_]*\.)*(?<unit>[A-Za-z0-9_]+DocumentKind)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PotentialStorageUnitDeclarationPattern = new(
        @"\b(?<factory>Unit|StorageUnitIdentity)\s*\(\s*(?<argument>[^,\)\r\n]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PotentialStorageUnitDeclarePattern = new(
        @"\bStorageUnit\s*\.\s*Declare\s*\(\s*(?<argument>[^,\)\r\n]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, string[]> RegistrationStorageUnits =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["IBookmarkStateStore"] = ["BookmarkStateDocumentKind"],
            ["IWorkflowExecutableStore"] =
                ["WorkflowExecutableDocumentKind", "WorkflowExecutableCoordinationDocumentKind"],
            ["IExecutableActivityTemplateStore"] =
                ["ExecutableActivityTemplateDocumentKind", "ExecutableActivityTemplateHashClaimDocumentKind"],
            ["IExecutableActivityTemplateWriter"] =
                ["ExecutableActivityTemplateDocumentKind", "ExecutableActivityTemplateHashClaimDocumentKind"],
            ["IWorkflowExecutableSourceReferenceStore"] = ["WorkflowExecutableSourceReferenceDocumentKind"],
            ["IWorkflowExecutableSourceReferenceWriter"] = ["WorkflowExecutableSourceReferenceDocumentKind"],
            ["IActivityExecutionStateStore"] = ["ActivityExecutionStateDocumentKind"],
            ["IActivityExecutionInspectionStore"] = ["ActivityExecutionInspectionDocumentKind"],
            ["IActivityExecutionInspectionWriter"] = ["ActivityExecutionInspectionDocumentKind"],
            ["IActivityExecutionHierarchyStore"] = ["ActivityExecutionHierarchyDocumentKind"],
            ["IActivityExecutionHierarchyWriter"] = ["ActivityExecutionHierarchyDocumentKind"],
            ["IWorkflowExecutionStateStore"] = ["WorkflowExecutionStateDocumentKind"],
            ["IWorkflowAlterationStore"] =
                ["WorkflowAlterationPlanDocumentKind", "WorkflowAlterationJobDocumentKind"],
            ["IWorkflowTestScopeStore"] = ["WorkflowTestScopeDocumentKind"],
            ["IWorkflowTestScopeAdmissionStore"] = ["WorkflowTestScopeDocumentKind", "WorkflowDispatchDocumentKind"],
            ["IWorkflowTestScopeCleanupStore"] =
                ["WorkflowTestScopeDocumentKind", "WorkflowDispatchDocumentKind", "PostCommitOutboxDocumentKind"],
            ["IDurableValueStateStore"] = ["DurableValueStateDocumentKind"],
            ["ISchedulerStateStore"] = ["SchedulerStateDocumentKind"],
            ["IExecutionLivenessStateStore"] = ["ExecutionLivenessStateDocumentKind"],
            ["IRuntimeExecutionOwnershipService"] = ["ExecutionLivenessStateDocumentKind"],
            ["IRuntimeRecoveryScanner"] = ["ExecutionLivenessStateDocumentKind"],
            ["IWorkflowHoldStateStore"] = ["WorkflowHoldStateDocumentKind"],
            ["IIncidentStateStore"] = ["IncidentStateDocumentKind"],
            ["IRuntimeCheckpointCommitStore"] = ["CheckpointCommitDocumentKind"],
            ["IRuntimePostCommitOutboxStore"] = ["PostCommitOutboxDocumentKind"],
            ["IPostCommitOutboxLookupStore"] = ["PostCommitOutboxDocumentKind"],
            ["IRuntimePostCommitOutboxClaimStore"] = ["PostCommitOutboxDocumentKind"],
            ["IRuntimePostCommitOutboxClaimCompletionStore"] =
                ["PostCommitOutboxDocumentKind", "WorkflowDispatchDocumentKind", "WorkflowExecutionStateDocumentKind"],
            ["IWorkflowDispatchStore"] = ["WorkflowDispatchDocumentKind"],
            ["IWorkflowDispatchQueryStore"] = ["WorkflowDispatchDocumentKind"],
            ["IWorkflowDispatchDeleteStore"] = ["WorkflowDispatchDocumentKind"],
            ["IWorkflowDispatchRetentionRootStore"] = ["WorkflowDispatchDocumentKind"],
            ["IWorkflowDispatchAdmissionStore"] = ["WorkflowDispatchDocumentKind"],
            ["IWorkflowDispatchCancellationStore"] = ["WorkflowDispatchDocumentKind"],
            ["IWorkflowDispatchRedriveStore"] = ["WorkflowDispatchDocumentKind", "PostCommitOutboxDocumentKind"],
            ["IWorkflowSchedulerPoisonStore"] = ["SchedulerPoisonDocumentKind"],
            ["IWorkflowSchedulerWorkQueue"] = ["SchedulerWorkItemDocumentKind"],
            ["IDurableTimerStore"] = ["DurableTimerDocumentKind"],
            ["IWorkflowTriggerBindingStore"] = ["WorkflowTriggerBindingDocumentKind"],
            ["IRecurringTriggerScheduleStore"] = ["RecurringTriggerScheduleDocumentKind"],
            ["IUserStore"] = ["IdentityUserDocumentKind"],
            ["IRoleStore"] = ["IdentityRoleDocumentKind"],
            ["IApplicationStore"] = ["IdentityApplicationDocumentKind"],
            ["ICredentialStore"] = ["IdentityCredentialDocumentKind"],
            ["IExternalIdentityStore"] = ["ExternalLoginDocumentKind"],
            ["IClaimMappingStore"] = ["IdentityClaimMappingDocumentKind"],
            ["IProviderConfigurationStore"] =
            [
                "IdentityProviderConfigurationDocumentKind",
                "IdentityGlobalProviderConfigurationDocumentKind"
            ],
            ["ITenantMembershipStore"] = ["IdentityTenantMembershipDocumentKind"],
            ["ISecretRepository"] = ["UnitId"],
            ["IRevisionAwareSecretRepository"] = ["UnitId"],
            ["IPagedSecretRepository"] = ["UnitId"],
            ["IExecutionPlacementStore"] = ["PlacementUnitId"],
            ["IExecutionCommandTransport"] = ["CommandTransportUnitId", "CommandStreamHeadUnitId"]
        };

    private static readonly string[] ContractSources =
    [
        "src/Elsa/Workflows/Runtime/Core/Contracts",
        "src/Elsa/Foundation/Identity/Abstractions/Iam/IamContracts.cs",
        "src/Elsa/Secrets/Core/Contracts/ISecretRepository.cs",
        "src/Elsa/Workflows/Runtime/Distributed/Contracts"
    ];

    private static readonly GroundworkFamilySource[] FamilySources =
    [
        new(
            "runtime",
            "src/Elsa/Persistence/Groundwork"),
        new(
            "iam",
            "src/Elsa/Foundation/Identity/Persistence/Groundwork"),
        new(
            "secrets",
            "src/Elsa/Secrets/Persistence/Groundwork"),
        new(
            "distributed-runtime",
            "src/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork")
    ];

    private readonly string _repositoryRoot = Path.GetFullPath(repositoryRoot);

    public GroundworkPersistenceInventorySnapshot Scan()
    {
        var durableContracts = ContractSources
            .SelectMany(SourceFiles)
            .SelectMany(file => DurableContractPattern.Matches(File.ReadAllText(file)))
            .Select(match => match.Groups["contract"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var durableContractSet = durableContracts.ToHashSet(StringComparer.Ordinal);

        var registrations = FamilySources
            .SelectMany(family => GroundworkSourceFiles(family.Root)
                .SelectMany(file => DiscoverRegistrations(family.Owner, file, durableContractSet)))
            .Distinct()
            .OrderBy(registration => registration.Owner, StringComparer.Ordinal)
            .ThenBy(registration => registration.Contract, StringComparer.Ordinal)
            .ThenBy(registration => registration.Implementation, StringComparer.Ordinal)
            .ToArray();

        var manifestStorageUnits = FamilySources
            .SelectMany(family => DiscoverManifestStorageUnits(family.Owner, GroundworkSourceFiles(family.Root)))
            .Distinct()
            .OrderBy(unit => unit.Owner, StringComparer.Ordinal)
            .ThenBy(unit => unit.StorageUnit, StringComparer.Ordinal)
            .ToArray();

        return new GroundworkPersistenceInventorySnapshot(durableContracts, registrations, manifestStorageUnits);
    }

    private IEnumerable<GroundworkManifestStorageUnit> DiscoverManifestStorageUnits(
        string owner,
        IEnumerable<string> files)
    {
        var discoveredUnits = new HashSet<string>(StringComparer.Ordinal);
        var declaredUnits = new List<(string Unit, string File)>();

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            var isManifestSource = Path.GetFileNameWithoutExtension(file)
                .Contains("Manifest", StringComparison.Ordinal);
            foreach (var unit in UnitHelperStorageUnitPattern.Matches(source)
                         .Concat(StorageUnitIdentityPattern.Matches(source))
                         .Concat(StorageUnitDeclarePattern.Matches(source))
                         .Select(match => match.Groups["unit"].Value))
            {
                discoveredUnits.Add(unit);
            }

            foreach (Match declaration in PotentialStorageUnitDeclarePattern.Matches(source))
            {
                var argument = declaration.Groups["argument"].Value.Trim();
                var isHelperImplementation = isManifestSource && argument == "id";
                var isRecognizedIdentity = Regex.IsMatch(
                    argument,
                    @"^(?:[A-Za-z_][A-Za-z0-9_]*\.)*(?:[A-Za-z_][A-Za-z0-9_]*UnitId|UnitId)$",
                    RegexOptions.CultureInvariant);
                if (isHelperImplementation || isRecognizedIdentity)
                    continue;

                throw UnparseableManifestDeclaration(file, argument);
            }

            if (!isManifestSource)
                continue;

            declaredUnits.AddRange(DocumentKindDeclarationPattern.Matches(source)
                .Select(match => (match.Groups["unit"].Value, file)));
            declaredUnits.AddRange(UnitIdDeclarationPattern.Matches(source)
                .Select(match => (match.Groups["unit"].Value, file)));

            foreach (Match invocation in DocumentKindInvocationPattern.Matches(source))
            {
                var factory = invocation.Groups["factory"].Value;
                if (factory is "Unit" or "Scoped" or "Global" or "StorageUnitIdentity")
                    continue;

                throw UnparseableManifestDeclaration(file, invocation.Groups["unit"].Value);
            }

            foreach (Match declaration in PotentialStorageUnitDeclarationPattern.Matches(source))
            {
                var factory = declaration.Groups["factory"].Value;
                var argument = declaration.Groups["argument"].Value.Trim();
                var isHelperSignature = factory == "Unit" && argument.StartsWith("string ", StringComparison.Ordinal);
                var isHelperImplementation = factory == "StorageUnitIdentity" && argument == "documentKind";
                var isRecognizedIdentity = Regex.IsMatch(
                    argument,
                    @"^(?:[A-Za-z_][A-Za-z0-9_]*\.)*[A-Za-z_][A-Za-z0-9_]*(?:DocumentKind|UnitId)$|^UnitId$",
                    RegexOptions.CultureInvariant);
                if (isHelperSignature || isHelperImplementation || isRecognizedIdentity)
                    continue;

                throw UnparseableManifestDeclaration(file, argument);
            }
        }

        foreach (var (unit, file) in declaredUnits)
        {
            if (!discoveredUnits.Contains(unit))
                throw UnparseableManifestDeclaration(file, unit);
        }

        return discoveredUnits.Select(unit => new GroundworkManifestStorageUnit(owner, unit));
    }

    private IEnumerable<GroundworkPersistenceRegistrationSnapshot> DiscoverRegistrations(
        string owner,
        string file,
        IReadOnlySet<string> durableContracts)
    {
        var source = File.ReadAllText(file);
        if (!source.Contains("IServiceCollection", StringComparison.Ordinal))
            return [];

        var parsedSpans = new List<(int Index, int Length)>();
        var registrations = new List<GroundworkPersistenceRegistrationSnapshot>();
        foreach (Match match in RegistrationPattern.Matches(source))
        {
            parsedSpans.Add((match.Index, match.Length));
            var genericArguments = match.Groups["generic"].Value
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var contract = SimpleTypeName(genericArguments[0]);
            if (!durableContracts.Contains(contract))
                continue;

            var implementation = genericArguments.Length switch
            {
                2 => SimpleTypeName(genericArguments[1]),
                1 => SimpleTypeName(FactoryImplementationPattern.Match(match.Groups["factory"].Value).Groups["implementation"].Value),
                _ => string.Empty
            };
            if (string.IsNullOrWhiteSpace(implementation))
                throw UnparseableRegistration(file, contract);
            if (implementation == contract)
                continue;

            if (!RegistrationStorageUnits.TryGetValue(contract, out var storageUnits))
            {
                throw new InvalidOperationException(
                    $"Groundwork durable registration '{contract}' in '{RelativePath(file)}' has no explicit storage-unit mapping; inventory scanning fails closed.");
            }

            registrations.Add(new GroundworkPersistenceRegistrationSnapshot(
                owner,
                contract,
                implementation,
                storageUnits));
        }

        var unparsedSource = source.ToCharArray();
        foreach (var (index, length) in parsedSpans)
            Array.Fill(unparsedSource, ' ', index, length);

        var typeAliases = TypeAliasPattern.Matches(source)
            .ToDictionary(
                match => match.Groups["variable"].Value,
                match => SimpleTypeName(match.Groups["type"].Value),
                StringComparer.Ordinal);
        foreach (Match descriptor in ServiceDescriptorInvocationPattern.Matches(source))
        {
            var durableContract = FindDurableContract(descriptor.Value, durableContracts)
                                  ?? typeAliases
                                      .Where(alias => durableContracts.Contains(alias.Value) &&
                                                      Regex.IsMatch(
                                                          descriptor.Groups["arguments"].Value,
                                                          $@"(?<![A-Za-z0-9_]){Regex.Escape(alias.Key)}(?![A-Za-z0-9_])",
                                                          RegexOptions.CultureInvariant))
                                      .Select(alias => alias.Value)
                                      .FirstOrDefault();
            if (durableContract is not null)
                throw UnparseableRegistration(file, durableContract);
        }

        foreach (Match candidate in RegistrationLikePattern.Matches(new string(unparsedSource)))
        {
            var durableContract = FindDurableContract(candidate.Value, durableContracts);
            if (durableContract is not null)
                throw UnparseableRegistration(file, durableContract);
        }

        return registrations;
    }

    private static string SimpleTypeName(string typeName)
    {
        var normalized = typeName.Trim();
        if (normalized.StartsWith("global::", StringComparison.Ordinal))
            normalized = normalized["global::".Length..];

        var separator = normalized.LastIndexOf('.');
        return separator >= 0 ? normalized[(separator + 1)..] : normalized;
    }

    private static string? FindDurableContract(string source, IEnumerable<string> durableContracts) =>
        durableContracts.FirstOrDefault(contract => Regex.IsMatch(
            source,
            $@"(?<![A-Za-z0-9_]){Regex.Escape(contract)}(?![A-Za-z0-9_])",
            RegexOptions.CultureInvariant));

    private InvalidOperationException UnparseableRegistration(string file, string contract) =>
        new(
            $"Groundwork durable registration for '{contract}' in '{RelativePath(file)}' could not be parsed safely; inventory scanning fails closed.");

    private InvalidOperationException UnparseableManifestDeclaration(string file, string storageUnit) =>
        new(
            $"Groundwork manifest declaration for '{storageUnit}' in '{RelativePath(file)}' could not be parsed safely; inventory scanning fails closed.");

    private IEnumerable<string> SourceFiles(string relativePath)
    {
        var path = RequiredPath(relativePath);
        return Directory.Exists(path)
            ? EnumerateSourceFiles(path)
            : [path];
    }

    private IEnumerable<string> GroundworkSourceFiles(string relativePath) =>
        EnumerateSourceFiles(RequiredDirectory(relativePath));

    private static IEnumerable<string> EnumerateSourceFiles(string directory) =>
        Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !HasGeneratedPathSegment(file))
            .Order(StringComparer.Ordinal);

    private static bool HasGeneratedPathSegment(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj");

    private string RequiredDirectory(string relativePath)
    {
        var path = RequiredPath(relativePath);
        if (!Directory.Exists(path))
            throw new InvalidOperationException($"Groundwork persistence inventory directory '{relativePath}' is missing.");
        return path;
    }

    private string RequiredPath(string relativePath)
    {
        var path = Path.Combine(_repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path) && !Directory.Exists(path))
            throw new InvalidOperationException($"Groundwork persistence inventory path '{relativePath}' is missing.");
        return path;
    }

    private string RelativePath(string path) => Path.GetRelativePath(_repositoryRoot, path).Replace('\\', '/');

    private sealed record GroundworkFamilySource(string Owner, string Root);
}
