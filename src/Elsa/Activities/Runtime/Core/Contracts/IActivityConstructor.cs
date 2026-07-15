using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Core.Contracts;

/// <summary>
/// Runtime construction contributor keyed by a stable consumer key and supported descriptor schemas.
/// </summary>
public interface IActivityConstructor
{
    string ConsumerKey { get; }
    IReadOnlySet<string> SupportedSchemaVersions { get; }

    ValueTask<IActivity> ConstructAsync(
        RuntimeActivityDescriptor descriptor,
        IReadOnlyDictionary<string, InputArgument> inputs,
        IReadOnlyDictionary<string, OutputArgument> outputs,
        CancellationToken cancellationToken);
}

/// <summary>
/// Typed construction contributor for payload type <typeparamref name="TDescriptor"/>. The generic
/// bridge owns JSON deserialization while the implementation supplies its stable
/// <see cref="IActivityConstructor.ConsumerKey"/>; schema 1 is declared by this convenience contract.
/// </summary>
public interface IActivityConstructor<TDescriptor> : IActivityConstructor
    where TDescriptor : class
{
    IReadOnlySet<string> IActivityConstructor.SupportedSchemaVersions =>
        new HashSet<string>(StringComparer.Ordinal) { RuntimeActivityDescriptor.InitialSchemaVersion };

    ValueTask<IActivity> IActivityConstructor.ConstructAsync(
        RuntimeActivityDescriptor descriptor,
        IReadOnlyDictionary<string, InputArgument> inputs,
        IReadOnlyDictionary<string, OutputArgument> outputs,
        CancellationToken cancellationToken)
    {
        var typedDescriptor = descriptor.Payload.Deserialize<TDescriptor>()
            ?? throw new InvalidOperationException($"Runtime descriptor for consumer '{descriptor.ConsumerKey}' resolved to null.");
        return Construct(
            typedDescriptor,
            inputs as IDictionary<string, InputArgument> ?? inputs.ToDictionary(),
            outputs as IDictionary<string, OutputArgument> ?? outputs.ToDictionary(),
            cancellationToken);
    }

    ValueTask<IActivity> Construct(
        TDescriptor descriptor,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken);
}
