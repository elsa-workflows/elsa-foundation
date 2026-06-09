namespace Elsa.Workflows.Design.Core.Models;

/// <summary>
/// One endpoint of an <see cref="ActivityConnection"/> — the activity-node identifier
/// + the port name. <see cref="ActivityNodeId"/> references <see cref="ActivityNode.NodeId"/>
/// within the owning Version/Draft. Renamed from <c>ActivityReferenceKey</c> per
/// Unit C FR-009 + research item R1 — the <c>Activity</c> prefix disambiguates the FK
/// reference from <c>ActivityNode.NodeId</c>'s own identity and matches typical FK naming.
/// </summary>
public sealed record ActivityPortConnection(
    string ActivityNodeId,
    string Port
);
