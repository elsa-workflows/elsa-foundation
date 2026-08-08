using System.Reflection;
using System.Text.Json;
using CShells.Features;
using Elsa.Activities.Design.Api.Contracts;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Activities.Design.Reconciliation.Clr.Services;
using Elsa.Activities.Design.Reconciliation.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Expressions.JavaScript.Rendering.Core.Contracts;
using Elsa.Expressions.JavaScript.Rendering.Core.Models;
using Elsa.Modularity.Nuplane.Services;
using Elsa.Primitives.Identity;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Contracts.Generator;

/// <summary>
/// Projects one built assembly into its <see cref="ContractFragment"/> by composing the product
/// projection pipeline (one-projection rule, spec 149 / RFC #1191): activities through
/// <see cref="ClrAssemblyScanner"/> (the same code CLR reconciliation persists from), structure kinds
/// through the assembly's own <see cref="IActivityStructureHandler"/> instances with payload schemas from
/// <see cref="AuthoringSchemaExporter"/>, feature options through <see cref="ManifestHintReader"/>,
/// intrinsics through the assembly's <see cref="IBuiltInAuthoringDescriptorProvider"/> instances, and
/// activity content hashes through the same factories the reconciler uses.
/// </summary>
public sealed class FragmentProjector(Diagnostics diagnostics, FeatureIndex? featureIndex = null)
{
    private const string ShellFeatureAttributeName = "ShellFeatureAttribute";

    public ContractFragment Project(string assemblyPath, IReadOnlyCollection<string> referencePaths)
    {
        var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);
        var assembly = TargetAssembly.LoadForExecution(assemblyPath);
        WarnOnTypeLoadDrops(assembly, assemblyPath);

        var features = ProjectFeatures(assembly, assemblyPath);
        // The runtime attribution rule (ActivityFeatureAttributionResolver): activity type → declaring
        // assembly → the feature whose startup type lives in it, ties broken by min-ordinal feature id.
        var owningFeatureId = features.Count > 0
            ? features.Select(feature => feature.Id).OrderBy(id => id, StringComparer.Ordinal).First()
            : null;

        // Contribution instances come from the assembly's OWN features — plus their DependsOn closure when
        // a feature index is available — composed into a service provider: the same registration path the
        // runtime resolves them from (one-projection); parameterless direct instantiation is the fallback
        // for types its features did not register.
        using var contributions = new ContributionResolver(assembly, assemblyPath, diagnostics, featureIndex);

        var activities = ProjectActivities(assemblyPath, referencePaths, assemblyName, owningFeatureId);
        var structures = ProjectStructures(contributions, owningFeatureId);
        var expressions = ProjectExpressions(contributions, assemblyName, owningFeatureId);
        var intrinsics = ProjectIntrinsics(contributions, owningFeatureId);

        return new ContractFragment(
            ContractFragment.CurrentSchemaVersion,
            assemblyName,
            features,
            activities,
            structures,
            expressions,
            intrinsics);
    }

    private IReadOnlyList<ActivityContract> ProjectActivities(
        string assemblyPath,
        IReadOnlyCollection<string> referencePaths,
        string assemblyName,
        string? owningFeatureId)
    {
        var scanner = new ClrAssemblyScanner(
            new ActivityTypeVersionResolver(),
            new ActivityTypeCategoryResolver(),
            NullLogger<ClrAssemblyScanner>.Instance);

        var models = scanner.ScanAssembly(assemblyPath, referencePaths);
        return models
            .Select(model => ToActivityContract(model, assemblyName, owningFeatureId))
            .OrderBy(activity => activity.ActivityTypeKey, StringComparer.Ordinal)
            .ThenBy(activity => activity.Version, StringComparer.Ordinal)
            .ToArray();
    }

    private static ActivityContract ToActivityContract(
        ActivityVersionReconciliationModel model,
        string assemblyName,
        string? owningFeatureId)
    {
        // Same hash pipeline as reconciliation (DefaultActivityDefinitionHasher via the version factory);
        // the fixed ids are excluded from the canonical form, so the hash equals the persisted row's.
        var identityGenerator = new GuidIdentityGenerator();
        var definition = new ActivityDefinitionFactory(identityGenerator).Create(
            model.ActivityTypeKey,
            model.Category ?? string.Empty,
            model.DisplayName,
            model.Description,
            "contract");
        var descriptorPayload = JsonSerializer.SerializeToElement(model.Descriptor, model.Descriptor.GetType());
        var version = new ActivityDefinitionVersionFactory(identityGenerator, new DefaultActivityDefinitionHasher()).Create(
            definition,
            model.Version,
            model.ProviderKey,
            model.ProviderSchemaVersion,
            model.ConsumerKey,
            model.ConsumerSchemaVersion,
            descriptorPayload,
            "CLR",
            assemblyName,
            model.Inputs,
            model.Outputs,
            model.DesignFacets,
            model.ExecutionType,
            "contract");

        ActivityStructureDesignFacetReader.TryReadSingle(model.DesignFacets, out var structureFacet);

        return new ActivityContract(
            owningFeatureId,
            model.ActivityTypeKey,
            model.Version,
            version.Hash ?? throw new InvalidOperationException($"No content hash was produced for '{model.ActivityTypeKey}'."),
            model.DisplayName ?? model.ActivityTypeKey,
            model.Category,
            model.Description,
            model.ExecutionType.ToString(),
            model.Inputs.Select(ToInputContract).ToArray(),
            model.Outputs.Select(ToOutputContract).ToArray(),
            model.DesignFacets.SelectMany(ToPorts).ToArray(),
            structureFacet?.Payload.Clone());
    }

    private static InputContract ToInputContract(InputDefinition input) => new(
        input.ReferenceKey,
        input.Name,
        input.Type.Alias,
        input.Type.CollectionKind.ToString(),
        input.DisplayName,
        input.Description,
        input.Order,
        input.Category,
        input.IsBrowsable ?? true,
        input.IsRequired,
        input.IsNullable,
        input.UiHint,
        input.DefaultValue,
        input.HasStaticDefault || input.DefaultValue is not null,
        input.DefaultSyntax,
        input.UISpecifications);

    private static OutputContract ToOutputContract(OutputDefinition output) => new(
        output.ReferenceKey,
        output.Name,
        output.Type.Alias,
        output.Type.CollectionKind.ToString(),
        output.DisplayName,
        output.Description,
        output.Category,
        output.IsBrowsable ?? true,
        output.IsRequired);

    // Same ports-facet convention the catalog handler reads (ListActivityAuthoringCatalogRequestHandler.ToPorts).
    private static IEnumerable<PortContract> ToPorts(ActivityDesignFacet facet)
    {
        if (facet.Payload.ValueKind != JsonValueKind.Object ||
            !facet.Payload.TryGetProperty("ports", out var ports) ||
            ports.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var port in ports.EnumerateArray())
        {
            if (port.ValueKind != JsonValueKind.Object ||
                !port.TryGetProperty("name", out var nameProperty) ||
                string.IsNullOrWhiteSpace(nameProperty.GetString()))
                continue;

            var name = nameProperty.GetString()!;
            yield return new PortContract(
                name,
                ReadString(port, "displayName") ?? name,
                ReadString(port, "type"),
                ReadBoolean(port, "isBrowsable") ?? true,
                ReadString(port, "referenceKey") ?? name);
        }
    }

    /// <summary>
    /// A type that fails to load is invisible to every projection below — which would silently drop a
    /// feature or contributor from the contract. Surface every distinct loader failure as a warning so
    /// the omission is a visible choice, never a silent one.
    /// </summary>
    private void WarnOnTypeLoadDrops(Assembly assembly, string assemblyPath)
    {
        try
        {
            assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            foreach (var message in exception.LoaderExceptions
                         .Where(loader => loader is not null)
                         .Select(loader => loader!.Message)
                         .Distinct(StringComparer.Ordinal)
                         .Order(StringComparer.Ordinal))
            {
                diagnostics.Warning(assemblyPath, "ELSACT009",
                    $"Some types could not be loaded and are absent from this fragment: {message}");
            }
        }
    }

    private IReadOnlyList<FeatureContract> ProjectFeatures(Assembly assembly, string assemblyPath)
    {
        var features = new List<FeatureContract>();
        foreach (var type in TargetAssembly.GetLoadableTypes(assembly))
        {
            if (type is not { IsClass: true, IsAbstract: false })
                continue;

            var attribute = type.GetCustomAttributesData()
                .FirstOrDefault(candidate => candidate.AttributeType.Name == ShellFeatureAttributeName);
            if (attribute is null)
                continue;

            var id = attribute.ConstructorArguments.Count > 0 ? attribute.ConstructorArguments[0].Value as string : null;
            if (string.IsNullOrWhiteSpace(id))
            {
                diagnostics.Error(assemblyPath, "ELSACT003", $"Feature class '{type.FullName}' declares a [ShellFeature] without a name.");
                continue;
            }

            var dependsOn = FeatureIndex.ReadDependsOn(type)
                .OrderBy(entry => entry, StringComparer.Ordinal)
                .ToArray();
            var options = ManifestHintReader.Read(type).Settings
                .Select(setting => new FeatureOptionContract(
                    setting.Name,
                    setting.DisplayName,
                    setting.Description,
                    setting.Category,
                    setting.ClrType,
                    setting.JsonType ?? "string",
                    setting.Required,
                    setting.DefaultValue,
                    setting.Secret,
                    setting.RestartRequired,
                    setting.Advanced,
                    setting.Experimental))
                .OrderBy(option => option.Name, StringComparer.Ordinal)
                .ToArray();

            features.Add(new FeatureContract(
                id,
                ReadNamedString(attribute, "DisplayName"),
                ReadNamedString(attribute, "Description"),
                dependsOn,
                options));
        }

        return features.OrderBy(feature => feature.Id, StringComparer.Ordinal).ToArray();
    }

    private IReadOnlyList<StructureContract> ProjectStructures(ContributionResolver contributions, string? owningFeatureId)
    {
        return contributions.Resolve<IActivityStructureHandler>("structure handler")
            .Select(handler => new StructureContract(
                owningFeatureId,
                handler.Kind,
                handler.SchemaVersion,
                handler.SupportsScopedVariables,
                handler.AuthoredPayloadType is { } payloadType
                    ? AuthoringSchemaExporter.ExportSchema(payloadType)
                    : null))
            .OrderBy(structure => structure.Kind, StringComparer.Ordinal)
            .ThenBy(structure => structure.SchemaVersion, StringComparer.Ordinal)
            .ToArray();
    }

    private ExpressionSurface? ProjectExpressions(ContributionResolver contributions, string assemblyName, string? owningFeatureId)
    {
        var descriptors = new List<ExpressionDescriptorContract>();
        var declarations = new List<JsDeclarationContract>();

        foreach (var provider in contributions.Resolve<IExpressionDescriptorProvider>("expression descriptor provider"))
        {
            descriptors.AddRange(provider.GetDescriptors().Select(descriptor => new ExpressionDescriptorContract(
                owningFeatureId,
                descriptor.TypeName,
                descriptor.DisplayName,
                descriptor.EditingMode.ToString())));
        }

        foreach (var contributor in contributions.Resolve<IJavaScriptDeclarationContributor>("JavaScript declaration contributor"))
        {
            var capture = new CapturingDeclarationsContext();
            contributor.Contribute(capture, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            declarations.Add(new JsDeclarationContract(
                owningFeatureId,
                contributor.GetType().FullName ?? contributor.GetType().Name,
                capture.Variables.Select(Serialize).ToArray(),
                capture.Types.Select(Serialize).ToArray(),
                capture.Functions.Select(Serialize).ToArray()));
        }

        // The Jint sandbox surface is declared data in the Jint package (pinned to the live engine by
        // SandboxSurfaceCatalogTests); it belongs to that assembly's fragment.
        var sandbox = string.Equals(assemblyName, typeof(SandboxSurfaceCatalog).Assembly.GetName().Name, StringComparison.Ordinal)
            ? SandboxSurfaceCatalog.Globals
                .Select(entry => new SandboxGlobalContract(entry.Name, entry.Kind, entry.Signature, entry.Availability))
                .ToArray()
            : [];

        if (descriptors.Count == 0 && declarations.Count == 0 && sandbox.Length == 0)
            return null;

        return new ExpressionSurface(
            descriptors.OrderBy(descriptor => descriptor.Type, StringComparer.Ordinal).ToArray(),
            declarations.OrderBy(declaration => declaration.Contributor, StringComparer.Ordinal).ToArray(),
            sandbox);
    }

    private static JsonElement Serialize<T>(T value) =>
        JsonSerializer.SerializeToElement(value, DeterministicJson.WireOptions);

    private IReadOnlyList<IntrinsicContract> ProjectIntrinsics(ContributionResolver contributions, string? owningFeatureId)
    {
        var intrinsics = new List<IntrinsicContract>();
        foreach (var provider in contributions.Resolve<IBuiltInAuthoringDescriptorProvider>("built-in authoring descriptor provider"))
        {
            foreach (var descriptor in provider.GetDescriptors())
            {
                if (descriptor.Intrinsic is null)
                {
                    diagnostics.Error(contributions.AssemblyPath, "ELSACT005",
                        $"Built-in descriptor '{descriptor.ActivityTypeKey}' from '{provider.GetType().FullName}' carries no intrinsic mapping.");
                    continue;
                }

                intrinsics.Add(new IntrinsicContract(
                    owningFeatureId,
                    descriptor.ActivityVersionId,
                    descriptor.ActivityTypeKey,
                    descriptor.Version,
                    descriptor.DisplayName,
                    descriptor.Category,
                    descriptor.Description,
                    descriptor.ExecutionType,
                    descriptor.Inputs.Select(ToInputContract).ToArray(),
                    descriptor.Outputs.Select(ToOutputContract).ToArray(),
                    descriptor.Ports.Select(port => new PortContract(
                        port.Name, port.DisplayName, port.Type, port.IsBrowsable, port.ReferenceKey ?? port.Name)).ToArray(),
                    new IntrinsicMapping(
                        descriptor.Intrinsic.Kind,
                        descriptor.Intrinsic.ValueInputKey,
                        descriptor.Intrinsic.VariableInputKey,
                        descriptor.Intrinsic.OutputNameInputKey)));
            }
        }

        return intrinsics.OrderBy(intrinsic => intrinsic.DescriptorId, StringComparer.Ordinal).ToArray();
    }

    private static InputContract ToInputContract(ActivityInputDescriptorView view) => new(
        view.ReferenceKey,
        view.Name,
        view.Type,
        view.CollectionKind.ToString(),
        view.DisplayName,
        view.Description,
        view.Order,
        view.Category,
        view.IsBrowsable,
        view.IsRequired,
        view.IsNullable,
        view.UiHint,
        view.DefaultValue,
        view.HasStaticDefault,
        view.DefaultSyntax,
        view.UiSpecifications);

    private static OutputContract ToOutputContract(ActivityOutputDescriptorView view) => new(
        view.ReferenceKey ?? view.Name,
        view.Name,
        view.Type,
        view.CollectionKind.ToString(),
        view.DisplayName,
        view.Description,
        view.Category,
        view.IsBrowsable,
        view.IsRequired);

    /// <summary>
    /// Resolves the target assembly's contribution instances the way the runtime does: the assembly's own
    /// features are composed into a service collection (with the same minimal prerequisites the
    /// feature-registration tests use) and instances resolve from DI — so registration-time configuration
    /// (e.g. feature-injected declaration options) shapes the contract exactly as it shapes the runtime.
    /// Types the features did not register fall back to parameterless construction; a contribution type
    /// that cannot be materialized either way is a canonical error, never a silent omission.
    /// </summary>
    private sealed class ContributionResolver : IDisposable
    {
        private readonly Assembly assembly;
        private readonly Diagnostics diagnostics;
        private readonly ServiceProvider? provider;

        public string AssemblyPath { get; }

        public ContributionResolver(Assembly assembly, string assemblyPath, Diagnostics diagnostics, FeatureIndex? featureIndex)
        {
            this.assembly = assembly;
            this.diagnostics = diagnostics;
            AssemblyPath = assemblyPath;

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOptions();

            var composed = new HashSet<Type>();
            foreach (var type in TargetAssembly.GetLoadableTypes(assembly))
                ComposeFeature(type, services, featureIndex, composed, depth: 0);

            try
            {
                provider = services.BuildServiceProvider();
            }
            catch (Exception exception)
            {
                diagnostics.Warning(assemblyPath, "ELSACT006",
                    $"Feature service provider could not be built for contract projection: {exception.GetBaseException().Message}");
                provider = null;
            }
        }

        /// <summary>
        /// Registers a feature and — via the feature index — its transitive DependsOn features first,
        /// mirroring the shell's composition order so cross-assembly contract dependencies
        /// (e.g. a declaration contributor needing the rendering feature's factory) resolve exactly
        /// as they do at runtime.
        /// </summary>
        private void ComposeFeature(Type type, ServiceCollection services, FeatureIndex? featureIndex, HashSet<Type> composed, int depth)
        {
            if (depth > 16 ||
                type is not { IsClass: true, IsAbstract: false } ||
                !typeof(IShellFeature).IsAssignableFrom(type) ||
                type.GetConstructor(Type.EmptyTypes) is null ||
                !composed.Add(type))
                return;

            if (featureIndex is not null)
            {
                foreach (var dependencyId in FeatureIndex.ReadDependsOn(type))
                {
                    if (featureIndex.TryGetFeatureType(dependencyId, out var dependencyType))
                        ComposeFeature(dependencyType, services, featureIndex, composed, depth + 1);
                }
            }

            try
            {
                ((IShellFeature)Activator.CreateInstance(type)!).ConfigureServices(services);
            }
            catch (Exception exception)
            {
                diagnostics.Warning(AssemblyPath, "ELSACT006",
                    $"Feature '{type.FullName}' could not register services for contract projection: {exception.GetBaseException().Message}");
            }
        }

        public IReadOnlyList<TContract> Resolve<TContract>(string kind) where TContract : class
        {
            var resolved = new List<TContract>();
            var resolvedTypes = new HashSet<Type>();
            if (provider is not null)
            {
                try
                {
                    foreach (var instance in provider.GetServices<TContract>())
                    {
                        if (instance.GetType().Assembly != assembly)
                            continue;
                        if (resolvedTypes.Add(instance.GetType()))
                            resolved.Add(instance);
                    }
                }
                catch (Exception exception)
                {
                    diagnostics.Warning(AssemblyPath, "ELSACT006",
                        $"Resolving {kind}s from the assembly's features failed: {exception.GetBaseException().Message}");
                }
            }

            // Fallback: contribution types the assembly declares but its own features did not register
            // (e.g. registered by a sibling feature assembly at composition time).
            foreach (var type in TargetAssembly.GetLoadableTypes(assembly))
            {
                if (type is not { IsClass: true, IsAbstract: false } ||
                    !typeof(TContract).IsAssignableFrom(type) ||
                    resolvedTypes.Contains(type))
                    continue;

                if (type.GetConstructor(Type.EmptyTypes) is not null)
                {
                    try
                    {
                        resolved.Add((TContract)Activator.CreateInstance(type)!);
                        resolvedTypes.Add(type);
                        continue;
                    }
                    catch (Exception exception)
                    {
                        diagnostics.Error(AssemblyPath, "ELSACT004",
                            $"{kind} '{type.FullName}' could not be instantiated for contract projection: {exception.GetBaseException().Message}");
                        continue;
                    }
                }

                if (provider is not null && TryActivatorUtilities(type, out var viaUtilities))
                {
                    resolved.Add((TContract)viaUtilities!);
                    resolvedTypes.Add(type);
                    continue;
                }

                diagnostics.Error(AssemblyPath, "ELSACT004",
                    $"{kind} '{type.FullName}' is not registered by this assembly's features and has no parameterless constructor; its contribution cannot be projected.");
            }

            return resolved;
        }

        private bool TryActivatorUtilities(Type type, out object? instance)
        {
            try
            {
                instance = ActivatorUtilities.CreateInstance(provider!, type);
                return true;
            }
            catch (InvalidOperationException)
            {
                instance = null;
                return false;
            }
        }

        public void Dispose() => provider?.Dispose();
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool? ReadBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;

    private static string? ReadNamedString(CustomAttributeData attribute, string name) =>
        attribute.NamedArguments.FirstOrDefault(argument => argument.MemberName == name).TypedValue.Value as string;

    private sealed class CapturingDeclarationsContext : IJavaScriptDeclarationsContributionContext
    {
        public List<JavaScriptVariableDeclaration> Variables { get; } = [];
        public List<JavaScriptTypeDeclaration> Types { get; } = [];
        public List<JavaScriptFunctionDeclaration> Functions { get; } = [];

        public void AddVariable(JavaScriptVariableDeclaration declaration) => Variables.Add(declaration);
        public void AddType(JavaScriptTypeDeclaration declaration) => Types.Add(declaration);
        public void AddFunction(JavaScriptFunctionDeclaration declaration) => Functions.Add(declaration);
    }
}
