using System.Text.Json;
using Elsa.Activities.Primitives.Binding;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Exceptions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Primitives.Constructors;

/// <summary>
/// Constructs CLR-backed activities. The descriptor is <see cref="ClrActivityDescriptor"/> (the stable
/// alias identity, owned by <c>Elsa.Primitives</c>); construction resolves the alias to a live CLR type
/// through the <see cref="IWellKnownTypeRegistry"/> and activates it, then binds the author arguments to
/// its typed properties. Runtime-side, no Design dependency.
/// </summary>
/// <remarks>
/// <para>
/// Resolution is alias-based, never <c>Assembly.Load(name, version)</c>: the alias the reflection-only
/// scanner emitted is the shared <see cref="TypeAliasConvention"/> canonical alias, and the framework's
/// startup pass registers every loaded activity CLR type under that same convention, so the alias resolves
/// back to the real type. An alias that resolves to nothing is a loud <see cref="UnknownActivityTypeException"/>
/// (a missing assembly), never a silent fall-through to <c>object</c>.
/// </para>
/// <para>
/// The payload is materialised with <see cref="IPayloadSerializer"/> — the same contract that wrote it on
/// the design side — so casing/converter settings round-trip faithfully (raw <c>JsonSerializer</c> defaults
/// would not match the camelCase-on-write convention).
/// </para>
/// </remarks>
public sealed class ClrActivityConstructor(
    IServiceProvider serviceProvider,
    ActivityArgumentBinder binder,
    IPayloadSerializer payloadSerializer,
    IWellKnownTypeRegistry typeRegistry)
    : IActivityConstructor
{
    public string ConsumerKey => WellKnownRuntimeActivityConsumers.ClrActivity;
    public IReadOnlySet<string> SupportedSchemaVersions { get; } = new HashSet<string>(StringComparer.Ordinal) { RuntimeActivityDescriptor.InitialSchemaVersion };

    public ValueTask<IActivity> ConstructAsync(
        RuntimeActivityDescriptor descriptor,
        IReadOnlyDictionary<string, InputArgument> inputs,
        IReadOnlyDictionary<string, OutputArgument> outputs,
        CancellationToken cancellationToken)
        => Construct(payloadSerializer.Deserialize<ClrActivityDescriptor>(descriptor.Payload), inputs, outputs, cancellationToken);

    public ValueTask<IActivity> Construct(
        ClrActivityDescriptor descriptor,
        IReadOnlyDictionary<string, InputArgument>? inputs,
        IReadOnlyDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken)
    {
        if (!typeRegistry.TryGetTypeOrDefault(descriptor.TypeAlias, out var type))
            throw new UnknownActivityTypeException(descriptor.TypeAlias);

        var activity = (IActivity)ActivatorUtilities.CreateInstance(serviceProvider, type);
        binder.Bind(
            activity,
            inputs as IDictionary<string, InputArgument> ?? inputs?.ToDictionary(),
            outputs as IDictionary<string, OutputArgument> ?? outputs?.ToDictionary());
        return new(activity);
    }
}
