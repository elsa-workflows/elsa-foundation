using Elsa.Etl.Core;

namespace Elsa.Etl.Runtime.Bindings;

internal enum EtlFileRole { Source, Lookup, MainOutput, ErrorOutput }

/// <summary>Trusted host configuration. This object is never accepted in an authored pipeline.</summary>
internal sealed record HostFileBinding(
    EtlScope Scope,
    string Alias,
    EtlFileRole Role,
    string Revision,
    string Path,
    bool IsCurrent,
    string? ExpectedSha256 = null,
    bool ImmutableInput = false,
    bool AtomicPublication = false);

/// <summary>Retained in runtime state; the handle resolves only the original host revision.</summary>
internal sealed record PinnedFileBinding(EtlScope Scope, string Alias, EtlFileRole Role, string Revision, string Handle, string? ExpectedSha256);

internal sealed record FileBindingInfo(string Alias, EtlFileRole Role);

internal sealed class EtlBindingException(string code, string safeMessage, Exception? innerException = null)
    : Exception(safeMessage, innerException)
{
    public string Code { get; } = code;
}
