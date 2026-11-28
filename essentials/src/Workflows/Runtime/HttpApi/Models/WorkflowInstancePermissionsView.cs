namespace Elsa.Workflows.Runtime.Api.Models;

/// <summary>The caller's permissions for one run (spec 260 FR-416). A display hint; the endpoints still enforce them.</summary>
public sealed record WorkflowInstancePermissionsView(bool CanManage, bool CanInspectValues);
