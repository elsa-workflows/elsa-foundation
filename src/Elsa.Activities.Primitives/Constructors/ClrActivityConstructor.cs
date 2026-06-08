using System.Text.Json;
using Elsa.Activities.Primitives.Binding;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Primitives.Constructors;

/// <summary>
/// Constructs CLR-backed activities. The descriptor is <see cref="TypeInformation"/> (the CLR
/// descriptor, owned by <c>Elsa.Primitives</c>); construction loads the type and activates it, then
/// binds the author arguments to its typed properties. Runtime-side, no Design dependency.
/// </summary>
/// <remarks>
/// The payload is materialised with <see cref="IPayloadSerializer"/> — the same contract that wrote
/// it on the design side — so casing/converter settings round-trip faithfully (raw
/// <c>JsonSerializer</c> defaults would not match the camelCase-on-write convention).
/// </remarks>
public sealed class ClrActivityConstructor(IServiceProvider serviceProvider, ActivityArgumentBinder binder, IPayloadSerializer payloadSerializer)
    : IActivityConstructor<TypeInformation>
{
    public string DescriptorType => typeof(TypeInformation).FullName!;

    ValueTask<IActivity> IActivityConstructor.Construct(
        JsonElement payload,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken)
        => Construct(payloadSerializer.Deserialize<TypeInformation>(payload), inputs, outputs, cancellationToken);

    public ValueTask<IActivity> Construct(
        TypeInformation descriptor,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken)
    {
        var type = descriptor.LoadType();
        var activity = (IActivity)ActivatorUtilities.CreateInstance(serviceProvider, type);
        binder.Bind(activity, inputs, outputs);
        return new(activity);
    }
}
