# Constitution review checklist

The constitution gates that no test or analyzer checks. A reviewer, or a plan's Constitution Check, walks the lines that the change touches. The gates with an automated check are in each constitution's Gate index: [framework](../../.specify/memory/constitution-framework.md#gate-index) and [Elsa](../../.specify/memory/constitution.md#gate-index). Each line names its section; the constitution text governs where the two differ.

## Framework

- **§2.1** A `.Core` takes only `Microsoft.Extensions.*` Abstractions and `Microsoft.Extensions.Primitives`; an implementation references another only within its provider family, for explicit specialization.
- **§2.2** No layer-marker segment in any package or namespace; `<Variation>` only when needed; a contributor-only feature is named `<App>.<ModelDomain>.<ConsumerDomain>`.
- **§2.3** A type with a recognisable domain is not in `<App>.Primitives`; no eager `<App>.Foundation.Core`.
- **§2.4** A helper belongs to one domain, has no `IFeature`, and no `.Core` references it.
- **§2.5** `ConfigureServices` is `virtual`; a collaborator that is an extension point is registered against a contract.
- **§2.5.1** Services are scoped unless they hold an application-wide static value or a documented reason requires a singleton.
- **§2.6** No cross-feature dependency on another implementation's side effects or internals.
- **§2.6.1** Sequential is unshielded; a reading-back publisher uses Sequential; handlers are independent; a fan-in uses a contributor interface and one aggregating handler; the suffix matches the shape; contribution points are named events.
- **§2.6.2** A replacement contract declares its kind, and a second registration is prevented or diagnosed at startup.
- **§2.6.3** No dependency hidden behind anonymous dispatch; nothing is read back from a Background publish.
- **§2.6.4** A surface with design-time and runtime consumers is two contracts.
- **§2.6.5** A sync contributor meets all three criteria; reviewers challenge it.
- **§2.6.6** The strategy and failure policy are deliberate; event names carry no `On` prefix; gate/outcome pairs are `Xxxing`/`Xxxed`.
- **§2.7, §2.7.1** A heavy or external dependency sits behind an adapter; the coupling pattern follows the decision rule.
- **§2.8** An extension method over three lines was reviewed; logic-bearing ones moved to an interface or service.
- **§2.9, §2.9.1** No required base `DbContext`; persistence-only fields are real properties hidden from the read interface.
- **§2.10** Persistence commands and queries are separate methods.
- **§2.11** DI diagnostics surface missing and duplicate registrations.
- **§2.13** Packaging cohesion follows dependency cohesion.
- **§2.14** An integration and its consumption-shapes are separate modules; a two-system unit is its own module.
- **§2.16, §2.16.1** NuGet identity is preserved; a new Layer-3 project under about 100 lines is justified or fits an exemption class.
- **§2.17** No shared helper that adds a dependency for fewer than three consumers; `.Core` implementations are thin.
- **§2.18.1–§2.18.3** A new domain's purpose fits one verb-led sentence; a contract another domain or feature would plausibly consume is in the `.Core`; a feature duplicated across domains is promoted to one `.Core`.
- **§2.19** Feature names are stable and unique; a rename is a new feature plus retirement.
- **§2.20** No premature provider umbrella; specific provider sub-packages; no feature depends on a concrete provider unless provider-suffixed (EF Core admission is automated).
- **§2.21.1** Tests describing valid behaviour still pass; a rewritten or deleted test names its reason.
- **§2.22, §2.22.1, §2.22.2** Feature docs list handlers, contributors and tasks; `EXTENSION_POINTS.md` changes in the same unit; the root index links every catalog.
- **§2.23.1, §2.23.2** Logic-bearing wiring has a registration test; risky paths have behaviour tests; no test asserts on text.
- **§2.23.4** Exposed hidden coupling is lifted to a contract, not reproduced in a stub.
- **§2.23.5** Infrastructure exceptions are wrapped in domain exceptions at the boundary.
- **§2.24 (draft)** The change uses a catalogued pattern or raises a new one.
- **§2.25.2, §2.25.3** Only a consolidation review retires artifacts (terminal spec status, superseded guard tests, stale catalog entries, generators without consumers); a removal is backed by build or test evidence, not a census.
- **§4.1, §4.2** The version bump matches the SemVer class of the package's surface change: a removed or renamed member or event, changed signature, required interface member or feature rename is a major.

## Elsa

- **§E2.2.3** Design-only, Runtime-only and combined deployments still work.
- **§E2.5** `ElsaDbContextBase` stays opt-in; no contract requires it.
- **§E2.6** A published artifact always runs; the runtime needs no design-side data.
- **§E2.8** The picker reads only the activity catalog.
- **§E2.9 (provisional)** `WorkflowDefinitionState` gains no out-of-scope member; `ActivityNode` gets no generic child collection, connection list, start-child member or executable mirror (§E2.9.1); State, read models and `WorkflowExecutable` stay separate (§E2.9.3); draft mutation stays one diff-based command.
- **§E5** One version line until GA; `Elsa.Server` stays reserved; Nuplane promotes a domain's `.Core` within its subtree, never host-pinned.
- **§E6** New or renamed Elsa-owned types follow R1–R8.
