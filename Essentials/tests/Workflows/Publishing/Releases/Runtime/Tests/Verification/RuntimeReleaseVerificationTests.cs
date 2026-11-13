using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Publishing.Releases.Runtime.Tests.Verification;

public sealed class RuntimeReleaseVerificationTests
{
    [Fact]
    public async Task Native_identity_hash_and_selected_association_keep_deduplicated_producer_separate()
    {
        var fixture = new RuntimeReleaseFixture(); var workflow = RuntimeReleaseFixture.Workflow();
        var artifact = fixture.Add(fixture.Codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, workflow)));
        var selection = fixture.Add(fixture.Codec.EncodeWorkflowSelection(RuntimeReleaseFixture.Selection(workflow)));
        var facts = await fixture.Verify(selection);
        Assert.Equal(ReleaseSemanticStatus.Verified, facts.Status);
        Assert.Equal("selected-version", facts.Identity!.Identity);
        Assert.Equal("2.0.0", facts.OwnerVersion!.TextValue);
        Assert.NotEqual("selected-version", workflow.Identity.DefinitionVersionId);
        Assert.Equal(artifact.Key, Assert.Single(facts.RootCapabilities).Content);
        Assert.Empty((await fixture.Verify(artifact)).RootCapabilities);
        using var provider = Services().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }); using var scope = provider.CreateScope();
        var transport = await fixture.Transport(scope.ServiceProvider.GetRequiredService<IReleaseBundleCodec>(), selection.Key, artifact.Key);
        var result = await scope.ServiceProvider.GetRequiredService<IReleaseSemanticVerifier>().VerifyAsync(transport);
        Assert.Equal(ReleaseSemanticStatus.Verified, result.Status);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("definition")]
    [InlineData("version")]
    [InlineData("artifact-version")]
    [InlineData("artifact-id")]
    [InlineData("scope")]
    [InlineData("test-only")]
    public async Task Selected_association_requires_full_five_field_pin_source_scope_and_published_reference(string change)
    {
        var fixture = new RuntimeReleaseFixture(); var workflow = RuntimeReleaseFixture.Workflow();
        fixture.Add(fixture.Codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, workflow)));
        var selection = RuntimeReleaseFixture.Selection(workflow);
        selection = change switch
        {
            "hash" => selection with { Artifact = selection.Artifact with { ArtifactHash = "sha256:" + new string('0', 64) } },
            "definition" => selection with { Artifact = selection.Artifact with { DefinitionId = "another-producer" } },
            "version" => selection with { Artifact = selection.Artifact with { DefinitionVersionId = "another-producer-version" } },
            "artifact-version" => selection with { Artifact = selection.Artifact with { ArtifactVersion = "2.0.0" } },
            "artifact-id" => selection with { Artifact = selection.Artifact with { ArtifactId = "another-artifact" } },
            "scope" => selection with { Scope = RuntimeReleaseFixture.Scope with { Authority = "another-authority" } },
            _ => selection with { ReferenceScope = WorkflowExecutableReferenceScope.TestRun }
        };
        var member = fixture.Add(fixture.Codec.EncodeWorkflowSelection(selection));
        Assert.Equal(ReleaseSemanticStatus.Invalid, (await fixture.Verify(member)).Status);
    }

    [Fact]
    public async Task Native_owner_hash_is_verified_independently_of_the_blob_digest()
    {
        var fixture = new RuntimeReleaseFixture(); var original = RuntimeReleaseFixture.Workflow();
        var tampered = RuntimeReleaseFixture.Workflow(identity: original.Identity with { ArtifactHash = "sha256:" + new string('0', 64) });
        var member = fixture.Add(fixture.Codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, tampered)));
        var facts = await fixture.Verify(member);
        Assert.Equal(ReleaseSemanticStatus.Invalid, facts.Status);
        Assert.Contains(facts.Diagnostics, x => x.Code == "invalid_native_identity_or_hash");
    }

    [Fact]
    public async Task Generic_native_dependencies_are_emitted_once_and_full_node_context_retains_module_metadata()
    {
        var fixture = new RuntimeReleaseFixture(); var model = RuntimeReleaseFixture.Model();
        var modelMember = fixture.Add(fixture.Codec.EncodeModel(new(RuntimeReleaseFixture.Scope, model)));
        var root = RuntimeReleaseFixture.Node(consumer: "fixture.consumer", payload: RuntimeReleaseFixture.Json("{\"opaque\":true}"), intrinsic: null,
            metadata: new Dictionary<string, string> { ["bpmn.model.v1"] = "untouched-source-locator" });
        var workflow = RuntimeReleaseFixture.Workflow(root, [new(model.Identity.ArtifactId, model.Identity.ArtifactHash, [root.ExecutableNodeId])]);
        var member = fixture.Add(fixture.Codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, workflow)));
        var facts = await fixture.Verify(member);
        Assert.Equal(ReleaseSemanticStatus.Verified, facts.Status);
        Assert.Equal(modelMember.Key, Assert.Single(facts.Edges).To);
        Assert.Contains(facts.Sites, x => x.Site.SiteKind == ReleaseRuntimeSiteKinds.Metadata && x.Site.Discriminator == "bpmn.model.v1");
        var context = new ReleaseRuntimeSiteReader(fixture.Cache).ReadNode(facts.Sites[0], fixture);
        Assert.Equal("untouched-source-locator", context.Node.Metadata["bpmn.model.v1"]);
        Assert.Single(context.Workflow!.Dependencies);
        Assert.Empty(facts.RootCapabilities);
    }

    [Fact]
    public async Task Neutral_model_verifier_emits_exact_protocol_site_and_grants_no_root_capability()
    {
        var fixture = new RuntimeReleaseFixture(); var model = RuntimeReleaseFixture.Model("elsa.bpmn", 1);
        var member = fixture.Add(fixture.Codec.EncodeModel(new(RuntimeReleaseFixture.Scope, model)));
        var facts = await fixture.Verify(member);
        Assert.Equal(ReleaseSemanticStatus.Verified, facts.Status);
        var site = Assert.Single(facts.Sites); Assert.Equal(new ReleaseSiteKey(ReleaseRuntimeSiteKinds.ModelProtocol, "elsa.bpmn", 1), site.Site);
        Assert.Empty(facts.RootCapabilities);
        Assert.Equal("fixture-profile", new ReleaseRuntimeSiteReader(fixture.Cache).ReadModel(site, fixture).Model.Profile);
    }

    [Fact]
    public async Task Installed_runtime_consumer_does_not_make_unclaimed_module_sites_complete()
    {
        var fixture = new RuntimeReleaseFixture();
        var workflow = RuntimeReleaseFixture.Workflow(RuntimeReleaseFixture.Node(consumer: "installed-but-uninspected", payload: RuntimeReleaseFixture.Json("{}"), intrinsic: null));
        var artifact = fixture.Add(fixture.Codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, workflow)));
        var selected = fixture.Add(fixture.Codec.EncodeWorkflowSelection(RuntimeReleaseFixture.Selection(workflow)));
        using var provider = Services().BuildServiceProvider(); using var scope = provider.CreateScope();
        var transport = await fixture.Transport(scope.ServiceProvider.GetRequiredService<IReleaseBundleCodec>(), selected.Key, artifact.Key);
        var result = await scope.ServiceProvider.GetRequiredService<IReleaseSemanticVerifier>().VerifyAsync(transport);
        Assert.Equal(ReleaseSemanticStatus.Unsupported, result.Status);
        Assert.Contains(result.Diagnostics, x => x.Code == "unsupported_occurrence_site");
    }

    [Fact]
    public async Task Template_direct_dependencies_cannot_omit_the_actual_closed_dag()
    {
        var fixture = new RuntimeReleaseFixture(); var child = RuntimeReleaseFixture.Template();
        fixture.Add(fixture.Codec.EncodeTemplate(new(RuntimeReleaseFixture.Scope, child)));
        fixture.Add(fixture.Codec.EncodeActivityVersion(RuntimeReleaseFixture.ActivityVersion(child)));
        var parent = RuntimeReleaseFixture.Template(dependencies: [new("activity-definition", "activity-version", "1.0.0", child.TemplateId, child.TemplateHash, "call", ActivityInvocationOrigin.Empty)], closed: []);
        var member = fixture.Add(fixture.Codec.EncodeTemplate(new(RuntimeReleaseFixture.Scope, parent)));
        var facts = await fixture.Verify(member);
        Assert.Equal(ReleaseSemanticStatus.Invalid, facts.Status);
        Assert.Contains(facts.Diagnostics, x => x.Code == "template_closed_dag_mismatch");
    }

    [Fact]
    public async Task Activity_projection_and_template_overlay_keep_mandatory_provider_and_occurrence_inspections()
    {
        var fixture = new RuntimeReleaseFixture(); var child = RuntimeReleaseFixture.Template();
        fixture.Add(fixture.Codec.EncodeTemplate(new(RuntimeReleaseFixture.Scope, child)));
        var activity = fixture.Add(fixture.Codec.EncodeActivityVersion(RuntimeReleaseFixture.ActivityVersion(child)));
        var publicationFacts = await fixture.Verify(activity);
        Assert.Contains(publicationFacts.Sites, x => x.Site.SiteKind == ReleaseRuntimeSiteKinds.Provider);
        Assert.Contains(publicationFacts.Sites, x => x.Site.SiteKind == ReleaseRuntimeSiteKinds.PublicContract);
        var parent = RuntimeReleaseFixture.Template(dependencies: [new("activity-definition", "activity-version", "1.0.0", child.TemplateId, child.TemplateHash, "call", ActivityInvocationOrigin.Empty,
            OccurrenceOverlay: new("1", RuntimeReleaseFixture.Json("[]"), null))], closed: [new(child.TemplateId, child.TemplateHash)]);
        var member = fixture.Add(fixture.Codec.EncodeTemplate(new(RuntimeReleaseFixture.Scope, parent)));
        var facts = await fixture.Verify(member);
        Assert.Equal(ReleaseSemanticStatus.Verified, facts.Status);
        Assert.Contains(facts.Sites, x => x.Site.SiteKind == ReleaseRuntimeSiteKinds.TemplateOverlay);
        Assert.Equal(2, facts.Edges.Count);
    }

    [Fact]
    public async Task Secret_and_connection_resource_requirements_keep_member_node_and_input_scope()
    {
        var fixture = new RuntimeReleaseFixture(); var stringType = new ValueTypeDescriptor("String");
        var bindings = new Dictionary<string, RuntimeInputBinding>
        {
            ["value"] = new("value", stringType, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.SecretRead, secret: new("source-secret", "api-key", "scope")),
            ["connection"] = new("connection", new("Elsa.Activities.Runtime.Core.Models.ConnectionReference"), ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.Literal, literal: ValueEnvelope.Inline(new("Elsa.Activities.Runtime.Core.Models.ConnectionReference"), RuntimeReleaseFixture.Json("{\"connection\":\"source-name\",\"type\":\"http\"}"), ValueProtectionPolicy.InstanceInline))
        };
        var member = fixture.Add(fixture.Codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, RuntimeReleaseFixture.Workflow(RuntimeReleaseFixture.Node("注文:/node", bindings)))));
        var facts = await fixture.Verify(member);
        Assert.Equal(ReleaseSemanticStatus.Verified, facts.Status);
        Assert.Equal(new[] { "注文:/node", "input", "value" }, facts.Resources.Single(x => x.Kind == "secret").Origin);
        Assert.Equal(new[] { "注文:/node", "input", "connection" }, facts.Resources.Single(x => x.Kind == "connection").Origin);
        Assert.All(facts.Resources, x => Assert.Equal(member.Key, x.Member));
        Assert.Contains(facts.Prerequisites, x => x.Kind == "secret-resolver");
    }

    [Fact]
    public async Task Runtime_profile_preserves_cancellation()
    {
        var fixture = new RuntimeReleaseFixture(); var member = fixture.Add(fixture.Codec.EncodeWorkflow(new(RuntimeReleaseFixture.Scope, RuntimeReleaseFixture.Workflow())));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Verifier.VerifyAsync(member, fixture.GetPayload(member.Key), fixture, cancellation.Token).AsTask());
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [Fact]
    public void Scoped_feature_preserves_replacements_and_rejects_duplicate_or_missing_canonical_contracts()
    {
        var services = Services();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }); using var a = provider.CreateScope(); using var b = provider.CreateScope();
        Assert.NotSame(a.ServiceProvider.GetRequiredService<IReleaseRuntimeArtifactCodec>(), b.ServiceProvider.GetRequiredService<IReleaseRuntimeArtifactCodec>());
        services.AddScoped<IReleaseRuntimeArtifactCodec, ReleaseRuntimeArtifactCodec>();
        Assert.Throws<InvalidOperationException>(() => new WorkflowReleaseRuntimeFeature().PostConfigureServices(services));
        var missing = new ServiceCollection(); var feature = new WorkflowReleaseRuntimeFeature(); feature.ConfigureServices(missing);
        Assert.Throws<InvalidOperationException>(() => feature.PostConfigureServices(missing));
    }

    private static ServiceCollection Services()
    {
        var services = new ServiceCollection();
        var transport = new Elsa.Workflows.Publishing.Releases.WorkflowReleaseTransportFeature(); transport.ConfigureServices(services); transport.PostConfigureServices(services);
        var verification = new Elsa.Workflows.Publishing.Releases.WorkflowReleaseVerificationFeature(); verification.ConfigureServices(services); verification.PostConfigureServices(services);
        services.AddScoped<Elsa.Workflows.Runtime.Core.Contracts.IWorkflowExecutableHasher, Elsa.Workflows.Runtime.Services.Executables.WorkflowExecutableHasher>();
        var runtime = new WorkflowReleaseRuntimeFeature(); runtime.ConfigureServices(services); runtime.PostConfigureServices(services);
        return services;
    }
}
