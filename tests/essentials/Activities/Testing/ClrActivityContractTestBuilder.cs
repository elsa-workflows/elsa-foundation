using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Runtime.Core;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Testing;

/// <summary>Builds complete CLR activity contracts and pinned input bindings for runtime tests.</summary>
public static class ClrActivityContractTestBuilder
{
    public static ActivityContract BuildContract(Type activityType)
    {
        ArgumentNullException.ThrowIfNull(activityType);

        var inputs = activityType.GetProperties()
            .Select(property => (Property: property, Attribute: property.GetCustomAttributes(typeof(ActivityInputAttribute), inherit: true)
                .Cast<ActivityInputAttribute>()
                .SingleOrDefault()))
            .Where(candidate => candidate.Attribute is not null)
            .Select(candidate =>
            {
                var attribute = candidate.Attribute!;
                var hasDefault = attribute.DefaultValue is not null;
                return new ActivityInputContract(
                    attribute.Key ?? candidate.Property.Name,
                    candidate.Property.Name,
                    ValueType(candidate.Property.PropertyType),
                    candidate.Property.IsDefined(typeof(RequiredAttribute), inherit: true),
                    IsNullable(candidate.Property),
                    hasDefault,
                    hasDefault
                        ? JsonSerializer.SerializeToElement(ParseDefault(candidate.Property.PropertyType, attribute.DefaultValue!))
                        : null,
                    ActivityValuePolicy.Default);
            })
            .ToArray();
        var resultType = activityType.GetInterfaces()
            .Single(candidate => candidate.IsGenericType &&
                                 candidate.GetGenericTypeDefinition() == typeof(IActivityResult<>))
            .GetGenericArguments()[0];
        var projections = resultType.GetProperties()
            .Select(property => (Property: property, Attribute: property.GetCustomAttributes(typeof(OutputAttribute), inherit: true)
                .Cast<OutputAttribute>()
                .SingleOrDefault()))
            .Where(candidate => candidate.Attribute is not null)
            .Select(candidate => new ActivityResultProjectionContract(
                candidate.Attribute!.Key ?? candidate.Property.Name,
                candidate.Attribute.Path ?? JsonNamingPolicy.CamelCase.ConvertName(candidate.Property.Name),
                ValueType(candidate.Property.PropertyType),
                candidate.Attribute.IsRequired,
                ActivityValuePolicy.Default))
            .ToArray();
        var descriptorType = typeof(ClrActivityDescriptor).FullName!;
        var alias = TypeAliasConvention.CanonicalAlias(activityType);
        return new ActivityContract(
            activityType.FullName!,
            "1.0.0",
            descriptorType,
            JsonSerializer.SerializeToElement(new ClrActivityDescriptor(alias)),
            inputs,
            new ActivityResultContract(
                ValueType(resultType),
                true,
                ActivityValuePolicy.Default,
                projections),
            activityType.GetCustomAttributes(typeof(ActivityOutcomeAttribute), inherit: true)
                .Cast<ActivityOutcomeAttribute>()
                .Select(attribute => attribute.Key)
                .DefaultIfEmpty(ActivityOutcomes.Done)
                .ToArray(),
            new ActivityActivationRequirement(descriptorType, alias),
            // Mirror ExecutableNodeCompiler.ResolveSideEffectProfile (ADR 0032 R1 / spec 107): unmarked ⇒ External.
            sideEffectProfile: activityType.GetCustomAttribute<ActivitySideEffectProfileAttribute>(inherit: true)?.Profile
                ?? SideEffectProfile.External);
    }

    public static IReadOnlyDictionary<string, RuntimeInputBinding> CompleteInputBindings(
        ActivityContract contract,
        IReadOnlyDictionary<string, RuntimeInputBinding>? authoredBindings)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var bindings = authoredBindings?.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)
                       ?? new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal);
        foreach (var input in contract.Inputs.Values.Where(input => !bindings.ContainsKey(input.Key)))
        {
            var policy = ValueProtectionPolicy.InstanceInline;
            var value = input.HasDefault
                ? ValueEnvelope.Inline(input.Type, input.DefaultValue!.Value, policy)
                : input.IsNullable == true
                    ? ValueEnvelope.Absent(input.Type, policy)
                    : throw new InvalidOperationException(
                        $"Test executable omits non-nullable activity input '{input.Key}' without a pinned default.");
            bindings.Add(input.Key, new RuntimeInputBinding(
                input.Key,
                input.Type,
                policy,
                RuntimeInputBindingSource.Literal,
                literal: value));
        }

        return bindings;
    }

    private static bool IsNullable(PropertyInfo property)
    {
        if (Nullable.GetUnderlyingType(property.PropertyType) is not null)
            return true;
        if (property.PropertyType.IsValueType)
            return false;
        return new NullabilityInfoContext().Create(property).ReadState != NullabilityState.NotNull;
    }

    private static object ParseDefault(Type type, string value) =>
        type == typeof(bool) ? bool.Parse(value) :
        type == typeof(int) ? int.Parse(value, CultureInfo.InvariantCulture) :
        type == typeof(TimeSpan) ? TimeSpan.Parse(value, CultureInfo.InvariantCulture) :
        value;

    private static ValueTypeDescriptor ValueType(Type type)
    {
        var reference = TypeReferenceFactory.FromClrType(type, TypeAliasConvention.CanonicalAlias);
        return new ValueTypeDescriptor(reference.Alias, reference.CollectionKind);
    }
}
