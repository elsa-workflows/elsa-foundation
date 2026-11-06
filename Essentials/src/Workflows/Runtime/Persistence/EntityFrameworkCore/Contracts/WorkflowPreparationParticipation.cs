using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

public enum WorkflowPreparationEfTopology { SamePhysical, IndependentPhysical }

// Only the configured Publishing EF issuer supplies this lease. Topology is
// independently qualified before any owner transaction starts.
public interface IWorkflowPreparationEfOwnerLease : IWorkflowPreparationOwnerLease
{
    DbContext ConfiguredOwnerContext { get; }
    WorkflowPreparationEfTopology Topology { get; }
}

// Actual selected provider participant; used for topology admission before a
// transaction is opened. No caller transaction may be supplied here.
public interface IWorkflowPreparationEfCommitParticipant : IWorkflowPreparationCommitParticipant
{
    DbContext RuntimeContext { get; }
    string PhysicalStoreIdentity { get; }
    // Validates the actual private provider frame, its configured tuple,
    // issuer/session identities, fresh shared contexts and callback lifetime.
    IWorkflowPreparationEfCommitContext RequireActiveContext(
        IWorkflowPreparationCommitContext context);
}

// Implemented only by the Runtime-owned active provider frame.
public interface IWorkflowPreparationEfCommitContext : IWorkflowPreparationCommitContext
{
    DbContext RuntimeContext { get; }
    // Fresh owner context enlisted by EfSharedTransaction in SamePhysical;
    // null in IndependentPhysical. Never a borrowed caller context.
    DbContext? OwnerContext { get; }
    DbConnection Connection { get; }
    DbTransaction Transaction { get; }
    string PhysicalStoreIdentity { get; }
}
