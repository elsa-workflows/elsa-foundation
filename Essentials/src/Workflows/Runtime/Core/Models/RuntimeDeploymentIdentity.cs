using System.Text;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>An exact environment address. A null tenant is an address, never a global fallback.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RuntimeDeploymentScope
{
    [JsonConstructor]
    public RuntimeDeploymentScope(string authority, string partition, string? tenantId)
    {
        RuntimeDeploymentCaptureGuard.Text(authority, 256); RuntimeDeploymentCaptureGuard.Text(partition, 256);
        RuntimeDeploymentCaptureGuard.OptionalText(tenantId, 256);
        Authority = authority; Partition = partition; TenantId = tenantId;
    }
    public string Authority { get; }
    public string Partition { get; }
    public string? TenantId { get; }
}

/// <summary>An original owner identity, qualified by its explicit source address.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RuntimeDeploymentMember
{
    [JsonConstructor]
    public RuntimeDeploymentMember(string kind, string identity, RuntimeDeploymentScope sourceScope)
    {
        RuntimeDeploymentCaptureGuard.Text(kind, 128); RuntimeDeploymentCaptureGuard.Text(identity, 4096);
        ArgumentNullException.ThrowIfNull(sourceScope); Kind = kind; Identity = identity; SourceScope = sourceScope;
    }
    public string Kind { get; }
    public string Identity { get; }
    public RuntimeDeploymentScope SourceScope { get; }
}

public sealed class RuntimeDeploymentCaptureException(string code, bool isUnsupported = false)
    : Exception("Runtime deployment capture was refused (" + code + ").")
{
    public string Code { get; } = code;
    public bool IsUnsupported { get; } = isUnsupported;
}

internal static class RuntimeDeploymentCaptureGuard
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static void Text(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value != value.Trim() || value.Any(char.IsControl)) throw Fail("runtime_deployment_identity_invalid");
        try { _ = Utf8.GetByteCount(value); }
        catch (EncoderFallbackException) { throw Fail("runtime_deployment_identity_invalid"); }
    }
    public static void OptionalText(string? value, int maximum) { if (value is not null) Text(value, maximum); }
    public static void Digest(string value)
    {
        if (value is not { Length: 71 } || !value.StartsWith("sha256:", StringComparison.Ordinal) ||
            value.AsSpan(7).ContainsAnyExcept("0123456789abcdef")) throw Fail("runtime_deployment_digest_invalid");
    }
    public static void Pin(WorkflowExecutableIdentity pin)
    { Text(pin.ArtifactId, 4096); Text(pin.DefinitionId, 4096); Text(pin.DefinitionVersionId, 4096); Text(pin.ArtifactVersion, 256); Text(pin.ArtifactHash, 256); }
    public static RuntimeDeploymentCaptureException Fail(string code, bool unsupported = false) => new(code, unsupported);
}
