using Elsa3.Mapping.Resolution;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 236 US3 (FR-007, FR-009, FR-010): an Elsa 4 CLR activity marked <c>[ReplacesActivityType]</c> is found for
/// that Elsa 3 type through the catalog CLR reconciliation builds, without loading assemblies.
/// </summary>
public sealed class Elsa3ReplacedActivityTypeTests
{
    private static readonly string SendInvoiceKey = typeof(SendInvoiceActivity).FullName!;

    [Fact]
    public async Task A_rebuilt_activity_is_found_for_the_elsa3_type_it_replaces()
    {
        var catalog = RealActivityCatalog.CreateWith(typeof(SendInvoiceActivity).Assembly);
        var invoice = Activity("invoice", "Acme.SendInvoice", ("customer", Literal("c-1")), ("AMOUNT", Literal("10", "Decimal")));

        var result = await ConversionServices.ConvertAsync(catalog, ConversionServices.Definition(Sequence("root", invoice)));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var node = Node(result.Value!.State, "invoice");
        Assert.Equal(catalog.VersionIdOf(SendInvoiceKey), node.ActivityVersionId);
        // Properties bind to same-named members, ignoring case (FR-010).
        Assert.Equal("c-1", Text(Input(node, "Customer")));
        Assert.Equal("10", Text(Input(node, "Amount")));
    }

    [Fact]
    public async Task Two_claimants_make_the_alias_ambiguous()
    {
        var catalog = RealActivityCatalog.CreateWith(typeof(SendInvoiceActivity).Assembly);

        var result = await ConversionServices.ConvertAsync(catalog, ConversionServices.Definition(Sequence("root", Activity("shared", "Acme.Shared"))));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(Elsa3ActivityDiagnosticCodes.AmbiguousAlias, diagnostic.Code);
        Assert.Contains(Elsa.Activities.Design.Core.Reconciliation.ActivityCatalogStableIds.DefinitionId(typeof(FirstSharedClaimActivity).FullName!)!, diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains(Elsa.Activities.Design.Core.Reconciliation.ActivityCatalogStableIds.DefinitionId(typeof(SecondSharedClaimActivity).FullName!)!, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_removed_claimant_no_longer_makes_the_alias_ambiguous()
    {
        var catalog = RealActivityCatalog.CreateWith(typeof(SendInvoiceActivity).Assembly)
            .WithRemoved(typeof(SecondSharedClaimActivity).FullName!);

        var result = await ConversionServices.ConvertAsync(catalog, ConversionServices.Definition(Sequence("root", Activity("shared", "Acme.Shared"))));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(catalog.VersionIdOf(typeof(FirstSharedClaimActivity).FullName!), Node(result.Value!.State, "shared").ActivityVersionId);
    }

    [Fact]
    public async Task A_newer_version_without_the_declaration_ends_the_alias()
    {
        var catalog = RealActivityCatalog.CreateWith(typeof(SendInvoiceActivity).Assembly)
            .WithVersionWithoutFacet(SendInvoiceKey, "999.0.0", Elsa3ReplacedActivityTypeIndex.FacetKind);

        var result = await ConversionServices.ConvertAsync(catalog, ConversionServices.Definition(Sequence("root", Activity("invoice", "Acme.SendInvoice"))));

        Assert.Equal(Elsa3ActivityDiagnosticCodes.UnmappedType, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public async Task A_full_elsa4_type_key_resolves_without_a_declaration()
    {
        var catalog = RealActivityCatalog.CreateWith(typeof(SendInvoiceActivity).Assembly);

        var result = await ConversionServices.ConvertAsync(catalog, ConversionServices.Definition(Sequence("root", Activity("invoice", SendInvoiceKey))));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(catalog.VersionIdOf(SendInvoiceKey), Node(result.Value!.State, "invoice").ActivityVersionId);
    }

    [Fact]
    public async Task The_built_in_map_wins_over_an_alias()
    {
        var catalog = RealActivityCatalog.CreateWith(typeof(SendInvoiceActivity).Assembly);

        var result = await ConversionServices.ConvertAsync(catalog, ConversionServices.Definition(
            Sequence("root", Activity("write", "Elsa.WriteLine", ("text", Literal("hi"))))));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal(catalog.VersionIdOf("Elsa.Activities.Primitives.Activities.WriteLine"), Node(result.Value!.State, "write").ActivityVersionId);
    }
}
