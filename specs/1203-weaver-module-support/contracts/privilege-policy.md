# Proposed contract: privilege policy, principal and feature admission

**Status**: Proposed Preview contract for leaf0; freeze only after qualification.
**Authority**: spec1203 FR005/015/017/018 and story1. Historical contract: [1186 session policy](../../1186-weaver-effects/contracts/session-policy.md).

## Exact public additions

All existing constructors and public overloads remain. New properties are body members.

    public enum AgentPrivilegeClass
    {
        Administrative = 0,
        Irrecoverable = 1,
        Activation = 2
    }

    public interface IAgentPrincipalSource
    {
        ClaimsPrincipal? GetCurrent(string actorId, string tenantId);
    }

AgentToolPreparation and AgentPreparedToolInvocation gain:

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AgentPrivilegeClass>? RequiredPrivilegeClasses { get; init; }

Null and empty are one logical empty set. The engine converts empty to null; copies nonempty input; rejects unknown enum values; removes duplicates; sorts by enum value; exposes a read-only list. Callers cannot mutate the approval basis after preparation. Existing Core enum serialization conventions apply; Studio normalizes the three explicit values for display.

Elsa.Agent gains public AgentPrivilegePolicyOptions, with SectionName Elsa:Agent:Privileges and three bool properties Administrative, Irrecoverable, Activation, all false. AgentBootstrapResponse gains a Privileges body member of AgentPrivilegePolicyView, a record of those three booleans, defaulting all false. It is read-only client information, never a policy-update command.

Core owns the enum/model/principal contract. Options and internal policy/admission mechanisms belong to Agent implementation; HTTP principal/bootstrap mapping belongs to Agent API.

## Current host policy

Bind the host configuration section to monitored options. Read/copy the current three booleans at every required boundary; do not capture a startup feature property, session policy or scoped service in the singleton session owner. Workbench base settings enable all three; Production does not undo that setting. Other hosts remain off unless configured.

For a new preparation, effective risk is max of descriptor and owner preparation. Add Administrative when that risk is Admin, even if the owner omitted it. Every declared class is required. Validate before freezing the preparation, then check current policy before review.

At approval, execute, recovered explicit execute and pending-client reissue, derive the effective requirement from frozen classes plus Administrative for frozen Admin risk. Approval uses only core-owned state; it calls neither module nor principal source. A veto on genuinely undispatched work closes the proposal as a zero-effect refusal with agent.proposal.privilege_disabled (403); enabling a class requires a new proposal.

Never replace a genuine accepted terminal receipt with a later policy refusal or dispatch again. Already dispatched work remains potentially applied: a veto prevents reissue but does not relabel it zero-effect. Keep existing uncertainty/deadline/recovery rules and the trusted pre-dispatch cancellation signal.

The last options read is an admission observation, not a distributed configuration lock. Recheck immediately before default dispatch after awaited authority checks where possible. A toggle after that read can race dispatch; prove this with barriers and preserve truthful outcomes.

## Legacy integrity and review

Empty classes call the exact existing PreparedDigest anonymous shape. Only a nonempty canonical set adds a RequiredPrivilegeClasses member to that hash shape. Null/empty must not add a new null/empty JSON property to the old digest.

A recovered legacy Admin preparation retains its original empty/null member and digest. Derive its administrative veto/display without rewriting either. Generic review uses frozen risk and the optional classes; Admin is existing risk value3, not an inferred domain action. Test numeric and textual risk presentation.

Held Agent1.0 can serve representable legacy unclassed records, including legacy Admin subject to current policy. A new effective-Admin preparation has a new frozen class set and requires new-feature admission. These are different cases.

## Principal replacement

IAgentPrincipalSource is an optional scoped host integration and a replacement contract, not a collection. Base Agent registers none, preserving legacy-only compositions. Agent API uses TryAddScoped for its HTTP implementation, preserving an explicitly supplied custom source, including a copied root registration. A host replacing it afterward uses Replace. Repeated API registration remains one. Extend AgentRegistrationValidator to reject more than one nonkeyed registration and a non-scoped lifetime. Every new owning tool/context call also resolves the set and requires exactly one before calling it. Zero is an unsupported principal integration and returns a cause-free owner-unavailable diagnostic, never a synthesized principal; duplicates fail closed even when a direct call bypasses the default facades. Last registration wins is not supported.

The HTTP implementation requires IsAuthenticated and AgentEndpointActor's exact actor/tenant match, then returns the actual current HttpContext.User object. It performs no I/O, so the interface is synchronous. Missing context, missing identities, mismatch or unavailable/duplicate source returns no principal and the module reports owner-unavailable.

Non-HTTP integrators may replace the source with an equivalent genuine-current-principal implementation. Never reconstruct claims from actor strings or retain a principal across requests. Preparation/read and every explicit execute obtain it separately. Owner API contracts independently verify actor/tenant and exactly-one IPermissionAuthorizationService. The source itself grants no permission.

Principal objects never enter JSON, prepared arguments, provider context, session records, audit or a digest. Keep existing IAgentTool signatures unchanged.

## Selected-store feature surface

- FoundationAgentEffectsFeature, identity FoundationAgentEffects, in Elsa.Agent: inherits abstractions, virtual ConfigureServices, no schema requirement.
- FoundationAgentEfEffectsFeature, identity FoundationAgentEfEffects, in existing Agent EF: inherits common behavior, directly declares Agent1.1.0, requires the existing EF store feature. It does not depend on the common sibling identity.

Capture actual feature Type from the inherited ConfigureServices call. Use the existing owned store-selection marker with this additive overload:

    SelectAgentSessionRecordStore(
        IServiceCollection services,
        Func<IServiceProvider, IAgentSessionRecordStore> factory,
        Type? requiredEffectsFeatureType)

Retain the existing two-argument public method; delegate with null so source and binary callers keep working in legacy-only compositions. Null on an explicit selection means undeclared, never memory. Selecting any effects variation alongside an undeclared explicit store is a configuration error with a migration diagnostic; this includes the old factory overload and explicit null on the new overload. The private known process-local default marker explicitly requires the common effects Type; all AddAgentStores paths provide the EF effects Type. A custom store must opt into its supported feature Type through the new overload. Extend that existing private selection metadata, not a second store registry. Common Agent code neither references EF nor infers behavior from a factory/runtime store type.

Validate the completed collection: one selected effects feature when new behavior is enabled; a store's required feature Type must be an ancestor of the actual selection; reject both siblings, wrong-store selection, duplicate or foreign registrations. No effects selection remains valid for legacy-only composition, but new optional behavior refuses.

Direct static declarations on the actual feature must include its required/base minimum declarations. Additional stricter requirements are allowed. RequiresSchemaVersionAttribute is not inherited. Use shared metadata extraction and exact declaration inclusion; do not invent version ordering. Custom specializations repeat the base minimum explicitly.

## Shared admission

Internal scoped AgentEffectsAdmission reads only selected static metadata and the shared ISchemaDormancyCheck. Empty requirements are available without an invented observation. Missing/unfinalized EF observation is refused with existing schema-version-not-finalized; absence is never satisfaction. Existing Agent features acquire no static requirement.

New behavior enters this one selected-feature check at tool collection/preparation/execution, nonempty preparation acceptance, fresh/retained source disclosure, and default/custom result acceptance. No tool descriptor contains a schema-version requirement or chooses a fleet. Legacy operations without new members do not acquire a new schema requirement; administrative veto remains independent.

Catalog, Attention and capabilities use actual feature metadata and the shared checker. Host-owned readability/fleet composition remains the only observer. Direct service consumers have the same admission and store-selection checks. Tests cover an arbitrary old two-argument factory (including an old EF factory) plus common effects, explicit null plus either variation, and a declared custom feature. The undeclared paths must diagnose before activation/new behavior; they remain valid without effects.

Qualification includes base/API ordering, explicit custom before API and Replace afterward, copied-root and repeated registration, zero/duplicate/wrong-lifetime sources, missing/both/wrong/specialized feature selections; normal memory; EF absent/held/unobserved/finalized and no-restart availability; direct consumer contexts; null/empty legacy bytes; current principal/tenant/authority failures; no replay and real composed/package journeys.
