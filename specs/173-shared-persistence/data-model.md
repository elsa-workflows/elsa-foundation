# Shared persistence configuration data model

Status: Integrated design review approved on 2026-09-23 for #1967. Published in PR #1973; implementation and runtime/database verification remain #1968/#1969 work.

Canonical decisions:

- [Authored persistence configuration and presence](decisions/authored-persistence.md)
- [Explicit configuration context for resource-aware tooling](decisions/tooling-configuration-context.md)
- [Resource and module selection in persistence tooling](decisions/tooling-target-selection.md)
- [Verify the supplied connection against the selected resource](decisions/tooling-target-verification.md)
- [Feature specification](spec.md)
- [Research and decisions](research.md)

## Model boundary

This model covers one bounded capability: selecting named relational persistence resources for explicitly enrolled first-party EF consumers. It keeps resolution separate from CShells feature construction, EF provider/context construction, connection-string resolution, live target verification, migration policy, and any generic settings or apply framework.

Host-owned OpenIddict, Secrets, distributed stores, readers, third-party stores, and custom persistence types remain outside automatic enrollment unless a later reviewed decision adds them. The pure resolver consumes detached authored data and returns detached redacted data. It does not read configuration providers, instantiate feature classes, open databases, resolve a connection string, or mutate a shell.

## Authored configuration

The first contract uses the existing Elsa:Persistence namespace:

    {
      "Elsa": {
        "Persistence": {
          "Resources": {
            "primary": {
              "Provider": "PostgreSql",
              "ConnectionName": "Elsa"
            },
            "diagnostics": {
              "Provider": "PostgreSql",
              "ConnectionName": "ElsaDiagnostics"
            }
          },
          "DefaultResource": "primary"
        }
      },
      "CShells": {
        "Shells": {
          "default": {
            "Configuration": {
              "Elsa": {
                "Persistence": {
                  "Bindings": {
                    "DiagnosticsStructuredLogsEntityFrameworkCore": "diagnostics",
                    "DiagnosticsOpenTelemetryEntityFrameworkCore": "diagnostics"
                  }
                }
              }
            }
          }
        }
      }
    }

The keys are:

| Path | Meaning |
|---|---|
| Elsa:Persistence:Resources:<name>:Provider | Canonical relational provider for one named resource. |
| Elsa:Persistence:Resources:<name>:ConnectionName | Host ConnectionStrings reference for that resource. |
| Elsa:Persistence:DefaultResource | Root fallback selected when a shell has no default. |
| CShells:Shells:<shell>:Configuration:Elsa:Persistence:DefaultResource | Shell default override. |
| CShells:Shells:<shell>:Configuration:Elsa:Persistence:Bindings:<featureId> | Shell-scoped feature binding. |

There are no root feature bindings or shell-local resource catalogs in this slice. A binding never enables a feature. Resource definitions without an applicable default or binding are inert. Unknown authored nodes remain in the authored document and are reported unresolved; they are not interpreted as root bindings.

Selection precedence is shell feature binding, shell default, root default, then legacy feature configuration. A present shell key wins over root fallback, including a final code-configured shell default. Removing the owning override reveals the next lower-priority value.

Resource identity is case-insensitive for lookup. Resource and connection-reference names follow the identifier-like syntax and reserved-token rule in [Authored persistence configuration](decisions/authored-persistence.md#configuration-surface). A selected resource is atomic: Provider and ConnectionName must both be present and nonblank on the same object. Resources have no inline ConnectionString, Schema, Pooling, or migration-policy fields.

Presence semantics are explicit:

| State | Selection/default/binding | Legacy target field |
|---|---|---|
| Absent | Inherit or continue legacy resolution. | Not authored. |
| Nonblank value | Candidate selection/value. | Authored; conflicts with applicable resource mode. |
| Empty/blank | Refusal. | Authored malformed value. |
| Explicit null | Refusal; never removal. | Authored null. |
| Unknown/wrong type | Refusal when selected or applicable. | Refusal when interpretation is unsafe. |

Deleting a key at its owning source is the only removal operation. Null does not remove a binding or restore inheritance.

## Detached resolver input

The following is an internal detached shape for the first implementation slice. It is not a new public settings API. The resolver and detached records remain internal initially; the enrollment marker and the narrow existing-type preparation facade in [the configuration contract](contracts/persistence-configuration.md#cross-assembly-preparation-boundary) are public integration points.

    internal interface IPersistenceResourceResolver
    {
        PersistenceResolutionResult Resolve(PersistenceResolutionInput input);
    }

    internal sealed record PersistenceResolutionInput(
        PersistenceResourceCatalog RootCatalog,
        PersistenceShellSelection ShellSelection,
        IReadOnlyList<EnrolledPersistenceParticipant> Participants,
        IReadOnlyDictionary<string, PersistenceLegacyTargetPresence> LegacyTargets,
        PersistenceConfigurationContext SourceContext);

    internal sealed record PersistenceResourceCatalog(
        IReadOnlyDictionary<string, PersistenceResourceDefinition> Resources,
        PersistenceAuthoredValue RootDefault);

    internal sealed record PersistenceShellSelection(
        string ShellName,
        PersistenceAuthoredValue ShellDefault,
        IReadOnlyDictionary<string, PersistenceAuthoredValue> FeatureBindings);

    internal sealed record PersistenceResourceDefinition(
        string Name,
        PersistenceAuthoredValue Provider,
        PersistenceAuthoredValue ConnectionName,
        PersistenceSourceProvenance Source);

    internal sealed record PersistenceAuthoredValue(
        PersistencePresence Presence,
        string? Value,
        PersistenceSourceProvenance Source);

    internal enum PersistencePresence
    {
        Absent,
        Value,
        Blank,
        Null,
        WrongType
    }

    internal sealed record PersistenceSourceProvenance(
        string Scope,
        string Mode,
        bool IsExternal,
        bool IsAuthored);

    internal enum PersistenceContextMode
    {
        Runtime,
        Management,
        ToolingFile,
        ToolingFileEnvironment
    }

Value is permitted only for non-secret provider, connection-reference, and resource-name values. A connection-string legacy field is represented by presence only:

    internal sealed record PersistenceLegacyFieldPresence(
        PersistencePresence Presence,
        PersistenceSourceProvenance Source);

    internal sealed record PersistenceLegacyTargetPresence(
        string FeatureId,
        bool IsEnabled,
        bool IsReset,
        PersistenceLegacyFieldPresence Provider,
        PersistenceLegacyFieldPresence ConnectionName,
        PersistenceLegacyFieldPresence ConnectionString);

The adapter builds this record from the final composed shell map plus a narrow selected-shell IConfiguration presence read. It uses child enumeration or provider TryGet, never IConfigurationSection.Exists(), to distinguish absent from explicit null. It supports CShells object-map features and array entries with Name, direct settings, or a Settings wrapper. CShells remains authoritative for malformed or mixed shape refusal.

IsReset suppresses lower-priority raw feature fields. A directly disabled feature is inactive. The final dependency-expanded graph remains authoritative: a feature reintroduced as a required dependency remains applicable and is not silently subtracted.

The source context is also detached:

    internal sealed record PersistenceConfigurationContext(
        PersistenceContextMode Mode,
        string ShellName,
        string? EnvironmentName,
        IReadOnlyList<PersistenceSourceProvenance> CheckedSources,
        bool IncludesExternalEnvironment,
        bool IsFrozenSnapshot);

The bounded tooling modes are workbench-json-v1 and workbench-json-environment-v1. The latter means worker-inherited environment supplied by the operator; it never claims to observe an independently running host. Raw host paths, configuration snapshots, and secret values stay in the owning adapter.

## Enrollment and module metadata

Enrollment is explicit and stable. The first slice proposes this marker on each of the 13 feature classes:

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class EfPersistenceResourceParticipantAttribute : Attribute;

The marker does not duplicate module names or provider defaults. The host-side metadata adapter combines it with ShellFeatureAttribute/stable feature identity, UsesEfModuleAttribute declarations, EfFeatureModuleUsage discovery, and EfModuleDescriptor metadata. That metadata supplies canonical modules, context identity, provider-derived context support, migration history, default connection names, and dependencies without constructing feature classes.

The detached participant model is:

    internal sealed record EnrolledPersistenceParticipant(
        string FeatureId,
        IReadOnlyList<string> ModuleNames,
        string ContextIdentity,
        bool DeclaresProvider,
        bool HasOpaqueConfigurator);

The marker prevents enrollment by property-name resemblance or assembly hardcoding. Existing metadata sources are:

- src/essentials/Persistence/EntityFramework/UsesEfModuleAttribute.cs
- src/essentials/Persistence/EntityFramework/Tooling/EfProviderAgreement.cs
- src/essentials/Persistence/EntityFramework/EfModuleCatalog.cs
- src/essentials/Persistence/EntityFramework/EfModuleDescriptor.cs

The 13 stable participants and their module/context ownership are defined canonically by [specification § Normative supported participants and constraints](spec.md#normative-supported-participants-and-constraints). Dashboard readers, Identity IAM, Identity provider configuration, Secrets, distributed contexts, Elsa 3 import, OpenIddict, private stores, and unknown/custom consumers remain outside automatic enrollment. Their authored data is preserved and unresolved where applicable.

## Resolver output

    internal sealed record PersistenceResolutionResult(
        IReadOnlyList<PersistenceParticipantResolution> Participants,
        IReadOnlyList<PersistenceResolutionRefusal> Refusals,
        PersistenceSourceEvidence Evidence)
    {
        public bool IsRefused => Refusals.Count != 0;
    }

    internal sealed record PersistenceParticipantResolution(
        EnrolledPersistenceParticipant Participant,
        PersistenceSelectionKind Selection,
        string? ResourceName,
        string? Provider,
        string? ConnectionName,
        PersistenceSourceProvenance Source,
        IReadOnlyList<string> UnresolvedPrerequisites);

    internal enum PersistenceSelectionKind
    {
        Legacy,
        ShellBinding,
        ShellDefault,
        RootDefault
    }

    internal sealed record PersistenceResolutionRefusal(
        string Code,
        string? FeatureId,
        string? ResourceName,
        string? FieldName);

    internal sealed record PersistenceSourceEvidence(
        PersistenceConfigurationContext Context,
        IReadOnlyList<PersistenceSourceProvenance> Sources,
        IReadOnlyList<string> UnverifiedPrerequisites);

For resource-selected participants, the output is the materialization plan: Provider and ConnectionName are patched before feature binding. Inherited values are not written back into the authored document. Legacy participants remain untouched.

The result contains no connection value, connection hash, raw source path, provider exception, or reversible secret representation. Public refusal/evidence records identify feature, module, resource/reference identity, checked source mode, and stable refusal code only.

## Validation ownership

The pure resolver validates authored resource shape, selection precedence, atomic provider/reference presence, enrollment applicability, legacy target conflicts, opaque configurator refusal, and unresolved unknowns.

EF-owned validation consumes the plan and module metadata for provider engine/context support, provider agreement, same-context Runtime agreement, schema/pooling compatibility, Activities Design/Workflows Design shared transactions, ordered publication operations, diagnostics transaction ownership, provider-specific schema rules, and existing migration policy. Resource names or aliases never establish physical database identity.

EfConnectionDefaults.ResolveConnectionString remains the legacy/runtime connection sidecar. Tooling resolves an expected named connection only inside the explicitly selected host context for live commands, compares it strictly with the existing env/stdin actual connection, and never returns either value. Offline commands do not resolve expected connection values.

The shared Runtime/Workflows Design/Activities Design/Publishing layout and separate Structured Logs/OpenTelemetry layout are configuration candidates, not readiness claims. Live host/database evidence remains owned by #1968 and #1969.

## Preservation and lifecycle

Adapters preserve unknown feature IDs, unknown settings, disabled/reset distinctions, and unrelated feature fields. The management store saves authored intent. The first slice refuses a legacy feature-management write when resource mode applies; it does not flatten generated Provider/ConnectionName values into the authored document. Resource-aware acceptance, source drift, and saved-versus-activated reporting remain #1964 work.

Startup and shell reload create a fresh input snapshot and invoke the resolver before feature construction/binding. A reload does not reuse stale materialization. If root and shell inputs cannot be collected consistently, the adapter refuses or retries the candidate as one snapshot; it does not claim an atomic configuration transaction.
