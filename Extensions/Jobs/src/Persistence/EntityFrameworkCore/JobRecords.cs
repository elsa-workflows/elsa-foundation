namespace Elsa.Jobs.Persistence.EntityFrameworkCore;

internal interface IJobRecord
{
    string Key { get; set; }
    string ScopeKey { get; set; }
    string Identity { get; set; }
    byte[] Token { get; set; }
    string SchemaVersion { get; set; }
}

internal sealed class JobRecord : IJobRecord
{
    public string Key { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string Identity { get; set; } = null!;
    public byte[] Token { get; set; } = [];
    public string SchemaVersion { get; set; } = JobsEfModule.Version;
    public string JobId { get; set; } = null!;
    public string RequestJson { get; set; } = null!;
    public string RequestSha256 { get; set; } = null!;
    public string HandlerKey { get; set; } = null!;
    public string? ImplementationIdentity { get; set; }
    public string? PinnedSupportKey { get; set; }
    public string JobType { get; set; } = null!;
    public string Queue { get; set; } = null!;
    public int State { get; set; }
    public long Revision { get; set; }
    public long AcceptedAt { get; set; }
    public long EligibleAt { get; set; }
    public int AttemptCount { get; set; }
    public long Fence { get; set; }
    public string? ActiveAttemptKey { get; set; }
    public bool CancellationRequested { get; set; }
    public string? ResultJson { get; set; }
    public string? ProgressJson { get; set; }
    public string? RetriedFromJobId { get; set; }
    public string? UnavailableDiagnosticJson { get; set; }
}

internal sealed class JobRecognitionRecord : IJobRecord
{
    public string Key { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string Identity { get; set; } = null!;
    public byte[] Token { get; set; } = [];
    public string SchemaVersion { get; set; } = JobsEfModule.Version;
    public string JobKey { get; set; } = null!;
    public string RequestSha256 { get; set; } = null!;
    public long RecognizeUntil { get; set; }
}

internal sealed class JobOriginRecord : IJobRecord
{
    public string Key { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string Identity { get; set; } = null!;
    public byte[] Token { get; set; } = [];
    public string SchemaVersion { get; set; } = JobsEfModule.Version;
    public string OriginSha256 { get; set; } = null!;
    public string RequestSha256 { get; set; } = null!;
    public string? JobKey { get; set; }
    public bool Cancelled { get; set; }
    public bool Retired { get; set; }
}

internal sealed class JobAttemptRecord : IJobRecord
{
    public string Key { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string Identity { get; set; } = null!;
    public byte[] Token { get; set; } = [];
    public string SchemaVersion { get; set; } = JobsEfModule.Version;
    public string JobKey { get; set; } = null!;
    public string AttemptId { get; set; } = null!;
    public int Number { get; set; }
    public string Owner { get; set; } = null!;
    public long Fence { get; set; }
    public long OwnershipRevision { get; set; }
    public string ImplementationIdentity { get; set; } = null!;
    public long GrantedAt { get; set; }
    public long ExpiresAt { get; set; }
    public long Deadline { get; set; }
    public int State { get; set; }
    public long? PhysicalStartedAt { get; set; }
    public long? PhysicalExitedAt { get; set; }
    public string? DiagnosticJson { get; set; }
    public string? CompletionReceiptKey { get; set; }
    public string? LastRenewalDigest { get; set; }
    public string? LastRenewalLeaseJson { get; set; }
}

internal sealed class JobClaimGrantRecord : IJobRecord
{
    public string Key { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string Identity { get; set; } = null!;
    public byte[] Token { get; set; } = [];
    public string SchemaVersion { get; set; } = JobsEfModule.Version;
    public string RequestDigest { get; set; } = null!;
    public string LeaseJson { get; set; } = null!;
}

internal sealed class JobGateRecord : IJobRecord
{
    public string Key { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string Identity { get; set; } = null!;
    public byte[] Token { get; set; } = [];
    public string SchemaVersion { get; set; } = JobsEfModule.Version;
    public int Kind { get; set; }
    public string? ModelKind { get; set; }
    public string Name { get; set; } = null!;
    public long ArbitrationRevision { get; set; }
    public long CapacityRevision { get; set; }
    public int? Capacity { get; set; }
    public int ActiveHolds { get; set; }
}

internal sealed class JobHoldRecord : IJobRecord
{
    public string Key { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string Identity { get; set; } = null!;
    public byte[] Token { get; set; } = [];
    public string SchemaVersion { get; set; } = JobsEfModule.Version;
    public string GateKey { get; set; } = null!;
    public string HoldJson { get; set; } = null!;
}

internal sealed class JobPermitRecord : IJobRecord
{
    public string Key { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string Identity { get; set; } = null!;
    public byte[] Token { get; set; } = [];
    public string SchemaVersion { get; set; } = JobsEfModule.Version;
    public string GateKey { get; set; } = null!;
    public string AttemptKey { get; set; } = null!;
    public long ExpiresAt { get; set; }
}

internal sealed class JobGateMembershipRecord : IJobRecord
{
    public string Key { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string Identity { get; set; } = null!;
    public byte[] Token { get; set; } = [];
    public string SchemaVersion { get; set; } = JobsEfModule.Version;
    public string JobKey { get; set; } = null!;
    public string GateKey { get; set; } = null!;
}

internal sealed class JobClaimCursorRecord : IJobRecord
{
    public string Key { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string Identity { get; set; } = null!;
    public byte[] Token { get; set; } = [];
    public string SchemaVersion { get; set; } = JobsEfModule.Version;
    public string? AfterKey { get; set; }
}

internal sealed class JobPhysicalReceiptRecord : IJobRecord
{
    public string Key { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string Identity { get; set; } = null!;
    public byte[] Token { get; set; } = [];
    public string SchemaVersion { get; set; } = JobsEfModule.Version;
    public string JobKey { get; set; } = null!;
    public string AttemptKey { get; set; } = null!;
    public string ReceiptJson { get; set; } = null!;
    public string ReceiptSha256 { get; set; } = null!;
}

internal sealed class JobOperationRecord : IJobRecord
{
    public string Key { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string Identity { get; set; } = null!;
    public byte[] Token { get; set; } = [];
    public string SchemaVersion { get; set; } = JobsEfModule.Version;
    public string Digest { get; set; } = null!;
    public string ResultJson { get; set; } = null!;
    public string? AuditJson { get; set; }
}

internal sealed class JobScheduleRecord : IJobRecord
{
    public string Key { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string Identity { get; set; } = null!;
    public byte[] Token { get; set; } = [];
    public string SchemaVersion { get; set; } = JobsEfModule.Version;
    public string DefinitionJson { get; set; } = null!;
    public string? UnavailableDiagnosticJson { get; set; }
    public long Revision { get; set; }
    public long DefinitionRevision { get; set; }
    public long NextOccurrence { get; set; }
    public bool Enabled { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
    public long? CoalesceNext { get; set; }
    public long? CoalesceCutoff { get; set; }
    public long CoalesceCount { get; set; }
    public long? CoalesceThrough { get; set; }
}

internal sealed class JobOccurrenceRecord : IJobRecord
{
    public string Key { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string Identity { get; set; } = null!;
    public byte[] Token { get; set; } = [];
    public string SchemaVersion { get; set; } = JobsEfModule.Version;
    public string ReceiptJson { get; set; } = null!;
}

internal sealed class JobDeliveryRecord : IJobRecord
{
    public string Key { get; set; } = null!;
    public string ScopeKey { get; set; } = null!;
    public string Identity { get; set; } = null!;
    public byte[] Token { get; set; } = [];
    public string SchemaVersion { get; set; } = JobsEfModule.Version;
    public string JobKey { get; set; } = null!;
    public string ResultId { get; set; } = null!;
    public string AssociationJson { get; set; } = null!;
    public string? PermissionJson { get; set; }
    public string? ProofJson { get; set; }
    public bool Acknowledged { get; set; }
}
