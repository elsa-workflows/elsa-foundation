using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Reconciliation.Clr.Contracts;
using Elsa.Activities.Design.Reconciliation.Core.Models;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Elsa.Activities.Design.Reconciliation.Clr.Services;

/// <summary>
/// Reflection-only scanner (R5) that reads activity-bearing assemblies from a folder via a
/// <see cref="MetadataLoadContext"/> and produces one <see cref="ActivityVersionReconciliationModel"/>
/// per discovered <c>IActivity</c> implementation. No type is loaded into the execution context, so
/// scanning never runs author code or pollutes the default <see cref="AssemblyLoadContext"/>.
/// </summary>
/// <remarks>
/// Resilient scan (FR-023): a DLL that exposes no activities is skipped silently; a DLL that fails
/// to load or reflect is logged and skipped; the pass never aborts wholesale. Per-activity faults —
/// an invalid <c>[Version]</c> or an unresolvable assembly version — still throw (the
/// <see cref="ActivityTypeVersionResolver"/> raises a domain exception) so a misconfigured activity is
/// loud rather than silently dropped.
/// </remarks>
public sealed class ClrAssemblyScanner(
    IActivityTypeVersionResolver versionResolver,
    IActivityTypeCategoryResolver categoryResolver,
    ILogger<ClrAssemblyScanner> logger) : IClrAssemblyScanner
{
    public const string ProviderKey = "elsa.clr-activity";
    public const string SchemaVersion = "1";
    private const long MaxJavaScriptSafeInteger = 9007199254740991L;
    private static readonly string ActivityInterfaceFullName = typeof(IActivity).FullName!;
    private static readonly string ActivityResultInterfaceFullName = typeof(IActivityResult<>).FullName!;
    private static readonly string RequiredAttributeFullName = typeof(RequiredAttribute).FullName!;
    private static readonly string RequiredMemberAttributeFullName = typeof(RequiredMemberAttribute).FullName!;
    private static readonly string ActivityInputAttributeFullName = typeof(ActivityInputAttribute).FullName!;
    private static readonly string OutputAttributeFullName = typeof(OutputAttribute).FullName!;
    private static readonly string ActivityInputOptionAttributeFullName = typeof(ActivityInputOptionAttribute).FullName!;
    private static readonly string ActivityStructureAttributeFullName = typeof(ActivityStructureAttribute).FullName!;
    private static readonly string ActivityChildSlotAttributeFullName = typeof(ActivityChildSlotAttribute).FullName!;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<ActivityVersionReconciliationModel> Scan(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return [];

        var folderDlls = Directory.EnumerateFiles(folderPath, "*.dll", SearchOption.TopDirectoryOnly).ToList();
        if (folderDlls.Count == 0)
            return [];

        using var context = new MetadataLoadContext(new PathAssemblyResolver(BuildResolverPaths(folderDlls)));
        var models = new List<ActivityVersionReconciliationModel>();

        foreach (var dll in folderDlls)
        {
            Assembly assembly;
            try
            {
                assembly = context.LoadFromAssemblyPath(dll);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skipping '{Dll}': assembly could not be loaded for reflection-only scanning.", dll);
                continue;
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                logger.LogWarning(ex, "Partial type load in '{Dll}'; scanning the types that resolved.", dll);
                types = ex.Types.Where(t => t is not null).ToArray()!;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skipping '{Dll}': types could not be reflected.", dll);
                continue;
            }

            foreach (var type in types)
            {
                bool isActivityType;

                try
                {
                    isActivityType = IsActivityType(type);
                }
                catch (Exception ex) when (IsRecoverableReflectionException(ex))
                {
                    logger.LogWarning(ex, "Skipping type '{Type}' in '{Dll}': interfaces could not be resolved during activity scanning.", type.FullName, dll);
                    continue;
                }

                if (!isActivityType)
                    continue;

                models.Add(
                    BuildModel(type, assembly)
                );
            }
        }

        return models;
    }

    private ActivityVersionReconciliationModel BuildModel(Type type, Assembly assembly)
    {
        var version = versionResolver.Resolve(type, assembly);
        var category = categoryResolver.Resolve(type, assembly);
        var attributes = type.GetCustomAttributesData().ToArray();

        var inputs = new List<InputDefinition>();
        var outputs = new List<OutputDefinition>();

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var inputNames = properties
            .Where(property => ReflectionOnlyAttributes.HasAttributeUpPropertyChain(property, ActivityInputAttributeFullName))
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var property in properties)
        {
            if (!ReflectionOnlyAttributes.HasAttributeUpPropertyChain(property, ActivityInputAttributeFullName))
                continue;

            var metadata = ReadActivityInputMetadata(property, property.PropertyType, inputNames, type.FullName!);
            var attribute = ReflectionOnlyAttributes.FindAttributeUpPropertyChain(property, ActivityInputAttributeFullName)!;
            var key = ReadNamedStringArgument(attribute, nameof(ActivityInputAttribute.Key)) ?? property.Name;
            inputs.Add(new InputDefinition(
                ReferenceKey: key,
                Name: property.Name,
                Type: ToTypeReference(property.PropertyType),
                StorageDriverType: null,
                DisplayName: property.Name,
                Category: metadata.Category,
                Order: metadata.Order,
                UiHint: metadata.UiHint,
                UISpecifications: metadata.UiSpecifications,
                IsRequired: HasRequired(property),
                DefaultValue: metadata.DefaultValue,
                DefaultSyntax: metadata.DefaultSyntax)
            {
                IsNullable = IsNullable(property)
            });
        }

        var resultType = FindTypedActivityResult(type);
        if (resultType is not null)
        {
            foreach (var property in resultType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var attribute = ReflectionOnlyAttributes.FindAttributeUpPropertyChain(property, OutputAttributeFullName);
                if (attribute is null)
                    continue;

                var key = ReadNamedStringArgument(attribute, nameof(OutputAttribute.Key)) ?? property.Name;
                outputs.Add(new OutputDefinition(
                    ReferenceKey: key,
                    Name: property.Name,
                    Type: ToTypeReference(property.PropertyType),
                    StorageDriverType: null,
                    DisplayName: property.Name,
                    Category: null,
                    IsRequired: !HasNamedArgument(attribute, nameof(OutputAttribute.IsRequired)) ||
                                ReadNamedBoolArgument(attribute, nameof(OutputAttribute.IsRequired))));
            }
        }

        return new ActivityVersionReconciliationModel(
            Id: null,
            Version: version,
            ActivityTypeKey: type.FullName!,
            DisplayName: null,
            Category: category,
            Description: null,
            ProviderKey: ProviderKey,
            ProviderSchemaVersion: SchemaVersion,
            ConsumerKey: WellKnownRuntimeActivityConsumers.ClrActivity,
            ConsumerSchemaVersion: RuntimeActivityDescriptor.InitialSchemaVersion,
            Descriptor: new ClrActivityDescriptor(TypeAliasConvention.CanonicalAlias(type)),
            Inputs: inputs,
            Outputs: outputs,
            DesignFacets: BuildDesignFacets(attributes),
            // Keep CLR catalog content stable for already-reconciled activity versions. Runtime trigger
            // classification is derived from the CLR descriptor by ExecutableNodeCompiler instead; changing
            // this value in place would invalidate persisted same-version hashes during an upgrade.
            ExecutionType: ActivityExecutionType.Action);
    }

    private static IReadOnlyCollection<ActivityDesignFacet> BuildDesignFacets(IReadOnlyCollection<CustomAttributeData> attributes)
    {
        var structureAttribute = attributes.FirstOrDefault(attribute => attribute.AttributeType.FullName == ActivityStructureAttributeFullName);
        if (structureAttribute is null)
            return [];

        var kind = ReadRequiredStringConstructorArgument(structureAttribute, 0);
        var schemaVersion = ReadRequiredStringConstructorArgument(structureAttribute, 1);
        var mode = ReadNamedStringArgument(structureAttribute, nameof(ActivityStructureAttribute.Mode)) ?? "generic";
        var supportsScopedVariables = ReadNamedBoolArgument(structureAttribute, nameof(ActivityStructureAttribute.SupportsScopedVariables));
        var slots = attributes
            .Where(attribute => attribute.AttributeType.FullName == ActivityChildSlotAttributeFullName)
            .Select(ToSlotDescriptor)
            .ToArray();
        var payload = new ActivityStructureDesignFacetPayload(
            mode,
            supportsScopedVariables,
            slots,
            BuildInitialPayload(mode, slots));

        return [new ActivityDesignFacet(kind, schemaVersion, JsonSerializer.SerializeToElement(payload, SerializerOptions))];
    }

    private static ActivityChildSlotDesignDescriptor ToSlotDescriptor(CustomAttributeData attribute) =>
        new(
            ReadRequiredStringConstructorArgument(attribute, 0),
            ReadRequiredStringConstructorArgument(attribute, 1),
            ReadRequiredStringConstructorArgument(attribute, 2),
            ReadRequiredStringConstructorArgument(attribute, 3),
            ReadNamedStringArgument(attribute, nameof(ActivityChildSlotAttribute.CollectionProperty)),
            ReadNamedStringArgument(attribute, nameof(ActivityChildSlotAttribute.ChildProperty)),
            ReadNamedStringArgument(attribute, nameof(ActivityChildSlotAttribute.LabelProperty)),
            ReadNamedStringArgument(attribute, nameof(ActivityChildSlotAttribute.SlotNameTemplate)));

    private static IReadOnlyDictionary<string, object?> BuildInitialPayload(
        string mode,
        IReadOnlyCollection<ActivityChildSlotDesignDescriptor> slots)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var slot in slots)
        {
            var property = slot.CollectionProperty ?? slot.Property;
            if (payload.ContainsKey(property))
                continue;

            payload[property] = slot.CollectionProperty is not null || slot.Cardinality == ActivityChildSlotCardinalities.Many
                ? Array.Empty<object>()
                : null;
        }

        if (mode == "flowchart")
        {
            payload.TryAdd("connections", Array.Empty<object>());
            payload.TryAdd("startNodeId", null);
            payload.TryAdd("nodeMetadata", new Dictionary<string, object>(StringComparer.Ordinal));
            payload.TryAdd("connectionMetadata", new Dictionary<string, object>(StringComparer.Ordinal));
        }

        return payload;
    }

    private static string ReadRequiredStringConstructorArgument(CustomAttributeData attribute, int index)
    {
        if (attribute.ConstructorArguments.Count <= index || attribute.ConstructorArguments[index].Value is not string value || string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Attribute '{attribute.AttributeType.FullName}' is missing required string constructor argument {index}.");

        return value;
    }

    private static string? ReadNamedStringArgument(CustomAttributeData attribute, string name) =>
        attribute.NamedArguments.FirstOrDefault(argument => argument.MemberName == name).TypedValue.Value as string;

    private static bool ReadNamedBoolArgument(CustomAttributeData attribute, string name)
    {
        var value = attribute.NamedArguments.FirstOrDefault(argument => argument.MemberName == name).TypedValue.Value;
        return value is bool boolValue && boolValue;
    }

    private static bool IsActivityType(Type type) =>
        type is { IsClass: true, IsAbstract: false }
        && type.GetInterfaces().Any(i => i.FullName == ActivityInterfaceFullName);

    private static Type? FindTypedActivityResult(Type activityType)
    {
        var contracts = activityType.GetInterfaces()
            .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition().FullName == ActivityResultInterfaceFullName)
            .Select(candidate => candidate.GetGenericArguments()[0])
            .Distinct()
            .ToArray();

        return contracts.Length switch
        {
            0 => null,
            1 => contracts[0],
            _ => throw new InvalidOperationException($"Activity type '{activityType.FullName}' declares more than one atomic result type.")
        };
    }

    private static bool IsRecoverableReflectionException(Exception exception) =>
        exception is FileNotFoundException or FileLoadException or TypeLoadException or BadImageFormatException;

    // Walk the base-property chain: requiredness declared on a base class's input property must
    // be honoured even though a reflection-only MetadataLoadContext gives no inherit-aware attribute
    // read (issue #417 item 3). Support both Elsa's [Required] marker and the metadata emitted by
    // C# `required` properties so the documented activity-authoring syntax maps to the same contract.
    private static bool HasRequired(PropertyInfo property) =>
        ReflectionOnlyAttributes.HasAttributeUpPropertyChain(property, RequiredAttributeFullName) ||
        ReflectionOnlyAttributes.HasAttributeUpPropertyChain(property, RequiredMemberAttributeFullName);

    private static bool IsNullable(PropertyInfo property)
    {
        var propertyType = property.PropertyType;
        if (propertyType.IsValueType)
        {
            return propertyType.IsGenericType &&
                   StringComparer.Ordinal.Equals(
                       propertyType.GetGenericTypeDefinition().FullName,
                       typeof(Nullable<>).FullName);
        }

        // Unknown is intentionally treated as nullable for assemblies compiled without nullable metadata.
        // Only an explicit NotNull annotation may tighten the published contract.
        return new NullabilityInfoContext().Create(property).WriteState is not NullabilityState.NotNull;
    }

    private static ActivityInputMetadata ReadActivityInputMetadata(
        PropertyInfo property,
        Type? valueType,
        IReadOnlySet<string> inputNames,
        string activityTypeName)
    {
        var attribute = ReflectionOnlyAttributes.FindAttributeUpPropertyChain(property, ActivityInputAttributeFullName);
        var order = attribute is null ? 0 : ReadNamedSingleArgument(attribute, nameof(ActivityInputAttribute.Order)) ?? 0;
        var category = attribute is null ? null : ReadNamedStringArgument(attribute, nameof(ActivityInputAttribute.Category));
        var defaultValue = attribute is null ? null : ReadNamedStringArgument(attribute, nameof(ActivityInputAttribute.DefaultValue));
        var defaultSyntax = attribute is null ? null : ReadNamedStringArgument(attribute, nameof(ActivityInputAttribute.DefaultSyntax));
        var uiHint = attribute is null ? null : ReadNamedStringArgument(attribute, nameof(ActivityInputAttribute.UIHint));
        var uiSpecifications = ReadOptionSpecifications(property, valueType, inputNames, activityTypeName);

        return new ActivityInputMetadata(
            order,
            string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
            string.IsNullOrWhiteSpace(defaultValue) ? null : ParseDefaultValue(defaultValue, valueType),
            defaultSyntax,
            string.IsNullOrWhiteSpace(uiHint) ? null : uiHint.Trim(),
            uiSpecifications);
    }

    private static JsonElement? ReadOptionSpecifications(
        PropertyInfo property,
        Type? valueType,
        IReadOnlySet<string> inputNames,
        string activityTypeName)
    {
        foreach (var declaration in ReflectionOnlyAttributes.EnumeratePropertyChain(property))
        {
            var attributes = declaration.GetCustomAttributesData();
            var inputAttribute = attributes.FirstOrDefault(x => x.AttributeType.FullName == ActivityInputAttributeFullName);
            var typedOptions = attributes.Where(x => x.AttributeType.FullName == ActivityInputOptionAttributeFullName).ToArray();
            var declaresOptions = inputAttribute is not null && HasNamedArgument(inputAttribute, nameof(ActivityInputAttribute.Options));
            var declaresProvider = inputAttribute is not null && HasNamedArgument(inputAttribute, nameof(ActivityInputAttribute.OptionsProvider));
            var declaresDependencies = inputAttribute is not null && HasNamedArgument(inputAttribute, nameof(ActivityInputAttribute.OptionsProviderDependencies));

            if (!declaresOptions && !declaresProvider && !declaresDependencies && typedOptions.Length == 0)
                continue;

            return BuildOptionSpecifications(
                property,
                valueType,
                inputNames,
                activityTypeName,
                inputAttribute,
                typedOptions,
                declaresOptions,
                declaresProvider,
                declaresDependencies);
        }

        return null;
    }

    private static JsonElement BuildOptionSpecifications(
        PropertyInfo property,
        Type? valueType,
        IReadOnlySet<string> inputNames,
        string activityTypeName,
        CustomAttributeData? inputAttribute,
        IReadOnlyCollection<CustomAttributeData> typedOptions,
        bool declaresOptions,
        bool declaresProvider,
        bool declaresDependencies)
    {
        var provider = declaresProvider ? ReadNamedStringArgument(inputAttribute!, nameof(ActivityInputAttribute.OptionsProvider)) : null;
        var dependencies = declaresDependencies
            ? ReadNamedStringArrayArgument(inputAttribute!, nameof(ActivityInputAttribute.OptionsProviderDependencies))
            : [];
        var sourceCount = (declaresOptions ? 1 : 0) + (typedOptions.Count > 0 ? 1 : 0) + (declaresProvider ? 1 : 0);

        if (sourceCount > 1)
            throw InvalidInputMetadata(activityTypeName, property.Name, "Option metadata has conflicting static, typed, or provider sources.");
        if (declaresDependencies && !declaresProvider)
            throw InvalidInputMetadata(activityTypeName, property.Name, "Option provider dependencies require an options provider.");
        if (declaresProvider && string.IsNullOrWhiteSpace(provider))
            throw InvalidInputMetadata(activityTypeName, property.Name, "The options provider key cannot be blank.");

        if (declaresProvider)
        {
            var seenDependencies = new HashSet<string>(StringComparer.Ordinal);
            foreach (var dependency in dependencies)
            {
                if (string.IsNullOrWhiteSpace(dependency))
                    throw InvalidInputMetadata(activityTypeName, property.Name, "An options provider dependency cannot be blank.");
                if (!seenDependencies.Add(dependency))
                    throw InvalidInputMetadata(activityTypeName, property.Name, $"Option provider dependency '{dependency}' is duplicated.");
                if (!inputNames.Contains(dependency))
                    throw InvalidInputMetadata(activityTypeName, property.Name, $"Option provider dependency '{dependency}' does not identify a sibling input.");
            }

            return JsonSerializer.SerializeToElement(new
            {
                optionsProvider = new { key = provider!.Trim(), dependsOn = dependencies }
            }, SerializerOptions);
        }

        var optionValueType = GetOptionValueType(valueType);
        var options = declaresOptions
            ? ReadNamedStringArrayArgument(inputAttribute!, nameof(ActivityInputAttribute.Options))
                .Select(value => ToStringOption(value, optionValueType, activityTypeName, property.Name))
                .ToArray()
            : typedOptions
                .Select(option => ToTypedOption(option, optionValueType, activityTypeName, property.Name))
                .ToArray();

        var duplicate = options
            .GroupBy(option => GetOptionIdentity(option.Value), StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw InvalidInputMetadata(activityTypeName, property.Name, $"Option value '{duplicate.Key}' is duplicated.");

        return JsonSerializer.SerializeToElement(new { options }, SerializerOptions);
    }

    private static ActivityInputOptionSpecification ToStringOption(string? value, Type? targetType, string activityTypeName, string inputName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw InvalidInputMetadata(activityTypeName, inputName, "A string option value cannot be blank.");
        if (targetType?.FullName != typeof(string).FullName)
            throw InvalidInputMetadata(activityTypeName, inputName, $"String option value '{value}' is not compatible with input type '{targetType?.FullName ?? "<unknown>"}'.");

        return new ActivityInputOptionSpecification(value, JsonSerializer.SerializeToElement(value));
    }

    private static ActivityInputOptionSpecification ToTypedOption(CustomAttributeData attribute, Type? targetType, string activityTypeName, string inputName)
    {
        if (attribute.ConstructorArguments.Count < 2)
            throw InvalidInputMetadata(activityTypeName, inputName, "A typed option requires a label and value.");

        var label = attribute.ConstructorArguments[0].Value as string;
        if (string.IsNullOrWhiteSpace(label))
            throw InvalidInputMetadata(activityTypeName, inputName, "A typed option label cannot be blank.");

        var valueArgument = UnwrapAttributeArgument(attribute.ConstructorArguments[1]);
        if (valueArgument.Value is null)
            throw InvalidInputMetadata(activityTypeName, inputName, "A typed option value cannot be null.");

        return new ActivityInputOptionSpecification(label, ConvertOptionValue(valueArgument, targetType, activityTypeName, inputName));
    }

    private static CustomAttributeTypedArgument UnwrapAttributeArgument(CustomAttributeTypedArgument argument) =>
        argument.Value is CustomAttributeTypedArgument nested ? nested : argument;

    private static JsonElement ConvertOptionValue(CustomAttributeTypedArgument argument, Type? targetType, string activityTypeName, string inputName)
    {
        targetType = UnwrapNullable(targetType);
        var sourceType = argument.ArgumentType;
        var value = argument.Value!;

        if (targetType is not null && targetType.IsEnum)
        {
            if (!sourceType.IsEnum || sourceType.FullName != targetType.FullName)
                throw IncompatibleOption(activityTypeName, inputName, sourceType, targetType);

            var enumName = sourceType.GetFields(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(field => EqualsNumeric(field.GetRawConstantValue(), value))?.Name;
            if (enumName is null)
                throw InvalidInputMetadata(activityTypeName, inputName, $"Enum option value '{value}' is not defined by '{targetType.FullName}'.");

            return JsonSerializer.SerializeToElement(enumName);
        }

        if (targetType?.FullName == typeof(string).FullName && value is string stringValue)
        {
            if (string.IsNullOrWhiteSpace(stringValue))
                throw InvalidInputMetadata(activityTypeName, inputName, "A typed string option value cannot be blank.");
            return JsonSerializer.SerializeToElement(stringValue);
        }

        if (targetType?.FullName == typeof(bool).FullName && value is bool boolValue)
            return JsonSerializer.SerializeToElement(boolValue);

        if (targetType is not null && IsNumericType(targetType) && IsNumericType(sourceType))
        {
            try
            {
                var converted = Convert.ChangeType(value, targetType.FullName switch
                {
                    "System.Byte" => typeof(byte),
                    "System.SByte" => typeof(sbyte),
                    "System.Int16" => typeof(short),
                    "System.UInt16" => typeof(ushort),
                    "System.Int32" => typeof(int),
                    "System.UInt32" => typeof(uint),
                    "System.Int64" => typeof(long),
                    "System.UInt64" => typeof(ulong),
                    "System.Single" => typeof(float),
                    "System.Double" => typeof(double),
                    _ => typeof(decimal)
                }, CultureInfo.InvariantCulture);
                ValidateNumericFidelity(converted, targetType, activityTypeName, inputName);
                return JsonSerializer.SerializeToElement(converted);
            }
            catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
            {
                throw IncompatibleOption(activityTypeName, inputName, sourceType, targetType);
            }
        }

        throw IncompatibleOption(activityTypeName, inputName, sourceType, targetType);
    }

    private static void ValidateNumericFidelity(object value, Type targetType, string activityTypeName, string inputName)
    {
        if (value is double doubleValue)
        {
            if (!double.IsFinite(doubleValue))
                throw InvalidInputMetadata(activityTypeName, inputName, $"Numeric option value '{doubleValue}' must be finite.");
            if (Math.Truncate(doubleValue) == doubleValue && Math.Abs(doubleValue) > MaxJavaScriptSafeInteger)
                throw InvalidInputMetadata(activityTypeName, inputName, $"Integral option value '{doubleValue:R}' exceeds the JavaScript safe integer range ±{MaxJavaScriptSafeInteger}.");
        }
        else if (value is float singleValue)
        {
            if (!float.IsFinite(singleValue))
                throw InvalidInputMetadata(activityTypeName, inputName, $"Numeric option value '{singleValue}' must be finite.");
            if (MathF.Truncate(singleValue) == singleValue && MathF.Abs(singleValue) > MaxJavaScriptSafeInteger)
                throw InvalidInputMetadata(activityTypeName, inputName, $"Integral option value '{singleValue:R}' exceeds the JavaScript safe integer range ±{MaxJavaScriptSafeInteger}.");
        }
        else
        {
            var decimalValue = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            if (decimal.Truncate(decimalValue) == decimalValue && (decimalValue < -MaxJavaScriptSafeInteger || decimalValue > MaxJavaScriptSafeInteger))
                throw InvalidInputMetadata(activityTypeName, inputName, $"Integral option value '{decimalValue}' exceeds the JavaScript safe integer range ±{MaxJavaScriptSafeInteger}.");
        }

        if (targetType.FullName != typeof(decimal).FullName)
            return;

        var authoredDecimal = (decimal)value;
        try
        {
            var asDouble = (double)authoredDecimal;
            if (!double.IsFinite(asDouble) || (decimal)asDouble != authoredDecimal)
                throw InvalidInputMetadata(activityTypeName, inputName, $"Decimal option value '{authoredDecimal}' cannot round-trip exactly through a double.");
        }
        catch (OverflowException)
        {
            throw InvalidInputMetadata(activityTypeName, inputName, $"Decimal option value '{authoredDecimal}' cannot round-trip exactly through a double.");
        }
    }

    private static string GetOptionIdentity(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => $"string:{value.GetString()}",
        JsonValueKind.True => "boolean:true",
        JsonValueKind.False => "boolean:false",
        JsonValueKind.Number when value.TryGetDecimal(out var decimalValue) => $"number:{decimalValue.ToString("G29", CultureInfo.InvariantCulture)}",
        JsonValueKind.Number when value.TryGetDouble(out var doubleValue) => $"number:{doubleValue.ToString("R", CultureInfo.InvariantCulture)}",
        _ => $"{value.ValueKind}:{value.GetRawText()}"
    };

    private static Type? GetOptionValueType(Type? valueType)
    {
        if (valueType is null)
            return null;
        if (valueType.IsArray)
            return UnwrapNullable(valueType.GetElementType());
        if (valueType.IsGenericType && valueType.GetGenericArguments() is [var element])
        {
            var definition = valueType.GetGenericTypeDefinition().FullName;
            if (definition is "System.Collections.Generic.IEnumerable`1"
                or "System.Collections.Generic.ICollection`1"
                or "System.Collections.Generic.IReadOnlyCollection`1"
                or "System.Collections.Generic.IList`1"
                or "System.Collections.Generic.IReadOnlyList`1"
                or "System.Collections.Generic.List`1"
                or "System.Collections.Generic.HashSet`1")
                return UnwrapNullable(element);
        }

        return UnwrapNullable(valueType);
    }

    private static Type? UnwrapNullable(Type? type) =>
        type is { IsGenericType: true } && type.GetGenericTypeDefinition().FullName == "System.Nullable`1"
            ? type.GetGenericArguments()[0]
            : type;

    private static bool IsNumericType(Type type) => type.FullName is
        "System.Byte" or "System.SByte" or "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32"
        or "System.Int64" or "System.UInt64" or "System.Single" or "System.Double" or "System.Decimal";

    private static bool EqualsNumeric(object? left, object? right)
    {
        if (left is null || right is null)
            return false;

        try
        {
            return Convert.ToDecimal(left, CultureInfo.InvariantCulture) == Convert.ToDecimal(right, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
        {
            return Equals(left, right);
        }
    }

    private static InvalidOperationException IncompatibleOption(string activityTypeName, string inputName, Type sourceType, Type? targetType) =>
        InvalidInputMetadata(activityTypeName, inputName, $"Option type '{sourceType.FullName}' is not compatible with input type '{targetType?.FullName ?? "<unknown>"}'.");

    private static InvalidOperationException InvalidInputMetadata(string activityTypeName, string inputName, string message) =>
        new($"Invalid activity input option metadata for '{activityTypeName}.{inputName}': {message}");

    private static bool HasNamedArgument(CustomAttributeData attribute, string name) =>
        attribute.NamedArguments.Any(argument => argument.MemberName == name);

    private static string?[] ReadNamedStringArrayArgument(CustomAttributeData attribute, string name)
    {
        var value = attribute.NamedArguments.FirstOrDefault(argument => argument.MemberName == name).TypedValue.Value;
        return value is IReadOnlyCollection<CustomAttributeTypedArgument> arguments
            ? arguments.Select(argument => argument.Value as string).ToArray()
            : [];
    }

    private static float? ReadNamedSingleArgument(CustomAttributeData attribute, string name)
    {
        var value = attribute.NamedArguments.FirstOrDefault(argument => argument.MemberName == name).TypedValue.Value;
        return value switch
        {
            float single => single,
            double number => (float)number,
            _ => null
        };
    }

    private static JsonElement ParseDefaultValue(string value, Type? valueType)
    {
        var typeName = valueType?.FullName;

        if (typeName == typeof(bool).FullName && bool.TryParse(value, out var boolValue))
            return JsonSerializer.SerializeToElement(boolValue);
        if (typeName == typeof(byte).FullName && byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var byteValue))
            return JsonSerializer.SerializeToElement(byteValue);
        if (typeName == typeof(sbyte).FullName && sbyte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sbyteValue))
            return JsonSerializer.SerializeToElement(sbyteValue);
        if (typeName == typeof(short).FullName && short.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var shortValue))
            return JsonSerializer.SerializeToElement(shortValue);
        if (typeName == typeof(ushort).FullName && ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ushortValue))
            return JsonSerializer.SerializeToElement(ushortValue);
        if (typeName == typeof(int).FullName && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
            return JsonSerializer.SerializeToElement(intValue);
        if (typeName == typeof(uint).FullName && uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uintValue))
            return JsonSerializer.SerializeToElement(uintValue);
        if (typeName == typeof(long).FullName && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
            return JsonSerializer.SerializeToElement(longValue);
        if (typeName == typeof(ulong).FullName && ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ulongValue))
            return JsonSerializer.SerializeToElement(ulongValue);
        if (typeName == typeof(float).FullName && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
            return JsonSerializer.SerializeToElement(floatValue);
        if (typeName == typeof(double).FullName && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
            return JsonSerializer.SerializeToElement(doubleValue);
        if (typeName == typeof(decimal).FullName && decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue))
            return JsonSerializer.SerializeToElement(decimalValue);

        return JsonSerializer.SerializeToElement(value);
    }

    private sealed record ActivityInputMetadata(
        float Order,
        string? Category,
        JsonElement? DefaultValue,
        string? DefaultSyntax,
        string? UiHint,
        JsonElement? UiSpecifications);

    private sealed record ActivityInputOptionSpecification(string Label, JsonElement Value);

    // Reflection-only path: types come from a MetadataLoadContext, so the runtime well-known type
    // registry can't resolve them. The element alias is produced by the shared TypeAliasConvention —
    // a reserved bare alias for BCL primitives (string→"String", int→"Int32"), else the dotted FullName.
    // The framework's runtime registration pass registers activity I/O CLR types under the SAME
    // convention, so these aliases resolve back to the real CLR type at compile time (FR-004b).
    private static TypeReference ToTypeReference(Type? valueType) =>
        valueType is null
            ? new TypeReference("Object")
            : TypeReferenceFactory.FromClrType(valueType, TypeAliasConvention.CanonicalAlias);

    // The host's own dependency closure (base directory, trusted-platform-assemblies, runtime
    // directory) is enumerated from disk and is process-lifetime-invariant, so it is read once and
    // cached instead of re-globbing on every Scan() call (issue #417 item 2). AppDomain assemblies
    // are NOT cached here: that set can grow as assemblies load lazily, and enumerating it is
    // in-memory (no disk IO), so it stays live per call to preserve resolution behaviour exactly.
    private static readonly Lazy<IReadOnlyList<string>> InvariantFrameworkPaths = new(CollectInvariantFrameworkPaths);

    // Instance method (not static): the collision path logs through the injected logger. Precedence
    // is unchanged (first-wins: folder → AppDomain → cached framework closure); we only surface the
    // drop that was previously silent (issue #417 item 4).
    private Dictionary<string, string>.ValueCollection BuildResolverPaths(IEnumerable<string> folderDlls)
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);

        void Add(string path)
        {
            if (!seenPaths.Add(path))
                return;

            var name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(name))
                return;

            if (byName.TryGetValue(name, out var existing))
            {
                // A later source (AppDomain or framework closure) offers the same simple-name as an
                // already-registered path. First-wins is intentional, but the drop must not be silent:
                // divergent copies of the same assembly can otherwise mask a version the author shipped.
                logger.LogWarning(
                    "Duplicate assembly name '{AssemblyName}' during reflection-only resolver setup; keeping '{KeptPath}' and skipping '{SkippedPath}'.",
                    name, existing, path);
                return;
            }

            byName[name] = path;
        }

        // Author assemblies win over framework copies so we read the version the author shipped.
        foreach (var dll in folderDlls)
            Add(dll);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
                continue;

            var location = assembly.Location;
            if (!string.IsNullOrEmpty(location) && File.Exists(location))
                Add(location);
        }

        // Cached framework closure (base directory → TPA → runtime directory), in the same
        // first-wins order as before; folder + AppDomain entries above still take precedence.
        foreach (var dll in InvariantFrameworkPaths.Value)
            Add(dll);

        return byName.Values;
    }

    private static IReadOnlyList<string> CollectInvariantFrameworkPaths()
    {
        var paths = new List<string>();

        // The host's own dependency closure (e.g. the activity base types and their references)
        // lives next to the host binary; these are not necessarily loaded into the AppDomain yet,
        // so add them explicitly to satisfy reflection-only base-type/reference resolution.
        var baseDirectory = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDirectory) && Directory.Exists(baseDirectory))
            paths.AddRange(Directory.EnumerateFiles(baseDirectory, "*.dll"));

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedPlatformAssemblies)
            paths.AddRange(trustedPlatformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        paths.AddRange(Directory.EnumerateFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"));

        return paths;
    }
}
