using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Serialization.Core;
using Elsa.Serialization.Testing;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Reconciliation.Git.Options;
using Elsa.Workflows.Design.Reconciliation.Git.Services;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation.Git;

// Uses the exact state received from the composed API probe, through the real Git exporter.
public sealed class S9BackendQuickstartGitProbe : GitIntegrationTest
{
    [Fact]
    public async Task API_saved_definition_exports_its_variable_default_without_rewriting_tokens()
    {
        var evidence = Environment.GetEnvironmentVariable("S9_BACKEND_QUICKSTART_ROOT")!;
        var saved = await File.ReadAllTextAsync(Path.Join(evidence, "saved-state.json"));
        var serializer = SerializationServices.PayloadSerializer();
        // WorkflowsDesignJsonOptions uses this authored wire enum vocabulary. The payload
        // serializer is the real Git writer, not the reader for an API response.
        var wireOptions = JsonBaseline.CreateCopy();
        wireOptions.Converters.Add(new JsonStringEnumConverter<AuthoredWorkflowIntrinsicKind>(JsonNamingPolicy.CamelCase));
        var state = JsonSerializer.Deserialize<WorkflowDefinitionState>(saved, wireOptions)!;
        const string raw = """{ "UpperKey" : "<", "N":1.0, "Name":"Ren\u00e9" }""";
        Assert.Equal(raw, Assert.Single(state.Variables).Default!.Value!.Value.GetRawText());
        var remote = GitTestSupport.CreateRemoteWithMain(_git, _tempPaths);
        var cache = GitTestSupport.NewCachePath();
        _tempPaths.Add(cache);
        var options = GitTestSupport.Options(new GitReconciliationOptions
        {
            RemoteUrl = remote, Branch = "main", WorkflowsPath = "workflows",
            LocalCachePath = cache, Role = GitReconciliationRole.Writer
        });
        var exporter = new GitWorkflowExporter(GitTestSupport.Workspace(_git, options), _git, serializer,
            new InMemoryDefinitionStore().With(new WorkflowDefinition { Id = "s9-quickstart", Name = "S-9 backend quickstart" }),
            new InMemoryVersionStore().With(new WorkflowDefinitionVersion("s9-quickstart", "1.0.0") { State = state }),
            options, CredentialLiteralTestSupport.Validator(CredentialLiteralTestSupport.Catalog()), NullLogger<GitWorkflowExporter>.Instance);
        await exporter.ExportAsync(CancellationToken.None);
        var exported = await File.ReadAllTextAsync(Path.Join(cache, "workflows", "s9-quickstart", "versions", "1.0.0.json"));
        await File.WriteAllTextAsync(Path.Join(evidence, "exported-definition.json"), exported);
        using var document = JsonDocument.Parse(exported);
        Assert.Equal(raw, document.RootElement.GetProperty("variables")[0].GetProperty("default").GetProperty("value").GetRawText());
        await File.WriteAllTextAsync(Path.Join(evidence, "quickstart-git-aggregate.json"),
            "{\"apiSavedStateExported\":true,\"gitRawDefault\":true}");
    }
}
