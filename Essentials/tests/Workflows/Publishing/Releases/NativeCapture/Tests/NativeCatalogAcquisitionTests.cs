using System.Text.Json.Nodes;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Capture.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Publishing.SourceCapture;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Publishing.Releases.NativeCapture.Tests;

public sealed class NativeCatalogAcquisitionTests
{
    [Fact]
    public async Task Actual_source_owned_publication_and_compiler_feed_original_catalog_evidence_without_rebuild()
    {
        await using var f = await PublishedNativeFixture.Create(); var childVersion = await f.PublishClrCatalog(); var version = await f.PublishReusableGraph(childVersion); await f.SeedClrWorkflow(version);
        await f.Publish("actual-clr-workflow-version"); var before = await f.Service<ActivitiesDesignDbContext>().ActivityDefinitionVersionPublications.AsNoTracking().SingleAsync(value => value.DefinitionVersionId == version);
        await f.Source().ExecuteAsync([new(NativeReleaseCaptureSelections.WorkflowDefinitionVersion, "actual-clr-workflow-version", null, ReleaseRootAction.ActivateServing)], new(), async (root, token) =>
        {
            var containers = root.Content.Members.Where(member => member.Key.Kind == ReleaseRuntimeCodecs.WorkflowKind).ToArray();
            await f.CatalogSource().ExecuteAsync(containers, root.Content, new(), async (additions, inner) =>
            {
                var member = Assert.Single(additions.Members.Where(value => value.Key == new ReleaseMemberKey(ReleaseRuntimeCodecs.ActivityVersionKind, version)));
                Assert.Equal(2, additions.Members.Count(value => value.Key.Kind == ReleaseRuntimeCodecs.ActivityVersionKind));
                Assert.Equal(2, member.Codec.CodecVersion); var decoded = f.Codec.DecodeActivityVersion(additions.GetPayload(member.Key));
                Assert.NotNull(decoded.Publication); Assert.True(decoded.Publication.OwnerTenantKnown); Assert.Null(decoded.Publication.OwnerTenantId);
                Assert.Equal(version, decoded.VersionId); Assert.Equal(before.TemplateHash, decoded.TemplateHash); Assert.Equal(before.ProviderFingerprint, decoded.CompilerFingerprint);
                var reader = new Joined(root.Content, additions);
                var verifier = f.ServicesOf<IReleaseArtifactSemanticVerifier>().Single(value => value.SupportedCodecs.Contains(member.Codec));
                var facts = await verifier.VerifyAsync(member, reader.GetPayload(member.Key), reader, inner); Assert.Equal(ReleaseSemanticStatus.Verified, facts.Status);
                var inspector = f.ServicesOf<IReleaseOccurrenceInspector>().Single(value => value.Identity.Name == "elsa.runtime-catalog-release-profile");
                foreach (var site in facts.Sites.Where(site => site.Site.SiteKind == ReleaseRuntimeSiteKinds.PublicContract))
                    Assert.Equal(ReleaseSemanticStatus.Verified, (await inspector.InspectAsync(site, reader, inner)).Status);
                // The Graph provider remains a separately mandatory owner profile.
                Assert.Contains(facts.Sites, site => site.Site.SiteKind == ReleaseRuntimeSiteKinds.Provider && site.Site.Discriminator == "elsa.activity-graph");
                var snapshot = await f.Service<Elsa.Activities.Design.Persistence.Core.Stores.IActivityPublicationSnapshotReader>().FindAsync(version, PublishedNativeFixture.Partition.Value, new(), inner);
                Assert.Equal(snapshot!.GetContractBytes(), System.Text.Encoding.UTF8.GetBytes(decoded.Publication.ContractJson));
                Assert.Equal(snapshot.GetProviderBytes(), System.Text.Encoding.UTF8.GetBytes(decoded.Publication.ProviderJson));
                var graphTemplate = Assert.Single(additions.Members.Where(value => value.Key == new ReleaseMemberKey(ReleaseRuntimeCodecs.TemplateKind, decoded.TemplateId)));
                // Isolate the actual template-dependency route from the workflow placement route.
                var templateOnly = new Joined(root.Content, new Subset(additions, [member.Key, graphTemplate.Key]));
                await f.CatalogSource().ExecuteAsync([graphTemplate], templateOnly, new(), async (children, childToken) =>
                {
                    var childMember = Assert.Single(children.Members.Where(value => value.Key.Kind == ReleaseRuntimeCodecs.ActivityVersionKind));
                    Assert.Equal(childVersion, childMember.Key.Identity); var complete = new Joined(templateOnly, children);
                    var childFacts = await verifier.VerifyAsync(childMember, complete.GetPayload(childMember.Key), complete, childToken);
                    Assert.Equal(ReleaseSemanticStatus.Verified, childFacts.Status);
                    foreach (var site in childFacts.Sites) Assert.Equal(ReleaseSemanticStatus.Verified, (await inspector.InspectAsync(site, complete, childToken)).Status);
                }, inner);
            }, token);
        });
        // This is an actual source/captured-catalog witness, not complete native bundle/import acceptance.
        Assert.Equal(2, await f.Service<ActivitiesDesignDbContext>().ActivityDefinitionVersionPublications.CountAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Catalog_and_template_exports_have_separate_explicit_permissions(bool catalogDenied)
    {
        await using var f = await PublishedNativeFixture.Create(); var childVersion = await f.PublishClrCatalog(); var version = await f.PublishReusableGraph(childVersion); await f.SeedClrWorkflow(version); await f.Publish("actual-clr-workflow-version");
        f.CatalogAuthorization.Allow = !catalogDenied; f.TemplateAuthorization.Allow = catalogDenied;
        await f.Source().ExecuteAsync([new(NativeReleaseCaptureSelections.WorkflowDefinitionVersion, "actual-clr-workflow-version", null, ReleaseRootAction.ActivateServing)], new(), async (root, token) =>
        {
            var error = await Assert.ThrowsAsync<ReleaseCaptureException>(() => f.CatalogSource().ExecuteAsync(root.Content.Members.Where(m => m.Key.Kind == ReleaseRuntimeCodecs.WorkflowKind).ToArray(),
                root.Content, new(), (_, _) => throw new InvalidOperationException("Denied acquisition reached capture."), token).AsTask());
            Assert.Equal("native_catalog_export_forbidden", error.Code);
            Assert.Equal(catalogDenied ? 0 : 1, f.CatalogReads); Assert.Equal(0, f.CatalogTemplateReads);
        });
    }

    [Fact]
    public void Catalog_feature_does_not_select_an_export_policy()
    {
        var services = new ServiceCollection(); var feature = new WorkflowsPublishingReleaseCatalogCaptureFeature(); feature.ConfigureServices(services);
        Assert.Throws<InvalidOperationException>(() => feature.PostConfigureServices(services));
    }
    private sealed class Subset(IReleaseContentReader actual, IReadOnlyList<ReleaseMemberKey> keys) : IReleaseContentReader
    {
        public IReadOnlyList<ReleaseContentMember> Members => keys.Select(actual.GetMember).ToArray();
        public ReleaseContentMember GetMember(ReleaseMemberKey key) => keys.Contains(key) ? actual.GetMember(key) : throw new KeyNotFoundException();
        public ReadOnlyMemory<byte> GetPayload(ReleaseMemberKey key) => keys.Contains(key) ? actual.GetPayload(key) : throw new KeyNotFoundException();
    }
    private sealed class Joined(params IReleaseContentReader[] readers) : IReleaseContentReader
    {
        public IReadOnlyList<ReleaseContentMember> Members => readers.SelectMany(value => value.Members).ToArray();
        public ReleaseContentMember GetMember(ReleaseMemberKey key) => readers.SelectMany(value => value.Members).Single(value => value.Key == key);
        public ReadOnlyMemory<byte> GetPayload(ReleaseMemberKey key)
        { foreach (var reader in readers) if (reader.Members.Any(value => value.Key == key)) return reader.GetPayload(key); throw new KeyNotFoundException(); }
    }
}
