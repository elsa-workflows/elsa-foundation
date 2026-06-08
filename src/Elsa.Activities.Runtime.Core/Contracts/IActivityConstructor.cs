using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Core.Contracts;

/// <summary>
/// Non-generic, registry-stored construction contributor. One per descriptor type. The
/// <see cref="DescriptorType"/> is the registry key (a descriptor type's <c>FullName</c>),
/// derived from <c>typeof(TDescriptor)</c> — never hand-authored. A <b>contribution</b> contract
/// (framework §2.6.1): features contribute a constructor for the descriptor type they own.
/// </summary>
public interface IActivityConstructor
{
    /// <summary>The descriptor type's <c>FullName</c> this constructor handles (the registry key).</summary>
    string DescriptorType { get; }

    /// <summary>
    /// Construct a fully-wired <see cref="IActivity"/> from the serialized descriptor payload and the
    /// author-filled input/output arguments. The implementation deserializes the payload into its
    /// concrete descriptor type — the design domain never does. The returned activity is whole:
    /// type resolved, descriptor-sourced state applied, author arguments bound.
    /// </summary>
    ValueTask<IActivity> Construct(
        JsonElement payload,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken);
}

/// <summary>
/// Typed construction contributor for descriptor type <typeparamref name="TDescriptor"/>. Each
/// implementation provides a one-line bridge to the non-generic <see cref="IActivityConstructor"/>
/// that owns deserialization (<c>payload.Deserialize&lt;TDescriptor&gt;()</c>) and exposes
/// <c>DescriptorType =&gt; typeof(TDescriptor).FullName!</c>. No shared base class.
/// </summary>
public interface IActivityConstructor<TDescriptor> : IActivityConstructor
    where TDescriptor : class
{
    ValueTask<IActivity> Construct(
        TDescriptor descriptor,
        IDictionary<string, InputArgument>? inputs,
        IDictionary<string, OutputArgument>? outputs,
        CancellationToken cancellationToken);
}
