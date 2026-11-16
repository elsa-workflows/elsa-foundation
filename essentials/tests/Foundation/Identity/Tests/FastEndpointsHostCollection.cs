namespace Elsa.Foundation.Identity.Tests;

/// <summary>
/// Serializes every test class that self-hosts FastEndpoints in this assembly.
/// <para>
/// FastEndpoints keeps its serializer configuration in a process-global static
/// (<c>Config.SerOpts.Options</c>). <c>MapFastEndpoints</c> installs a fresh
/// <see cref="System.Text.Json.JsonSerializerOptions"/> into that static and then mutates it
/// (<c>ConfigureSerializer</c> assigns <c>TypeInfoResolver</c>). Every live FastEndpoints host in
/// the process serializes requests and responses through that same static, and System.Text.Json
/// freezes an options instance the first time it is used. So a host that serves a single request
/// while another host sits between the install and the mutation freezes the very instance that
/// second host is about to configure, and its startup dies with
/// "This JsonSerializerOptions instance is read-only or has already been used in serialization or
/// deserialization." The same static holds the security options (<c>Config.SecOpts</c>), which the
/// hosts here configure with different claim types.
/// </para>
/// <para>
/// xUnit parallelizes at the collection level, so one shared collection guarantees no two of these
/// hosts are ever mapping or serving at the same time. Each test still builds and disposes its own
/// host, so per-test isolation of the identity stores is unchanged. Any new test class that maps
/// FastEndpoints belongs in this collection.
/// </para>
/// </summary>
[CollectionDefinition(Name)]
public sealed class FastEndpointsHostCollection
{
    public const string Name = "fastendpoints-host";
}
