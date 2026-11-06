using System.Text.Json.Serialization;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Reconciliation;

/// <summary>Ephemeral server-only origin; both fields are excluded from serialized material.</summary>
public sealed record CatalogAdmissionOrigin(
    [property: JsonIgnore] string RegistrationId,
    [property: JsonIgnore] object ScopeBinding);

/// <summary>Canonical server-loaded or cloned material and actual ownership, not authored scope.</summary>
public sealed record CatalogAdmissionCandidate(
    IActivityDefinition Definition,
    IActivityDefinitionVersion Version,
    CatalogOwnerScope DefinitionOwner,
    CatalogOwnerScope VersionOwner,
    CatalogDisclosureRequirement Inherited);

/// <summary>A reconciliation contribution retaining its trusted current-scope origin.</summary>
public sealed record CatalogAdmissionContribution(
    CatalogAdmissionCandidate Candidate,
    [property: JsonIgnore] CatalogAdmissionOrigin Origin);
