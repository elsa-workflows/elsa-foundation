using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.EFCore.DbContext;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// SC-014 + Unit C FR-024. The promotion gate refuses to promote a Draft whose
/// <c>WorkflowDefinitionDraftValidation</c> sibling carries any errors. Unit C ships the
/// contract (<see cref="IPromoteDraftToVersionCommand"/>) + the exception type
/// (<see cref="DraftHasValidationErrorsException"/>); Unit D ships the implementation. This
/// test exercises a stub implementation against the same sibling state the real command will
/// read.
/// </summary>
public sealed class PromotionGateTests
{
    [Fact]
    public async Task Promotion_throws_when_validation_sibling_has_errors()
    {
        using var host = WorkflowsDesignTestHost.Create();

        host.DomainEventSender.OnSend = evt =>
        {
            if (evt is OnDraftValidating validating)
            {
                validating.AddValidationError(new ValidationError("$workflow", "Graph/StartActivity", "No start"));
                validating.AddValidationError(new ValidationError("n1", "Graph/OrphanActivity", "Orphan node"));
            }
        };

        var draftId = await CreateDraft(host);

        var gate = new StubPromotionGate(host.Services);

        var ex = await Assert.ThrowsAsync<DraftHasValidationErrorsException>(
            () => gate.Execute(draftId));

        Assert.Equal(draftId, ex.DraftId);
        Assert.Equal(2, ex.ErrorCount);
    }

    [Fact]
    public async Task Promotion_succeeds_when_validation_sibling_is_empty()
    {
        using var host = WorkflowsDesignTestHost.Create();

        // No OnSend hook — validators contribute nothing; sibling persists with empty Errors.
        var draftId = await CreateDraft(host);

        var gate = new StubPromotionGate(host.Services);

        var versionId = await gate.Execute(draftId);

        Assert.NotNull(versionId);
        Assert.NotEmpty(versionId);
    }

    private static async Task<string> CreateDraft(WorkflowsDesignTestHost host)
    {
        await host.EnsureDefinition("wf-1");
        using var scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICreateDraftCommand>().Execute("wf-1");
    }

    /// <summary>
    /// Minimal in-test promotion handler that reads the validation sibling and either throws
    /// or returns a synthetic version id. Substantive implementation lives in Unit D.
    /// </summary>
    private sealed class StubPromotionGate(IServiceProvider services) : IPromoteDraftToVersionCommand
    {
        public async Task<string> Execute(string draftId, CancellationToken cancellationToken = default)
        {
            using var scope = services.CreateScope();
            var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<WorkflowsDesignDbContext>>();

            await using var dbContext = await contextFactory.CreateDbContextAsync(cancellationToken);
            var sibling = await dbContext.WorkflowDefinitionDraftValidations
                .FirstOrDefaultAsync(v => v.WorkflowDefinitionDraftId == draftId, cancellationToken);

            var errorCount = sibling?.Errors.Count ?? 0;
            if (errorCount > 0)
                throw new DraftHasValidationErrorsException(draftId, errorCount);

            return $"version-of-{draftId}";
        }
    }
}
