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
using Elsa.Expressions.Core.Models;
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
        // KNOWN DUPLICATION (accepted, tracked): this restates the runtime attribution rule of
        // Elsa.Activities.Design.Api's ActivityFeatureAttributionResolver — activity type → declaring
        // assembly → the feature whose startup type lives in it, ties broken by min-ordinal feature id.
        // The resolver itself cannot be reused here: it depends on runtime services (IWellKnownTypeRegistry,
        // CShells' IRuntimeFeatureCatalog) that no build-time projection can supply. Unlike the ports facet
        // convention (unified in ActivityPortDesignFacetReader), this is a two-line rule rather than a parser,
        // so §2.17 "duplication beats dependency" applies — but the two MUST change together.
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
            StripBuildMetadata(model.Version),
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

    /// <summary>
    /// SemVer build metadata carries the SourceLink commit sha (<c>1.0.0+&lt;sha&gt;</c>): activity identity
    /// is build-metadata-insensitive by design (constitution §E2.8 — the SemVer sort key excludes it), and
    /// a committed fragment must not change on every commit, so the published version strips it.
    /// </summary>
    public static string StripBuildMetadata(string version)
    {
        var plus = version.IndexOf('+');
        return plus < 0 ? version : version[..plus];
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
        input.UISpecifications,
        input.EnumValues);

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

    // The ports-facet convention is parsed by the product reader the served catalog also uses
    // (ActivityPortDesignFacetReader) — never re-implemented here (one-projection rule).
    private static IEnumerable<PortContract> ToPorts(ActivityDesignFacet facet) =>
        ActivityPortDesignFacetReader.Read(facet)
            .Select(port => new PortContract(
                port.Name,
                port.DisplayName,
                port.Type,
                port.IsBrowsable,
                port.ReferenceKey));

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
        using var initializerReader = new InitializerDefaultReader();
        var features = new List<FeatureContract>();
        var candidates = TargetAssembly.GetLoadableTypes(assembly)
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Select(type => (Type: type, Attribute: type.GetCustomAttributesData()
                .FirstOrDefault(candidate => candidate.AttributeType.Name == ShellFeatureAttributeName)))
            .Where(candidate => candidate.Attribute is not null);

        foreach (var (type, attribute) in candidates)
        {
            var id = attribute!.ConstructorArguments.Count > 0 ? attribute.ConstructorArguments[0].Value as string : null;
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
                    // NOT setting.DefaultValue: ManifestHintReader reads defaults off an activated
                    // instance, which embeds the generator's environment (current directory, machine
                    // limits, ...) and breaks fragment determinism. Contracts publish static defaults only.
                    ResolveStaticOptionDefault(type, setting.Name, initializerReader),
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

    private const string ManifestSettingAttributeFullName = "Elsa.Platform.PackageManifest.Generator.Hints.ManifestSettingAttribute";

    /// <summary>
    /// The G1 static-default ladder applied to feature options: explicit <c>[ManifestSetting(DefaultValue)]</c>
    /// → compiled property-initializer constant (via <see cref="InitializerDefaultReader"/>) → synthesizable
    /// <c>default(T)</c> → null (no published default). Never an instance read: a default like
    /// <c>Path.Combine(Environment.CurrentDirectory, ...)</c> is honest runtime behavior but not static contract.
    /// Enum values use the raw member name, matching the options list ManifestHintReader publishes.
    /// </summary>
    private static JsonElement? ResolveStaticOptionDefault(Type featureType, string propertyName, InitializerDefaultReader initializerReader)
    {
        var property = featureType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property is null)
            return null;

        var explicitDefault = property.GetCustomAttributesData()
            .FirstOrDefault(attribute => attribute.AttributeType.FullName == ManifestSettingAttributeFullName)
            ?.NamedArguments.FirstOrDefault(argument => argument.MemberName == "DefaultValue").TypedValue.Value as string;
        var underlying = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (!string.IsNullOrWhiteSpace(explicitDefault))
            return ParseExplicitOptionDefault(explicitDefault, underlying);

        var initializer = initializerReader.GetInitializer(property.DeclaringType ?? featureType, property.Name);
        return initializer.Kind switch
        {
            InitializerKind.Constant when initializer.ViaNewobj && Nullable.GetUnderlyingType(property.PropertyType) is null => null,
            InitializerKind.Constant => SerializeOptionConstant(initializer.Value, underlying),
            InitializerKind.Unrecognized => null,
            _ when property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) is null =>
                SynthesizeOptionDefault(underlying),
            _ => null
        };
    }

    private static JsonElement? ParseExplicitOptionDefault(string value, Type underlying)
    {
        // Mirrors ManifestHintReader.ParseDefault's jsonType-driven parsing.
        if (underlying == typeof(bool) && bool.TryParse(value, out var boolValue))
            return JsonSerializer.SerializeToElement(boolValue);
        if (IsIntegerType(underlying) && long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var longValue))
            return JsonSerializer.SerializeToElement(longValue);
        if (IsFloatingType(underlying) && double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var doubleValue))
            return JsonSerializer.SerializeToElement(doubleValue);
        return JsonSerializer.SerializeToElement(value);
    }

    private static JsonElement? SerializeOptionConstant(object? value, Type underlying)
    {
        if (value is null)
            return null;

        if (underlying.IsEnum)
        {
            var name = Enum.GetName(underlying, Enum.ToObject(underlying, value));
            return name is null ? null : JsonSerializer.SerializeToElement(name);
        }

        if (underlying == typeof(bool) && value is int boolBits)
            return JsonSerializer.SerializeToElement(boolBits != 0);
        if (underlying == typeof(string) && value is string stringValue)
            return JsonSerializer.SerializeToElement(stringValue);
        if (IsIntegerType(underlying) || IsFloatingType(underlying))
        {
            try
            {
                return JsonSerializer.SerializeToElement(Convert.ChangeType(value, underlying, System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
            {
                return null;
            }
        }

        return null;
    }

    private static JsonElement? SynthesizeOptionDefault(Type underlying)
    {
        if (underlying.IsEnum)
        {
            var zero = Enum.GetName(underlying, Enum.ToObject(underlying, 0));
            return zero is null ? null : JsonSerializer.SerializeToElement(zero);
        }

        if (underlying == typeof(bool))
            return JsonSerializer.SerializeToElement(false);
        if (IsIntegerType(underlying) || IsFloatingType(underlying))
            return JsonSerializer.SerializeToElement(Convert.ChangeType(0, underlying, System.Globalization.CultureInfo.InvariantCulture));

        // Structs like TimeSpan/Guid cannot be synthesized statically — no published default.
        return null;
    }

    private static bool IsIntegerType(Type type) =>
        type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) ||
        type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);

    private static bool IsFloatingType(Type type) =>
        type == typeof(float) || type == typeof(double) || type == typeof(decimal);

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

        // The always-available expression types (Literal, Object, Input) are not provider-registered —
        // they are intrinsic to the authoring surface. Publish them from the assembly that declares them
        // so a contracts-only consumer can discover Literal, which nearly every authored input uses.
        if (string.Equals(assemblyName, typeof(IntrinsicExpressionDescriptors).Assembly.GetName().Name, StringComparison.Ordinal))
        {
            descriptors.AddRange(IntrinsicExpressionDescriptors.All.Select(descriptor => new ExpressionDescriptorContract(
                // Deliberately null: intrinsic expression types are not gated by any feature — they are
                // available wherever the expressions system is present.
                FeatureId: null,
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
                    // The variable target is carried by the node's `intrinsic` block, not by `inputs`
                    // (AuthoredWorkflowIntrinsic exposes Kind/ValueType/Variable only), so it is marked
                    // rather than published as if it were an argument binding.
                    descriptor.Inputs.Select(input => ToInputContract(input, descriptor.Intrinsic.VariableInputKey)).ToArray(),
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

    private static InputContract ToInputContract(ActivityInputDescriptorView view, string? intrinsicBlockInputKey = null) => new(
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
        view.UiSpecifications,
        view.EnumValues,
        string.Equals(view.ReferenceKey, intrinsicBlockInputKey, StringComparison.Ordinal)
            ? InputAuthoringSites.IntrinsicBlock
            : InputAuthoringSites.Argument);

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
                var dependencyTypes = FeatureIndex.ReadDependsOn(type)
                    .Select(dependencyId => featureIndex.TryGetFeatureType(dependencyId, out var dependencyType) ? dependencyType : null)
                    .Where(dependencyType => dependencyType is not null);
                foreach (var dependencyType in dependencyTypes)
                    ComposeFeature(dependencyType!, services, featureIndex, composed, depth + 1);
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
                    // One instance per contributing type: a feature may register the same contract more
                    // than once, but the contract describes the type's contribution, not its registrations.
                    var instances = provider.GetServices<TContract>()
                        .Where(candidate => candidate.GetType().Assembly == assembly)
                        .DistinctBy(candidate => candidate.GetType());

                    foreach (var instance in instances)
                    {
                        resolvedTypes.Add(instance.GetType());
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
            var unregistered = TargetAssembly.GetLoadableTypes(assembly)
                .Where(type => type is { IsClass: true, IsAbstract: false } &&
                               typeof(TContract).IsAssignableFrom(type) &&
                               !resolvedTypes.Contains(type));
            foreach (var type in unregistered)
            {
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
