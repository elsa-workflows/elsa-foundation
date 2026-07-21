using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Validations.Handlers;
using Elsa.Workflows.Design.Validations.Validators;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// SC-014 + Unit C FR-024. Validation errors are derived state (no persisted sibling): the
/// promotion gate re-runs the validators in-lock via <c>OnDraftValidating</c> and refuses to
/// promote a Draft whose current state produces any errors, throwing
/// <c>DraftHasValidationErrorsException</c> with the error count. Validator contributions are
/// simulated with the <c>CapturingEventPublisher.OnPublish</c> hook — the project-wide pattern for
/// driving <c>OnDraftValidating</c> without spinning up the full event pipeline.
/// </summary>
public sealed class PromotionGateTests
{
    [Fact]
    public async Task Promotion_throws_when_the_draft_state_produces_validation_errors()
    {
        using var host = WorkflowsDesignTestHost.Create();

        host.EventPublisher.OnPublish = evt =>
        {
            if (evt is OnDraftValidating validating)
            {
                validating.Errors.Add(new ValidationError("$workflow", "RootActivity/Missing", "No root activity"));
                validating.Errors.Add(new ValidationError("n1", "Graph/UnknownActivityVersion", "Unknown activity version"));
            }
        };

        var draftId = await CreateDraft(host);

        using var scope = host.Services.CreateScope();
        var gate = scope.ServiceProvider.GetRequiredService<IPromoteDraftToVersionCommand>();
        var ex = await Assert.ThrowsAsync<DraftHasValidationErrorsException>(() => gate.Execute(WorkflowsDesignTestHost.TestOperationKey, draftId));

        Assert.Equal(draftId, ex.DraftId);
        Assert.Equal(2, ex.ErrorCount);

        // Issue #927: the exception now carries the full derived error list (not just the count) so
        // the Promote endpoint can enrich the 409 ProblemDetails with the actual violations.
        Assert.Equal(2, ex.Errors.Count);
        Assert.Contains(ex.Errors, e => e is { Path: "$workflow", Type: "RootActivity/Missing" });
        Assert.Contains(ex.Errors, e => e is { Path: "n1", Type: "Graph/UnknownActivityVersion" });
    }

    [Fact]
    public async Task Promotion_succeeds_when_the_draft_state_produces_no_errors()
    {
        using var host = WorkflowsDesignTestHost.Create();

        // No OnPublish hook — validators contribute nothing, so the re-run gate finds no errors.
        var draftId = await CreateDraft(host);

        using var scope = host.Services.CreateScope();
        var gate = scope.ServiceProvider.GetRequiredService<IPromoteDraftToVersionCommand>();
        var versionId = await gate.Execute(WorkflowsDesignTestHost.TestOperationKey, draftId);

        Assert.NotNull(versionId);
        Assert.NotEmpty(versionId);

        await using var ctx = host.CreateContext();
        var version = await ctx.WorkflowDefinitionVersions.FirstOrDefaultAsync(x => x.Id == versionId);
        Assert.NotNull(version);
        Assert.Equal("1.0.0", version.Version);
    }

    [Fact]
    public async Task Draft_referencing_unknown_activity_version_cannot_be_promoted()
    {
        // FR-033 2026-07-05 amendment consequence pin: a node whose ActivityVersionId is not in
        // the catalog is a baseline validation error, so the FR-024 gate blocks promotion with a
        // node-addressed error instead of the store's opaque EntityNotFoundException fault. Wires
        // the REAL ExecuteValidations aggregator + UnknownActivityVersionValidator against an
        // empty (throwing, per store contract) catalog; the error's shape is pinned by
        // UnknownActivityVersionValidatorTests — this test pins only the gate consequence.
        using var host = WorkflowsDesignTestHost.Create();

        var validator = new UnknownActivityVersionValidator(
            ValidatorTestHelpers.CatalogResolver(new StubActivityCatalog()),
            ValidatorTestHelpers.Options(),
            ValidatorTestHelpers.Walker());
        var executeValidations = new ExecuteValidations([validator]);
        host.EventPublisher.Subscribe<OnDraftValidating>(e => executeValidations.Handle(e, CancellationToken.None));

        var draftId = await CreateDraft(host);
        await UpdateDraftTestKit.Update(host, draftId, UpdateDraftTestKit.State(
            activities: [UpdateDraftTestKit.Node("n1", "av-unregistered")]));

        using var scope = host.Services.CreateScope();
        var gate = scope.ServiceProvider.GetRequiredService<IPromoteDraftToVersionCommand>();
        var ex = await Assert.ThrowsAsync<DraftHasValidationErrorsException>(() => gate.Execute(WorkflowsDesignTestHost.TestOperationKey, draftId));

        Assert.Equal(1, ex.ErrorCount);
    }

    private static async Task<string> CreateDraft(WorkflowsDesignTestHost host)
    {
        await host.EnsureDefinition("wf-1");
        using var scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICreateDraftCommand>().Execute(WorkflowsDesignTestHost.TestOperationKey, "wf-1");
    }
}
