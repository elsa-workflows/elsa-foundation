using Elsa.Integrations.Sdk;

namespace Elsa.Integrations.Tests;

public sealed class ActivitiesOnlyLoadingTests
{
    [Fact]
    public void Sdk_has_no_shell_feature_hosted_service_or_ef_context()
    {
        var types = typeof(ConnectorActivity).Assembly.GetTypes();
        Assert.DoesNotContain(types, type => Implements(type, "IShellFeature"));
        Assert.DoesNotContain(types, type => Implements(type, "IHostedService"));
        Assert.DoesNotContain(types, type => type.Name.EndsWith("DbContext", StringComparison.Ordinal) || type.BaseType?.Name == "DbContext");
        Assert.DoesNotContain(types, type => type.Name is
            "ConnectionReference" or "SensitiveValue" or "ConnectionUnavailableException"
            or "IConnectionLookup" or "IConnectionAuthenticator" or "ConnectionTypeDescriptor"
            or "IntegrationsHttpAuthHandler" or "IntegrationsHttpAuthScope");
    }

    [Fact]
    public void Sdk_does_not_reference_elsa3_or_design_reconciliation()
    {
        var names = typeof(ConnectorActivity).Assembly.GetReferencedAssemblies().Select(name => name.Name).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain(names, name => name is not null && name.StartsWith("Elsa3.", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name is not null && name.StartsWith("Elsa.Activities.Design", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name is not null && name.StartsWith("Elsa.Workflows.Design", StringComparison.Ordinal));
    }

    private static bool Implements(Type type, string interfaceName) =>
        type is { IsClass: true, IsAbstract: false } && type.GetInterfaces().Any(candidate => candidate.Name == interfaceName);
}
