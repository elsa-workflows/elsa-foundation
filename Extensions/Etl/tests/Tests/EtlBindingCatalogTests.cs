using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Bindings;

namespace Elsa.Etl.Tests;

public sealed class EtlBindingCatalogTests
{
    [Fact]
    public void Binding_pages_use_ordinal_alias_role_boundaries_and_only_return_safe_current_scope_metadata()
    {
        var scope = new EtlScope("tenant-a", "partition-a");
        var bindings = new HostFileBindings(
        [
            Binding(scope, "alpha", EtlFileRole.Source, "source-v1-retained", isCurrent: false),
            Binding(scope, "alpha", EtlFileRole.Source, "source-v2-current", isCurrent: true),
            Binding(scope, "alpha", EtlFileRole.Lookup, "lookup-current", isCurrent: true),
            Binding(scope, "alpha", EtlFileRole.MainOutput, "output-current", isCurrent: true),
            Binding(scope, "Alpha", EtlFileRole.Source, "uppercase-current", isCurrent: true),
            Binding(scope, "beta", EtlFileRole.Source, "beta-current", isCurrent: true),
            Binding(new EtlScope("tenant-b", "partition-a"), "other-scope", EtlFileRole.MainOutput, "hidden-current", isCurrent: true)
        ]);

        var firstPage = bindings.ListBindings(scope, afterAlias: null, afterRole: null, take: 2);
        Assert.Equal(
            new[] { new EtlBindingSummary("Alpha", "Source"), new EtlBindingSummary("alpha", "Lookup") },
            firstPage);

        var secondPage = bindings.ListBindings(scope, firstPage[^1].Alias, firstPage[^1].Role, take: 2);
        Assert.Equal(
            new[] { new EtlBindingSummary("alpha", "MainOutput"), new EtlBindingSummary("alpha", "Source") },
            secondPage);

        var thirdPage = bindings.ListBindings(scope, secondPage[^1].Alias, secondPage[^1].Role, take: 2);
        Assert.Equal(new[] { new EtlBindingSummary("beta", "Source") }, thirdPage);
        Assert.DoesNotContain(firstPage.Concat(secondPage).Concat(thirdPage), item => item.Alias == "other-scope");
        Assert.Equal(1, firstPage.Concat(secondPage).Concat(thirdPage).Count(item => item.Alias == "alpha" && item.Role == "Source"));

        var safeJson = JsonSerializer.Serialize(firstPage.Concat(secondPage).Concat(thirdPage));
        Assert.DoesNotContain(Path.GetTempPath(), safeJson, StringComparison.Ordinal);
        Assert.DoesNotContain("source-v1-retained", safeJson, StringComparison.Ordinal);
        Assert.DoesNotContain("source-v2-current", safeJson, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('a', 64), safeJson, StringComparison.Ordinal);
        Assert.Equal(new[] { "Alias", "Role" }, typeof(EtlBindingSummary).GetProperties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
    }

    private static HostFileBinding Binding(EtlScope scope, string alias, EtlFileRole role, string revision, bool isCurrent)
    {
        var input = role is EtlFileRole.Source or EtlFileRole.Lookup;
        return new HostFileBinding(
            scope,
            alias,
            role,
            revision,
            Path.Combine(Path.GetTempPath(), "etl-binding-catalog-private", $"{alias}-{revision}.csv"),
            isCurrent,
            input ? new string('a', 64) : null,
            ImmutableInput: input,
            AtomicPublication: !input);
    }
}
