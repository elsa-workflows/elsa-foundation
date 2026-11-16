# Elsa.Activities.Design.Reconciliation.Clr

A reconciliation **source** (§2.6.1 DI-source pattern) that discovers CLR activities by scanning a
folder of assemblies and contributes one catalog row per discovered `IActivity` implementation. It
plugs into the universal reconciliation lifecycle owned by `Elsa.Activities.Design.Reconciliation`
without depending on that feature project. It references `Elsa.Activities.Design.Core` for source and
shape contracts, `Elsa.Activities.Runtime.Core` for activity abstractions and the unchanged activity-value
JSON profile, and `Elsa.Expressions.Core` for the activated selectable type catalog. It has no API or
Runtime implementation dependency.

## What this feature provides

- **`ClrActivityReconciliationSource`** — `IActivityReconciliationSource` with `SourceKind => "CLR"`.
  Its `Read` drives the scanner and emits one `ActivityVersionReconciliationModel` per activity, each
  carrying stable provider/consumer identity `elsa.clr-activity`, schema `1`, and a
  `ClrActivityDescriptor` payload wrapping the activity's stable alias
  (`TypeAliasConvention.CanonicalAlias(type)`).
- **`ClrAssemblyScanner`** — reflection-only scanner (R5) built on `MetadataLoadContext` +
  `PathAssemblyResolver`. Discovers `IActivity` implementations by metadata, never loads author code
  into the execution context, and never pollutes the default `AssemblyLoadContext`. Resilient
  (FR-023): a DLL with no activities is skipped silently; a DLL that fails to load or reflect is
  logged and skipped; the pass never aborts wholesale.
- **`ActivityTypeVersionResolver`** (`IActivityTypeVersionResolver`) — pure version-precedence
  decision (FR-020): the `[Version]` attribute wins; else a valid `AssemblyInformationalVersion`; else
  the 4-part assembly version's `Major.Minor.Build` → `MAJOR.MINOR.PATCH`. Invalid attribute semver /
  unresolvable assembly version raise domain-scoped exceptions carrying the activity type + offending
  value (§2.23.5).
- **`ActivityTypeCategoryResolver`** (`IActivityTypeCategoryResolver`) — pure category decision: the
  last dot-separated segment of the declaring assembly's simple name (e.g.
  `Elsa.Runtime.Activities.Primitives` → `Primitives`), so every activity an author ships in one
  assembly shares one catalog bucket.
- **`ClrActivityReconciliationFeature`** — `IShellFeature` that registers the options plus the two
  resolvers, the scanner, and the source — each against its contract so an inheriting feature can
  replace one in isolation (§2.5). `ConfigureServices` is `virtual`; options and the default activated
  type-capability snapshot are singleton, and executing sources/resolvers are scoped (§2.5.1). It also
  contributes `IActivitySerializedShapeSource` and composes the base Expressions feature for selectable
  type contracts, without requiring an expression language. It does **not** derive from the reconciliation feature.

## Outgoing serialized member queries

The metadata contribution describes an exact admitted CLR activity result/projection, including typed
whole results without named Output declarations. Boundary/variable/current-item types are admitted
through the same configured selectable type and capability catalogs in API and endpoint-free hosts.
No alias is installed or resolved through Runtime. The caller retains resource and disclosure authority.

Type admission alone does not establish serialized member names for a Workflow input, variable or current
item. Request seeding preserves caller JsonElement keys and uses default System.Text.Json for CLR values
(`RuntimeWorkflowStateSeed.Serialize`); expression materialization also uses default JSON options. Those
keys can differ from the activity completion profile. This contribution returns Unknown with
`activity.shape.serialization-profile-unbound`, retaining an admitted whole type but no selected type
for a nonempty member path. Literal exact-key reads remain available with deferred fit; no source is
normalized or case-folded. A future owner with an authoritative schema/profile can contribute the same port.

For an admitted activity result, the reader uses the unchanged activity-value profile and metadata-only
serialized names, ignores, conditional omission, outgoing nullability and collection facts. It constructs
no DTO, attribute or converter and invokes no getter. Unsupported or opaque contracts remain unknown; missing or mismatched source
assets remain unavailable. Limits bound the returned depth and one global member budget. Literal path
segments retain dots and empty names, and selected subtrees preserve nullable/optional prefix facts.

Profile/source/metadata revisions are separate from existing catalog hashes. Metadata revisions cover
consulted declarations, inherited/interface/attribute assets and framed returned facts; file/module
identity checks refuse changed assets behind an already loaded selectable type. Existing scanner
catalog projections and input nullability WriteState remain unchanged. The shape query needs the existing
catalog hasher to verify exact result-source projections; without that authority it returns unavailable.

## What the scanner reads from an activity

The runtime carries no author-supplied UI metadata, so the scanner reads only structural shape plus
the two values derived from assembly/type identity:

- the CLR type **full name** → `ActivityTypeKey` (excludes assembly identity, FR-022);
- public plain CLR properties annotated with `[ActivityInput]` → `InputDefinition` (the attribute's
  `Key`, or the property name when omitted, becomes `ReferenceKey`; the property type becomes the
  input type);
- public properties annotated with `[Output]` on the `TResult` contract declared through
  `IActivityResult<TResult>` (including `Activity<TResult>` and stateful activity bases) →
  `OutputDefinition` (the attribute's `Key`, or the property name when omitted, becomes
  `ReferenceKey`);
- the `[Required]` attribute on an input property → `IsRequired`; output requiredness comes from
  `[Output(IsRequired = ...)]`;
- `[ActivityInput(IsSensitive = true)]` and `[ActivityInput(IsCredential = true)]` → the input's `IsSensitive` and
  `IsCredential`, `true` when declared and null otherwise, so an input that declares nothing keeps its catalog hash; a
  credential is recorded as sensitive too. A credential declaration that could never be bound to a secret reference
  fails the scan, naming the type and the input: one with a `DefaultValue`, one on a property that is not a `string`,
  one on an input the type names in `[RefusesSecretBinding]` (read for this check only, never written to the catalog),
  and any on a type implementing `IRuntimeActivityCheckpointParticipant` (matched by name);
- `[ReplacesActivityType("…")]` on the class itself (not inherited) → design facet `elsa.replaced-activity-types` v1,
  payload `{ "typeNames": [...] }` (trimmed, distinct, ordinally sorted). It records which type names from another
  system or generation the activity replaces; the Elsa 3 converter reads it to find a rebuilt custom activity. With no
  declaration there is no facet, so existing catalog hashes are unchanged. The facet is part of the version's content
  hash: adding or removing a declaration needs a new activity version (otherwise reconciliation refuses the changed
  content), and a converter reading only the highest version stops honouring a removed declaration;
- the resolved semver (above);
- the resolved category, derived from the declaring assembly's name (above).

`DisplayName` and `Description` are left null — they are author-supplied design-time concerns, not
something the runtime assembly is allowed to dictate. `Category` is the exception: it is *derived*
from assembly identity (not an author UI string), so it is structural metadata the scanner may set.

## Registration

Compose the feature alongside `ActivitiesDesignReconciliationFeature`; the universal
`CollectActivityVersions` discovers the source from DI:

```csharp
shell.AddFeature(new ClrActivityReconciliationFeature
{
    Options = { FolderPath = "/path/to/activity/plugins" }
});
```

When the Publishing bridge is composed, one source-owned version is committed atomically as catalog
version, provider-source authority, immutable executable template, publication, layout, and source
reference. Reconciliation never reclassifies CLR-discovered content as Design-owned authoring. Repeating
an identical scan is a no-op; if a matching catalog version predates publication, the same bridge heals
the missing publication artifacts without changing the version identity.

## Options

- **`ClrReconciliationOptions.FolderPath`** — the folder scanned for activity-bearing assemblies.
- **`ClrReconciliationOptions.SourceId`** — the source identity recorded on every contributed row.
  When unset it defaults to the normalised `FolderPath` (R3), so two distinct folders are distinct
  sources.

## Constitutional basis

§2.6.1 (DI-source pattern), §E2.2 (the `…Reconciliation.Clr → Elsa.Activities.Runtime.Core`
Design→Runtime edge is the allowed direction), §E2.8 (author-controlled semver), §2.23.5
(domain-scoped faults).
