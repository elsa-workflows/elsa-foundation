using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Elsa.Workflows.Design.Validations.Core.Exceptions;
using Xunit;
using static Elsa.Workflows.Design.Tests.Infrastructure.CredentialLiteralTestSupport;

namespace Elsa.Workflows.Design.Tests.Unit.CredentialLiteral;

/// <summary>
/// Spec 188, FR-008 at the five Design API callers that take incoming state (draft save through Definitions/Add,
/// Drafts/Replace and Definitions/Update, then Versions/Add and Definitions/Submit), over the real EF store: a literal on
/// a credential input is refused before the command runs, so no row is written and no stored state holds the literal;
/// a secret reference, an empty literal and a literal on a sensitive input that is not a credential are stored. A literal
/// on an activity the catalog does not hold cannot be judged and is stored (the residual, research R7), which publish
/// then refuses (T112). The 400 the refusal maps to is pinned over HTTP in <c>CredentialLiteralEndpointTests</c>.
/// </summary>
public sealed class CredentialLiteralStorageTests(CredentialLiteralStorageTests.SeededHost fixture)
    : IClassFixture<CredentialLiteralStorageTests.SeededHost>
{
    public static TheoryData<string> EntryPoints => new() { "DefinitionsAdd", "DraftsReplace", "DefinitionsUpdate", "VersionsAdd", "DefinitionsSubmit" };

    public static TheoryData<string, string> AcceptedBindings
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var entryPoint in EntryPoints)
            foreach (var binding in new[] { "Secret", "EmptyLiteral" })
                data.Add(entryPoint, binding);
            return data;
        }
    }

    private DesignAdmissionTestHost Host => fixture.Host;

    [Theory]
    [MemberData(nameof(EntryPoints))]
    public async Task A_literal_on_a_credential_input_is_refused_and_nothing_is_stored(string entryPoint)
    {
        // A literal of this row's own, so the storage scan judges this entry point alone in the shared host.
        var literal = $"refused-{Guid.NewGuid():N}";
        var rowsBefore = await Host.CountRowsAsync();

        var refusal = await Assert.ThrowsAsync<CredentialLiteralRefusedException>(() =>
            SaveAsync(entryPoint, State(ActivityVersionId, Bind(CredentialKey, "Literal", literal))));

        var finding = Assert.Single(refusal.Findings);
        Assert.Equal($"{NodeId}/inputs/{CredentialKey}", finding.Path);
        Assert.StartsWith("Inputs/CredentialLiteral", finding.Message, StringComparison.Ordinal);
        Assert.Equal(rowsBefore, await Host.CountRowsAsync());
        Assert.False(await Host.AnyStoredStateContainsAsync(literal));
    }

    [Theory]
    [MemberData(nameof(AcceptedBindings))]
    public async Task A_secret_reference_or_an_empty_literal_on_a_credential_input_is_accepted(string entryPoint, string binding) =>
        Assert.Null(await Record.ExceptionAsync(() => SaveAsync(entryPoint, CredentialBoundAs(binding))));

    [Theory]
    [MemberData(nameof(EntryPoints))]
    public async Task A_literal_on_a_sensitive_input_that_is_not_a_credential_is_accepted_and_reaches_the_store(string entryPoint)
    {
        // Also the positive control of the storage scan: a literal the rule accepts is found where a refused one is not.
        var literal = $"sensitive-{Guid.NewGuid():N}";

        await SaveAsync(entryPoint, State(ActivityVersionId, Bind(CredentialKey, "Secret"), Bind(SensitiveKey, "Literal", literal)));

        Assert.True(await Host.AnyStoredStateContainsAsync(literal));
    }

    [Theory]
    [InlineData("DraftsReplace")]
    [InlineData("VersionsAdd")]
    public async Task A_literal_on_an_activity_the_catalog_does_not_hold_is_stored_because_it_cannot_be_judged(string entryPoint)
    {
        var literal = $"uncataloged-{Guid.NewGuid():N}";

        await SaveAsync(entryPoint, State(UncatalogedActivityVersionId, Bind(CredentialKey, "Literal", literal)));

        Assert.True(await Host.AnyStoredStateContainsAsync(literal));
    }

    private Task SaveAsync(string entryPoint, WorkflowDefinitionState state) => entryPoint switch
    {
        "DefinitionsAdd" => Host.AddDefinitionAsync(state),
        "DraftsReplace" => Host.ReplaceDraftAsync(fixture.DraftId, state),
        "DefinitionsUpdate" => Host.UpdateDefinitionAsync(fixture.DefinitionId, state),
        "VersionsAdd" => Host.AddVersionAsync(fixture.DefinitionId, state),
        "DefinitionsSubmit" => Host.SubmitAsync(state),
        _ => throw new ArgumentOutOfRangeException(nameof(entryPoint), entryPoint, null)
    };

    /// <summary>One host for the class, with a definition and its draft to save into.</summary>
    public sealed class SeededHost : IAsyncLifetime
    {
        internal DesignAdmissionTestHost Host { get; private set; } = null!;
        public string DefinitionId { get; private set; } = null!;
        public string DraftId { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            Host = await DesignAdmissionTestHost.CreateAsync();
            var seeded = await Host.AddDefinitionAsync(CredentialBoundAs("Secret"));
            DefinitionId = seeded.Definition.Id;
            DraftId = seeded.Draft!.Id;
        }

        public async Task DisposeAsync() => await Host.DisposeAsync();
    }
}
