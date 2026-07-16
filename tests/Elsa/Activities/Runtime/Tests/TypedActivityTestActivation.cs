using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Primitives.Activation;
using Elsa.Activities.Primitives.Binding;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Resolvers;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Tests;

internal static class TypedActivityTestActivation
{
    public static RuntimeActivityInputMaterializer CreateMaterializer()
    {
        var registry = new WellKnownTypeRegistry();
        registry.RegisterType(typeof(string), "String");
        return new RuntimeActivityInputMaterializer(new RuntimeInputBindingResolver(), registry);
    }

    public static async ValueTask<ActivityActivationLease> ActivateAsync<TActivity>(
        IServiceProvider services,
        IReadOnlyDictionary<string, object?> values)
        where TActivity : IActivity
    {
        var activityType = typeof(TActivity);
        var alias = TypeAliasConvention.CanonicalAlias(activityType);
        var registry = new WellKnownTypeRegistry();
        registry.RegisterType(activityType, alias);
        var serializer = new JsonPayloadSerializer(new JsonPayloadConverterRegistry());
        var contractInputs = activityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (Property: property, Attribute: property.GetCustomAttribute<ActivityInputAttribute>(inherit: true)))
            .Where(item => item.Attribute is not null)
            .Select(item =>
            {
                var reference = TypeReferenceFactory.FromClrType(item.Property.PropertyType, TypeAliasConvention.CanonicalAlias);
                return new ActivityInputContract(
                    item.Attribute!.Key ?? item.Property.Name,
                    item.Property.Name,
                    new ValueTypeDescriptor(reference.Alias, reference.CollectionKind),
                    item.Property.GetCustomAttribute<RequiredAttribute>(inherit: true) is not null,
                    false,
                    null,
                    ActivityValuePolicy.Default);
            })
            .ToArray();
        var descriptor = serializer.SerializeToElement(new ClrActivityDescriptor(alias));
        var contract = new ActivityContract(
            alias,
            "1.0.0",
            typeof(ClrActivityDescriptor).FullName!,
            descriptor,
            contractInputs,
            new ActivityResultContract(new ValueTypeDescriptor("Elsa.Unit"), true, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement(typeof(ClrActivityDescriptor).FullName!, alias));
        var envelopes = contractInputs.ToDictionary(
            input => input.Key,
            input =>
            {
                var value = values[input.Key];
                return value is null
                    ? ValueEnvelope.Null(input.Type, ValueProtectionPolicy.InstanceInline)
                    : ValueEnvelope.Inline(input.Type, JsonSerializer.SerializeToElement(value, value.GetType()), ValueProtectionPolicy.InstanceInline);
            },
            StringComparer.Ordinal);
        var snapshot = new ActivityInputSnapshot(
            "invocation-1",
            contract.SchemaFingerprint,
            "test-bindings",
            envelopes,
            DateTimeOffset.UtcNow);
        var request = new ActivityActivationRequest(
            contract,
            snapshot,
            new ActivityAttempt("attempt-1", "invocation-1", 1, ActivityAttemptReason.Initial, DateTimeOffset.UtcNow));
        var activator = new ClrActivityActivator(
            services.GetRequiredService<IServiceScopeFactory>(),
            registry,
            serializer,
            new ActivityInputHydrator());
        var activation = await activator.ActivateAsync(request);
        activation.Activity.Id = "invocation-1";
        activation.Activity.NodeId = "node-1";
        return activation;
    }
}
