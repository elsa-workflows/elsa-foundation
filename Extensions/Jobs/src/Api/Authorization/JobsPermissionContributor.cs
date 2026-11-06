using Elsa.Foundation.Identity.Core.Authorization;

namespace Elsa.Jobs.Api.Authorization;

internal sealed class JobsPermissionContributor : IPermissionContributor
{
    public string OwnerId => "Elsa.Jobs.Api";
    public string ContributorType => typeof(JobsPermissionContributor).FullName!;
    public IEnumerable<Permission> Contribute() =>
    [
        Permission(JobsPermissions.AuditRead, "Read job operation audit", "Inspect retained original actors, reasons, targets and confirmations of Jobs operations."),
        Permission(JobsPermissions.Read, "Read jobs", "Inspect job, queue, schedule, attempt and hold metadata."),
        Permission(JobsPermissions.Submit, "Submit jobs", "Accept independent registered handler invocations."),
        Permission(JobsPermissions.InputsRead, "Read job inputs", "Inspect bounded job and schedule input payloads."),
        Permission(JobsPermissions.ResultsRead, "Read job results", "Inspect bounded retained result payloads."),
        Permission(JobsPermissions.Pause, "Pause and resume jobs", "Add or release an addressed queue, type or workflow hold."),
        Permission(JobsPermissions.Cancel, "Cancel jobs", "Request cooperative cancellation and prevent later attempts."),
        Permission(JobsPermissions.Retry, "Retry jobs as new runs", "Create a linked run while preserving prior results and model history."),
        Permission(JobsPermissions.SchedulesManage, "Manage job schedules", "Create, edit, enable or disable recurring schedules."),
        Permission(JobsPermissions.CapacityManage, "Manage job capacity", "Change revisioned authorized-lease limits.")
    ];

    private Permission Permission(string key, string name, string description) => new(key, name, "Jobs", description)
    { OwnerId = OwnerId, ContributorType = ContributorType };
}
