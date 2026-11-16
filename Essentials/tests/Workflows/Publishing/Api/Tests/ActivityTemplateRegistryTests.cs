using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Services;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class ActivityTemplateRegistryTests
{
    [Fact]
    public async Task Compiler_and_discovery_guards_use_only_the_registered_public_guidance()
    {
        var declaration = new ActivityProviderDiagnosticDescriptor("guided.model.required", "Choose a model connection.", "Select a configured connection in Designer.", "/model", "Choose model connection");
        var declarations = new List<ActivityProviderDiagnosticDescriptor> { declaration };
        var provider = new GuidedProvider(declarations);
        var compilers = new ActivityTemplateProviderCompilerRegistry([provider]);
        var discoverers = new ActivityTemplateDependencyDiscovererRegistry([provider]);
        declarations[0] = declaration with { Message = "PRIVATE changed after registration" };
        var manifest = new ActivityProviderManifest("guided", "1", System.Text.Json.JsonSerializer.SerializeToElement(new { }));
        var discovered = await discoverers.Resolve("guided", "1").DiscoverDependenciesAsync(new("definition", "draft", 1, manifest));
        var compiled = await compilers.Resolve("guided", "1").CompileAsync(new("definition", "Acme.Agent", "draft", 1, "1.0.0", new("1", [], [], []), manifest, [], "guided/compiler/1"));
        foreach (var diagnostic in new[] { Assert.Single(discovered.Diagnostics), Assert.Single(compiled.Diagnostics) })
        {
            Assert.Equal(declaration.Message, diagnostic.Message);
            Assert.Equal(declaration.Remediation, diagnostic.Remediation);
            Assert.Equal("/model", diagnostic.Location!.JsonPointer);
            Assert.Equal("Choose model connection", diagnostic.Metadata["actionLabel"]);
            Assert.DoesNotContain("PRIVATE", System.Text.Json.JsonSerializer.Serialize(diagnostic));
        }
    }

    [Fact]
    public void Compiler_claims_are_preflighted_atomically()
    {
        var registry = new ActivityTemplateProviderCompilerRegistry([new Compiler("provider", new HashSet<string> { "2" })]);

        Assert.Throws<InvalidOperationException>(() => registry.Add(new Compiler("provider", new HashSet<string> { "1", "2" })));

        Assert.False(registry.TryResolve("provider", "1", out _));
        Assert.True(registry.TryResolve("provider", "2", out _));
    }

    [Fact]
    public async Task Dependency_discoverer_failures_become_ordered_safe_diagnostics_and_requested_cancellation_escapes()
    {
        var registry = new ActivityTemplateDependencyDiscovererRegistry([new ThrowingDiscoverer()]);
        var discoverer = registry.Resolve("provider", "1");
        var request = new ActivityTemplateDependencyDiscoveryRequest(
            "definition", "draft", 1, new("provider", "1", System.Text.Json.JsonSerializer.SerializeToElement(new { })));

        var failed = await discoverer.DiscoverDependenciesAsync(request);
        Assert.Equal("activity.provider.failure", Assert.Single(failed.Diagnostics).Code);
        Assert.DoesNotContain("secret", failed.Diagnostics[0].Message, StringComparison.OrdinalIgnoreCase);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => discoverer.DiscoverDependenciesAsync(request, cancellation.Token).AsTask());
    }

    [Theory]
    [InlineData(null, "ActivityDraft")]
    [InlineData("ActivitySourceVersion", "ActivitySourceVersion")]
    public async Task A_provider_failure_names_the_subject_kind_the_request_names(string? subjectKind, string expected)
    {
        var manifest = new ActivityProviderManifest("provider", "1", System.Text.Json.JsonSerializer.SerializeToElement(new { }));
        var discovery = new ActivityTemplateDependencyDiscoveryRequest("definition", "version-1", 0, manifest);
        var compilation = new ActivityTemplateCompilationRequest("definition", "Acme.Send", "version-1", 0, "1.0.0",
            new("1", [], [], []), manifest, [], "provider/compiler/1");
        if (subjectKind is not null)
        {
            discovery = discovery with { SubjectKind = subjectKind };
            compilation = compilation with { SubjectKind = subjectKind };
        }

        var discovered = await new ActivityTemplateDependencyDiscovererRegistry([new ThrowingDiscoverer()])
            .Resolve("provider", "1").DiscoverDependenciesAsync(discovery);
        var compiled = await new ActivityTemplateProviderCompilerRegistry([new ThrowingCompiler()])
            .Resolve("provider", "1").CompileAsync(compilation);

        foreach (var diagnostic in new[] { Assert.Single(discovered.Diagnostics), Assert.Single(compiled.Diagnostics) })
        {
            Assert.Equal("activity.provider.failure", diagnostic.Code);
            Assert.Equal(expected, diagnostic.Subject.Kind);
            Assert.Equal("version-1", diagnostic.Subject.Id);
        }
    }

    [Fact]
    public async Task Provider_diagnostics_cannot_disclose_messages_subjects_paths_remediation_or_metadata()
    {
        var registry = new ActivityTemplateDependencyDiscovererRegistry([new DisclosingDiscoverer()]);
        var result = await registry.Resolve("provider", "1").DiscoverDependenciesAsync(new(
            "definition", "draft", 1, new("provider", "1", System.Text.Json.JsonSerializer.SerializeToElement(new { }))));

        var serialized = System.Text.Json.JsonSerializer.Serialize(Assert.Single(result.Diagnostics));
        Assert.DoesNotContain("secret", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tenant-private", serialized, StringComparison.Ordinal);
        Assert.Contains("provider.validation.failed", serialized, StringComparison.Ordinal);
    }

    private sealed class GuidedProvider(IReadOnlyList<ActivityProviderDiagnosticDescriptor> declarations) : IActivityTemplateProviderCompiler, IActivityTemplateDependencyDiscoverer, IActivityProviderDiagnosticCatalog
    {
        public string ProviderKey => "guided";
        public string CompilerFingerprint => "guided/compiler/1";
        public IReadOnlySet<string> SupportedManifestSchemas { get; } = new HashSet<string> { "1" };
        public IReadOnlyList<ActivityProviderDiagnosticDescriptor> DiagnosticDescriptors => declarations;
        private static ActivityDiagnostic Diagnostic() => new("guided.model.required", ActivityDiagnosticSeverity.Error,
            "PRIVATE runtime message", new("PRIVATE", "PRIVATE"), new("PRIVATE", "/PRIVATE"), "PRIVATE remediation",
            new Dictionary<string, string> { ["actionLabel"] = "PRIVATE" });
        public ValueTask<ActivityTemplateDependencyDiscovery> DiscoverDependenciesAsync(ActivityTemplateDependencyDiscoveryRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityTemplateDependencyDiscovery([], [Diagnostic()]));
        public ValueTask<ActivityTemplateCompilation> CompileAsync(ActivityTemplateCompilationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityTemplateCompilation(null, new Dictionary<string, Elsa.Workflows.Runtime.Core.Models.WorkflowExecutableResumeTarget>(), [], [], [], new(0, 0, 0, 0, 0, 0, 0), CompilerFingerprint, [], [Diagnostic()]));
    }

    private sealed class Compiler(string providerKey, IReadOnlySet<string> schemas) : IActivityTemplateProviderCompiler
    {
        public string ProviderKey => providerKey;
        public string CompilerFingerprint => $"{providerKey}/compiler/1";
        public IReadOnlySet<string> SupportedManifestSchemas => schemas;
        public ValueTask<ActivityTemplateCompilation> CompileAsync(ActivityTemplateCompilationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingCompiler : IActivityTemplateProviderCompiler
    {
        public string ProviderKey => "provider";
        public string CompilerFingerprint => "provider/compiler/1";
        public IReadOnlySet<string> SupportedManifestSchemas { get; } = new HashSet<string> { "1" };
        public ValueTask<ActivityTemplateCompilation> CompileAsync(ActivityTemplateCompilationRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("secret provider detail");
    }

    private sealed class ThrowingDiscoverer : IActivityTemplateDependencyDiscoverer
    {
        public string ProviderKey => "provider";
        public IReadOnlySet<string> SupportedManifestSchemas { get; } = new HashSet<string> { "1" };
        public ValueTask<ActivityTemplateDependencyDiscovery> DiscoverDependenciesAsync(ActivityTemplateDependencyDiscoveryRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("secret provider detail");
        }
    }

    private sealed class DisclosingDiscoverer : IActivityTemplateDependencyDiscoverer
    {
        public string ProviderKey => "provider";
        public IReadOnlySet<string> SupportedManifestSchemas { get; } = new HashSet<string> { "1" };

        public ValueTask<ActivityTemplateDependencyDiscovery> DiscoverDependenciesAsync(
            ActivityTemplateDependencyDiscoveryRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityTemplateDependencyDiscovery([], [new(
                "provider.validation.failed",
                ActivityDiagnosticSeverity.Error,
                "secret manifest payload",
                new("ActivityDraft", "tenant-private"),
                new("provider", "/secret/expression"),
                "Paste the secret into a log.",
                new Dictionary<string, string> { ["secret"] = "tenant-private" })]));
    }
}
