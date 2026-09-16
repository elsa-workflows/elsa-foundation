using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Services;

/// <summary>
/// The two Design contexts an upgrade reads and writes, with the stores each lane owns bound to exactly
/// those contexts.
/// </summary>
/// <remarks>
/// An upgrade runs twice against the same code: once on the ambient request-scoped contexts, to plan and
/// to reject a stale plan before any connection is opened, and once on the fresh contexts an
/// <c>EfSharedTransaction</c> constructs, so that every read a final validation makes and every row it
/// writes are inside the one transaction that commits. Binding the stores to the lane rather than
/// resolving them from the container is what makes the second pass possible; it is the same shape the
/// Elsa 3 import command uses.
/// </remarks>
internal sealed class EfActivityUpgradeLane
{
    public EfActivityUpgradeLane(
        ActivitiesDesignDbContext activities,
        WorkflowsDesignDbContext workflows,
        IPersistenceAccessContextAccessor access,
        IPayloadSerializer payloadSerializer)
    {
        ArgumentNullException.ThrowIfNull(activities);
        ArgumentNullException.ThrowIfNull(workflows);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(payloadSerializer);
        Activities = activities;
        Workflows = workflows;
        Access = access;
        Payloads = payloadSerializer;
        Design = new EfActivityDesignStores(activities, access);
        WorkflowDefinitions = new EfWorkflowDefinitionStore(workflows, access);
        WorkflowDrafts = new EfWorkflowDefinitionDraftStore(workflows, payloadSerializer, access);
        WorkflowVersions = new EfWorkflowDefinitionVersionStore(workflows, payloadSerializer, WorkflowDefinitions, access);
        WorkflowVersionLayouts = new EfWorkflowDefinitionVersionLayoutStore(workflows, access);
    }

    public ActivitiesDesignDbContext Activities { get; }

    public WorkflowsDesignDbContext Workflows { get; }

    public IPersistenceAccessContextAccessor Access { get; }

    public IPayloadSerializer Payloads { get; }

    /// <summary>Every Activities Design read port, bound to <see cref="Activities"/>.</summary>
    public EfActivityDesignStores Design { get; }

    public EfWorkflowDefinitionStore WorkflowDefinitions { get; }

    public EfWorkflowDefinitionDraftStore WorkflowDrafts { get; }

    public EfWorkflowDefinitionVersionStore WorkflowVersions { get; }

    public EfWorkflowDefinitionVersionLayoutStore WorkflowVersionLayouts { get; }
}
