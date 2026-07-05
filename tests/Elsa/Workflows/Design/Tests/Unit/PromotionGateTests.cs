using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
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
/// SC-014 + Unit C FR-024. The promotion gate refuses to promote a Draft whose
/// <c>WorkflowDefinitionDraftValidation</c> sibling carries any errors.
/// </summary>
public sealed class PromotionGateTests
{
    [Fact]
    public async Task Promotion_throws_when_validation_sibling_has_errors()
    {
        using var host = WorkflowsDesignTestHost.Create();

        host.EventPublisher.OnPublish = evt =>
        {
            if (evt is OnDraftValidating validating)
            {
                validating.Errors.Add(new ValidationError("$workflow", "Graph/StartActivity", "No start"));
                validating.Errors.Add(new ValidationError("n1", "Graph/OrphanActivity", "Orphan node"));
            }
        };

        var draftId = await CreateDraft(host);

        using var scope = host.Services.CreateScope();
        var gate = scope.ServiceProvider.GetRequiredService<IPromoteDraftToVersionCommand>();
        var ex = await Assert.ThrowsAsync<DraftHasValidationErrorsException>(() => gate.Execute(draftId));

        Assert.Equal(draftId, ex.DraftId);
        Assert.Equal(2, ex.ErrorCount);
    }

    [Fact]
    public async Task Promotion_succeeds_when_validation_sibling_is_empty()
    {
        using var host = WorkflowsDesignTestHost.Create();

        // No OnSend hook - validators contribute nothing; sibling persists with empty Errors.
        var draftId = await CreateDraft(host);

        using var scope = host.Services.CreateScope();
        var gate = scope.ServiceProvider.GetRequiredService<IPromoteDraftToVersionCommand>();
        var versionId = await gate.Execute(draftId);

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
            ValidatorTestHelpers.Resolver(new StubActivityCatalog()),
            ValidatorTestHelpers.Options(),
            ValidatorTestHelpers.Walker());
        var executeValidations = new ExecuteValidations([validator]);
        host.EventPublisher.Subscribe<OnDraftValidating>(e => executeValidations.Handle(e, CancellationToken.None));

        var draftId = await CreateDraft(host);
        await UpdateDraftTestKit.Update(host, draftId, UpdateDraftTestKit.State(
            activities: [UpdateDraftTestKit.Node("n1", "av-unregistered")]));

        using var scope = host.Services.CreateScope();
        var gate = scope.ServiceProvider.GetRequiredService<IPromoteDraftToVersionCommand>();
        var ex = await Assert.ThrowsAsync<DraftHasValidationErrorsException>(() => gate.Execute(draftId));

        Assert.Equal(1, ex.ErrorCount);
    }

    private static async Task<string> CreateDraft(WorkflowsDesignTestHost host)
    {
        await host.EnsureDefinition("wf-1");
        using var scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICreateDraftCommand>().Execute("wf-1");
    }
}
