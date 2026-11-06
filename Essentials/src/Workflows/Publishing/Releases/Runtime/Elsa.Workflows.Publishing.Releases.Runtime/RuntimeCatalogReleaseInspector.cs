using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Releases.Runtime;

/// <summary>Portable public-contract schema and source-owned CLR descriptor data; no Design/compiler or class loading.</summary>
internal sealed class RuntimeCatalogReleaseInspector(IReleaseRuntimeSiteReader reader) : IReleaseOccurrenceInspector
{
    private const string ClrProvider = "elsa.clr-activity";
    public ReleaseSemanticAdapterIdentity Identity { get; } = new("elsa.runtime-catalog-release-profile", "1");
    public IReadOnlyCollection<ReleaseSiteKey> SupportedSites { get; } = Array.AsReadOnly(new[]
    {
        new ReleaseSiteKey(ReleaseRuntimeSiteKinds.PublicContract, "elsa.activity-contract", 1),
        new ReleaseSiteKey(ReleaseRuntimeSiteKinds.Provider, ClrProvider, 1)
    });

    public ValueTask<ReleaseOccurrenceFacts> InspectAsync(ReleaseSemanticSite site, IReleaseContentReader content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!SupportedSites.Contains(site.Site)) throw Refuse("catalog_site_unsupported");
            Fields(site.Payload, "versionId");
            if (site.Node != "$catalog" || site.Input is not null || site.Origin.Count != 0 || Text(site.Payload, "versionId") != site.Member.Identity)
                throw Refuse("invalid_catalog_site");
            var version = reader.ReadActivityVersion(site.Member, content);
            var template = reader.ReadTemplate(new(ReleaseRuntimeCodecs.TemplateKind, version.TemplateId), content);
            if (template.Scope != version.Scope || template.Template.TemplateHash != version.TemplateHash || template.Template.ProviderFingerprint != version.CompilerFingerprint)
                throw Refuse("catalog_template_association_mismatch");
            return ValueTask.FromResult(site.Site.SiteKind == ReleaseRuntimeSiteKinds.Provider ? Provider(version, template.Template) : Contract(version, template.Template));
        }
        catch (OperationCanceledException) { throw; }
        catch (ReleaseRuntimeArtifactException exception)
        { return ValueTask.FromResult(Failed(exception.Code, exception.Code.Contains("unsupported", StringComparison.Ordinal) ? ReleaseSemanticStatus.Unsupported : ReleaseSemanticStatus.Invalid)); }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        { return ValueTask.FromResult(Failed("invalid_catalog_captured_content", ReleaseSemanticStatus.Invalid)); }

        ReleaseOccurrenceFacts Failed(string code, ReleaseSemanticStatus status) => new(status, diagnostics: [new(code, site.Member, site.Origin, site.Input)]);
    }

    private static ReleaseOccurrenceFacts Provider(ReleaseActivityVersion version, ExecutableActivityTemplate template)
    {
        if (version.ProviderKey != ClrProvider || version.ProviderSchemaVersion != "1" || version.ResolutionKind != "AuthorableActivity")
            throw Refuse("clr_provider_schema_unsupported");
        Fields(version.ProviderPayload, "typeAlias"); var alias = Text(version.ProviderPayload, "typeAlias");
        var root = template.Root;
        if (root.Descriptor.ConsumerKey != WellKnownRuntimeActivityConsumers.ClrActivity || root.Descriptor.SchemaVersion != "1" ||
            !JsonElement.DeepEquals(version.ProviderPayload, root.Descriptor.Payload) || template.NodesById.Count != 1 ||
            root.ChildSlots.Count != 0 || root.Structure is not null || template.DirectDependencies.Count != 0 || template.ClosedTemplates.Count != 0 ||
            root.ActivityContract?.Activation.Capability != alias)
            throw Refuse("clr_provider_template_mismatch");
        // Literal reuse of the existing source-owned producer's fingerprint recipe, not a new hash algorithm.
        var fingerprint = ActivityTemplateHasher.ComputeCanonicalValueHash(new
        {
            version.ProviderKey,
            version.ProviderSchemaVersion,
            ConsumerKey = root.Descriptor.ConsumerKey,
            ConsumerSchemaVersion = root.Descriptor.SchemaVersion,
            descriptor = version.ProviderPayload
        });
        if (fingerprint != version.CompilerFingerprint) throw Refuse("clr_provider_fingerprint_mismatch");
        return new(ReleaseSemanticStatus.Verified);
    }

    private static ReleaseOccurrenceFacts Contract(ReleaseActivityVersion version, ExecutableActivityTemplate template)
    {
        var contract = version.Contract;
        Fields(contract, "contractSchemaVersion", "inputs", "outputs", "outcomes");
        if (Text(contract, "contractSchemaVersion") != "1") throw Refuse("public_contract_schema_unsupported");
        var inputs = Items(contract, "inputs"); var outputs = Items(contract, "outputs"); var outcomes = Items(contract, "outcomes");
        Unique(inputs, "referenceKey"); Unique(outputs, "referenceKey"); Unique(outcomes, "referenceKey");
        var runtime = template.Root.ActivityContract ?? throw Refuse("public_contract_template_unsupported");
        if (runtime.ActivityTypeKey != version.ActivityTypeKey || runtime.Inputs.Count != inputs.Length || runtime.Result.Projections.Count != outputs.Length)
            throw Refuse("public_contract_template_mismatch");
        var drivers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            Fields(input, "referenceKey", "name", "type", "isRequired", "isNullable", "default", "storageDriverKey", "durability", "displayName", "description", "category", "order", "uiHint", "uiSpecifications");
            var key = Text(input, "referenceKey"); var type = Type(input.GetProperty("type")); Presentation(input); Durability(input); drivers.Add(Text(input, "storageDriverKey"));
            if (!runtime.Inputs.TryGetValue(key, out var actual) || actual.Name != Text(input, "name") || actual.Type != type ||
                actual.IsRequired != Boolean(input, "isRequired") || actual.IsNullable != Boolean(input, "isNullable")) throw Refuse("public_input_template_mismatch");
            var hasDefault = input.TryGetProperty("default", out var value) && value.ValueKind != JsonValueKind.Null;
            if (hasDefault)
            {
                Fields(value, "syntax", "value");
                if (!string.Equals(Text(value, "syntax"), "Literal", StringComparison.OrdinalIgnoreCase)) throw Refuse("public_default_syntax_unsupported");
                if (type.Alias == typeof(ConnectionReference).FullName) throw Refuse("public_default_resource_unsupported");
                var literal = value.GetProperty("value");
                if (literal.ValueKind == JsonValueKind.Null && !actual.IsNullable) throw Refuse("public_default_null_mismatch");
                if (!actual.HasDefault || !actual.DefaultValue.HasValue || !JsonElement.DeepEquals(literal, actual.DefaultValue.Value)) throw Refuse("public_default_template_mismatch");
            }
            else if (actual.HasDefault) throw Refuse("public_default_template_mismatch");
        }
        foreach (var output in outputs)
        {
            Fields(output, "referenceKey", "name", "type", "isRequired", "isNullable", "storageDriverKey", "durability", "displayName", "description", "category", "order", "uiHint", "uiSpecifications", "sourceRepresentation");
            var key = Text(output, "referenceKey"); var type = Type(output.GetProperty("type")); Presentation(output); Durability(output); drivers.Add(Text(output, "storageDriverKey"));
            _ = Text(output, "name"); _ = Boolean(output, "isNullable");
            if (!runtime.Result.Projections.TryGetValue(key, out var actual) || actual.Type != type || actual.IsRequired != Boolean(output, "isRequired"))
                throw Refuse("public_output_template_mismatch");
            ValueRepresentation? representation = null;
            if (output.TryGetProperty("sourceRepresentation", out var source) && source.ValueKind != JsonValueKind.Null)
            {
                if (source.ValueKind != JsonValueKind.String || !Enum.TryParse<ValueRepresentation>(source.GetString(), out var parsed) || !Enum.IsDefined(parsed))
                    throw Refuse("public_output_representation_unsupported");
                representation = parsed;
            }
            if (actual.EffectiveSourceRepresentation != (representation ?? ValueRepresentationDefaults.Infer(type))) throw Refuse("public_output_representation_mismatch");
        }
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var outcome in outcomes)
        {
            Fields(outcome, "referenceKey", "name", "isEmitted", "description");
            var name = Text(outcome, "name"); OptionalText(outcome, "description");
            if (Boolean(outcome, "isEmitted") && !emitted.Add(name)) throw Refuse("duplicate_public_outcome_name");
        }
        if (!emitted.SetEquals(runtime.Outcomes)) throw Refuse("public_outcome_template_mismatch");
        var member = new ReleaseMemberKey(ReleaseRuntimeCodecs.ActivityVersionKind, version.VersionId);
        return new(ReleaseSemanticStatus.Verified, prerequisites: drivers.Order(StringComparer.Ordinal).Select(driver => new ReleaseSemanticPrerequisite(
            JsonSerializer.Serialize(new[] { member.Kind, member.Identity, "storage-driver", driver, "opaque", "required" }), member, "storage-driver", driver, "opaque", "required")).ToArray());
    }

    private static ValueTypeDescriptor Type(JsonElement value)
    {
        Fields(value, "alias", "collectionKind"); var alias = Text(value, "alias"); var collection = CollectionKind.Single;
        if (value.TryGetProperty("collectionKind", out var shape) && (shape.ValueKind != JsonValueKind.String ||
            !Enum.TryParse(shape.GetString(), out collection) || !Enum.IsDefined(collection))) throw Refuse("public_type_shape_unsupported");
        return new(alias, collection);
    }
    private static void Durability(JsonElement value)
    {
        if (value.TryGetProperty("durability", out var durability) && !(durability.ValueKind == JsonValueKind.Number && durability.TryGetInt32(out var number) && number == 0 ||
            durability.ValueKind == JsonValueKind.String && durability.GetString() == "Required")) throw Refuse("public_durability_unsupported");
    }
    private static void Presentation(JsonElement value)
    {
        foreach (var field in new[] { "displayName", "description", "category", "uiHint" }) OptionalText(value, field);
        if (value.TryGetProperty("order", out var order) && (!order.TryGetSingle(out var number) || !float.IsFinite(number))) throw Refuse("invalid_public_order");
        // UI specifications remain bounded opaque descriptive data and are retained verbatim in the original member.
    }
    private static void OptionalText(JsonElement value, string name)
    { if (value.TryGetProperty(name, out var text) && text.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) throw Refuse("invalid_public_presentation"); }
    private static JsonElement[] Items(JsonElement value, string name)
    {
        var array = value.GetProperty(name);
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 10_000) throw Refuse("public_contract_member_limit");
        return array.EnumerateArray().ToArray();
    }
    private static void Unique(IEnumerable<JsonElement> values, string name)
    { var keys = new HashSet<string>(StringComparer.Ordinal); foreach (var value in values) if (!keys.Add(Text(value, name))) throw Refuse("duplicate_public_reference_key"); }
    private static string Text(JsonElement value, string name) => value.GetProperty(name) is { ValueKind: JsonValueKind.String } text && !string.IsNullOrWhiteSpace(text.GetString())
        ? text.GetString()! : throw Refuse("invalid_public_contract_text");
    private static bool Boolean(JsonElement value, string name) => value.GetProperty(name).ValueKind switch
    { JsonValueKind.True => true, JsonValueKind.False => false, _ => throw Refuse("invalid_public_contract_boolean") };
    private static void Fields(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Refuse("invalid_public_contract_object");
        foreach (var property in value.EnumerateObject()) if (!names.Contains(property.Name, StringComparer.Ordinal)) throw Refuse("public_contract_field_unsupported");
    }
    private static ReleaseRuntimeArtifactException Refuse(string code) => ReleaseRuntimeArtifactCodec.Refuse(code);
}
