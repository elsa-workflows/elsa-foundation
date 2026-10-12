using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

/// <summary>
/// Serializes every test class that self-hosts FastEndpoints in this assembly.
/// <para>
/// FastEndpoints keeps its serializer and security configuration in process-global statics
/// (<c>Config.SerOpts</c> / <c>Config.SecOpts</c>). <c>UseFastEndpoints</c> installs a fresh
/// <see cref="System.Text.Json.JsonSerializerOptions"/> into that static and then mutates it, while
/// every live FastEndpoints host in the process serializes through the same static — and
/// System.Text.Json freezes an options instance the first time it is used. A host that serves a
/// request while another host sits between the install and the mutation therefore kills that host's
/// startup with "This JsonSerializerOptions instance is read-only or has already been used in
/// serialization or deserialization."
/// </para>
/// <para>
/// xUnit parallelizes at the collection level, so one shared collection keeps these hosts off each
/// other. Any new test class that maps FastEndpoints belongs in it.
/// </para>
/// </summary>
[CollectionDefinition(Name)]
public sealed class FastEndpointsHostCollection
{
    public const string Name = "fastendpoints-host";
}
