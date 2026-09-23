# Effective configuration across runtime and EF tooling

Status: reviewed discovery completed for [#1966](https://github.com/elsa-workflows/elsa-foundation/issues/1966). Approved for specification input; not an implementation contract.

Source baseline: `e738badd1f9ddc2974248079de43d21440ba5afd`. This follows the [reviewed persistence-boundary result](https://github.com/elsa-workflows/elsa-foundation/issues/1965#issuecomment-5798240404). The program remains blocked from implementation until #1967 reconciles both reports.

## Confirmed tooling flow

1. `ElsaCli.WithHostConfiguration` reads `ShellConfiguration.Read(hostDirectory, environment)`.
2. `ShellConfiguration` layers `shells.json`, then `shells.<environment>.json`, using the JSON configuration provider. It supports CShells object-map and array shapes, rejects mixed/duplicate shapes, and respects disabled object-map features. It retains only shell name, feature name, and `Provider` for the worker request.
3. The front end launches its worker inside the target host's dependency closure. The worker has no EF dependency and reaches the host's `EfToolingHost` reflectively; the host's own EF version and engine perform the work.
4. `EfToolingHost` discovers module usages and runs `EfProviderAgreement` against selected modules before constructing contexts or writing artifacts. An unset feature provider is treated as SQLite. An explicit `--provider` remains authoritative, and disagreement is refused.
5. Offline list/plan/script commands do not take a real connection. Live apply/validate/post-migrate commands obtain a connection through the worker's environment/stdin path and must not echo it.

Sources: [shell reader](../../../src/essentials/Cli/ShellConfiguration.cs), [CLI](../../../src/essentials/Cli/ElsaCli.cs), [worker](../../../src/essentials/Cli/Worker/WorkerRunner.cs), [tooling contract](../../../src/essentials/Persistence/EntityFramework/Tooling/EfToolingContract.cs), [tooling host](../../../src/essentials/Persistence/EntityFramework/Tooling/EfToolingHost.cs), [provider agreement](../../../src/essentials/Persistence/EntityFramework/Tooling/EfProviderAgreement.cs).

## Existing architecture boundary

[ADR 0076](../../adr/0076-persistence-tooling-runs-inside-the-host-closure.md) deliberately keeps the CLI front end free of EF references (D1), treats `--provider` as authoritative and shell-file inspection as distinct from the live host environment (D4), and permits connection credentials through environment/stdin only (D7). The new program must not silently reinterpret those rules.

One effective configuration means equivalent explicit input contexts produce equivalent results. It cannot mean assuming the CLI process's environment is the running host's environment. A proposal to add resource-aware context/snapshot resolution must identify its inputs, provenance, secret boundary, and any required ADR amendment before implementation. Existing command behavior needs a compatibility path.

The existing provider-only worker projection loses resource declarations and binding intent before the host-side checker can inspect them. Resolution must happen before that loss, or the protocol must carry a suitable explicit context for host-side resolution. The front end must not grow a competing EF resolver.

## Executable disagreement probe

Executed 2026-09-23 against the production `EfProviderAgreement.Check`, with a synthetic usage record for `WorkflowsRuntimeEntityFrameworkCore` / `Workflows.Runtime`:

| Input given to the checker | Requested provider | Observed result |
|---|---|---|
| Feature provider absent | PostgreSql | One disagreement: the effective legacy default is SQLite |
| Feature provider materialized as PostgreSql | PostgreSql | No disagreement |
| Feature provider explicitly Sqlite | PostgreSql | One disagreement retained |

All three assertions passed. The ephemeral probe project referenced the source `Elsa.Persistence.EntityFramework.csproj`; it did not implement a resolver, open a database, or launch a complete host. The example establishes why an unresolved resource document cannot simply pass through the current checker. It does not prove a new resource contract.

Probe command used: `dotnet run --project /tmp/runtime-composition-1966-probe/Probe.csproj`. The source is a small direct call sequence over `EfFeatureModuleUsage` and `EfProviderAgreement.Check`, with counts asserted as 1, 0, and 1 respectively; no credentials were used or printed.

Reproducible probe source (console project targeting net10.0 with a project reference to `src/essentials/Persistence/EntityFramework/Elsa.Persistence.EntityFramework.csproj`):

```csharp
using Elsa.Persistence.EntityFramework.Tooling;

const string feature = "WorkflowsRuntimeEntityFrameworkCore";
const string module = "Workflows.Runtime";
EfFeatureModuleUsage[] usages = [new(feature, typeof(ProbeFeature), [module], true)];
var unresolved = EfProviderAgreement.Check([("Default", feature, (string?)null)], usages, [module], "PostgreSql");
var effective = EfProviderAgreement.Check([("Default", feature, (string?)"PostgreSql")], usages, [module], "PostgreSql");
var conflict = EfProviderAgreement.Check([("Default", feature, (string?)"Sqlite")], usages, [module], "PostgreSql");
if (unresolved.Count != 1 || effective.Count != 0 || conflict.Count != 1)
    throw new InvalidOperationException("Provider agreement probe did not match the inspected contract.");
Console.WriteLine("PASS: unresolved per-feature default disagrees with central PostgreSql selection.");
Console.WriteLine("PASS: materialized PostgreSql provider agrees before tooling validation.");
Console.WriteLine("PASS: explicit conflicting Sqlite provider remains refused.");
Console.WriteLine("Scope: production EfProviderAgreement with synthetic usage/input; no database or complete host proof.");

internal sealed class ProbeFeature;
```

## Confirmed startup and management flow

Workbench appends shell JSON, environment-specific shell JSON, environment variables, then command-line arguments after the default application builder. Later providers win. Foundation Host instead appends only `shells.json` after its default builder providers; do not silently change that host's legacy precedence. Before CShells binds feature objects, configuration keys still exist. After binding, an initialized `Provider = "Sqlite"` cannot distinguish omission from an explicit choice. Raw source adapters must preserve property presence, including explicit null; a merged scalar value alone is insufficient.

Sources: [Workbench startup](../../../src/apps/Elsa.Workbench/Program.cs), [Foundation Host startup](../../../src/apps/Elsa.Foundation.Host/Program.cs), [Runtime feature defaults](../../../src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/RuntimeEntityFrameworkCoreFeature.cs), [Workbench precedence test](../../../tests/essentials/Architecture/WorkbenchConfigurationTests.cs).

The management path is different:

1. Load the authored base JSON snapshot; build and mask the catalog.
2. Restore secret placeholders from that stored snapshot; validate revision, full desired state, and declared property types.
3. Run activation guards on the restored request.
4. Save the enabled feature configurations; refresh the catalog; reload the shell.

The store reads one file, not the effective startup overlays. Its snapshot includes the shell's own `Configuration` object, but its revision covers only raw `Features`, including secrets. Changing shell defaults or external overlays does not change that revision. The EF migration guard uses the snapshot's migration-policy section when present, otherwise host configuration. That is not proof that it sees the runtime's merged policy.

A save rereads and checks the revision, then writes directly; it is not an atomic compare-and-swap. Refresh/reload failure after save leaves the authored file changed. These are existing limitations, not guarantees the new resolver may claim to fix. A resource-aware apply path must validate the effective candidate and detect resource/source drift before claiming successful activation. The later operations epic owns general recoverable apply; #1967 must decide the minimum safe integration or explicit refusal for the first resource-mode management write.

Sources: [management service](../../../src/essentials/Modularity/Nuplane/Services/FeatureManagementService.cs), [JSON store](../../../src/essentials/Modularity/Api/Services/JsonShellFeatureConfigurationStore.cs), [snapshot](../../../src/essentials/Modularity/Core/Models/ShellFeatureConfigurationSnapshot.cs), [EF activation guard](../../../src/essentials/Modularity/EntityFramework/EfPendingMigrationActivationGuard.cs), [shell reloader](../../../src/essentials/Modularity/Api/Services/ShellReloader.cs).

Unknown configured settings are masked conservatively and restored on round-trip when the placeholder is retained. Unknown non-placeholder fields are preserved as JSON. Explicit false and zero survive; null clears a stored secret; omission removes a property; a placeholder restores it. Masking is at the setting-property level, not recursively inside a non-secret object. Disabled feature configuration is not retained by the current store. Its permissive scalar-to-empty-object behavior also differs from the CLI's scalar refusal. Preserve these distinctions in legacy characterization; resource mode should refuse malformed inputs consistently rather than silently coerce them.

Sources: [secret masking/restoration](../../../src/essentials/Modularity/Nuplane/Services/SecretSettingMask.cs), [store tests](../../../tests/essentials/Modularity/Tests/JsonShellFeatureConfigurationStoreTests.cs), [management tests](../../../tests/essentials/Modularity/Tests/FeatureManagementServiceTests.cs), [mask tests](../../../tests/essentials/Modularity/Tests/SecretSettingMaskTests.cs).

## Recommended ownership and resolution seam

**Proposed decision for #1967:** one pure persistence resolver, owned by Elsa's persistence integration, consumes an explicit authored input context and returns effective participant settings plus redacted provenance/refusals. EF-owned adapters supply enrollment and shared-context/transaction constraints from #1965. Keep module-owned contexts and provider engines unchanged. This is a bounded persistence contract, not ratification of framework-wide settings classification.

Use thin entry-point adapters:

| Entry point | Input context and integration point | Result consumer |
|---|---|---|
| Runtime startup/reload | Host's actual configured source chain, shell identity, raw presence, loaded participant metadata; resolve before CShells feature object binding | Materialized feature configuration before EF registration |
| Management preview/apply | Stored candidate with restored secrets plus the same runtime source context; preserve external overrides and distinguish authored from effective values | Existing guards receive effective settings; persist authored intent, not generated inherited fields |
| EF tooling | Explicit target-host file/environment context, or a deliberately supplied equivalent context; transport sufficient authored data to the host closure before the current provider-only projection loses it | Host-side resolver then provider agreement, followed by the selected tooling operation |

A secret-bearing trusted execution result and a redacted explanation are different projections. Resolve a connection reference only inside the trusted execution boundary; never embed its value in a public plan, diagnostic, manifest, or command argument. A configuration-only plan can validate reference syntax/existence when its context supplies that information, but must report unresolved prerequisites when it cannot. Live target equality and migration readiness require separate checks.

The front end and worker remain EF-free. Extend the host tooling protocol deliberately, including version negotiation/refusal for older hosts. Preserve `--provider` authority, explicit file-only context reporting, and environment/stdin credentials. Equivalent input contexts must produce the same effective settings; unlike input contexts must be reported as different, not claimed equivalent. Live tooling currently receives one connection per invocation, so diagnostics on a second target requires explicit module/target selection and independent validation, not silently migrating all modules against one supplied connection.

A startup adapter can wrap/materialize the configuration supplied to `WithConfigurationProvider` without changing existing feature APIs. It must rerun on configuration/shell/package reload and preserve configuration-source precedence. An initial frozen in-memory overlay is insufficient. A CShells-native pre-binding callback would provide a cleaner lifecycle hook but introduces external API/version work; no suitable existing hook was established in this spike. #1967 must verify the lifecycle hook against the actual CShells implementation or an executable reload probe before declaring an implementation story ready. Activation guards alone are too late to own ordinary startup resolution, and shell initialization occurs after service registration.

Planning performs no package installation, database access, file save, or shell reload. Metadata discovery/loading, connection resolution, live validation, and apply are explicitly separate stages. Reuse provider/package and migration guards without describing either as proof of physical layout validity.

## Proposed precedence, presence, and compatibility matrix

This is specification input. Exact JSON paths and public type names remain for #1967.

| Authored case | Proposed outcome | Compatibility / evidence obligation |
|---|---|---|
| No resource mode | Existing per-feature provider, connection, schema, pooling, and migration-policy behavior | No reinterpretation of initialized CLR defaults or host source order |
| Resource definitions but no selection | No implicit resource choice | Unselected definitions do not rewrite legacy consumers |
| Enrolled consumer, shell default, no feature binding or legacy target fields | Inherit selected resource's provider and connection reference atomically | Materialize both before feature binding and provider agreement |
| Explicit feature binding plus shell default | Feature binding wins as one complete target | No provider from one resource paired with another connection |
| Feature binding removed | Inherit shell default; otherwise return to legacy path | Removing a binding does not delete unrelated settings |
| Binding null/empty/unknown, missing selected resource, missing resource provider/reference | Refuse malformed or unresolved new-mode selection | Never silently fall back to SQLite; distinguish removal from malformed selection |
| Authored legacy Provider/ConnectionString/ConnectionName plus an applicable new resource selection | Refuse ambiguous ownership and identify feature/source fields | Do not guess intent, even if values happen to agree; migration makes the choice explicit |
| Explicit legacy fields on a feature outside any applicable resource selection | Preserve legacy semantics | Unknown/private consumers are not enrolled by resemblance of property names |
| Explicit missing/blank connection-name target | Refuse, retaining existing strict lookup | Do not replace it with a default connection or SQLite file |
| Resource provider overridden by a higher-priority source | Recompute the resource target and revalidate provider/connection together | Source precedence cannot bypass ambiguity or supported-layout checks |
| Feature false; setting false or zero | Feature disabled; setting values remain explicit | Do not use truthiness as presence; disabled settings remain authored data in a future lossless editor |
| Existing property null, empty, or absent | Preserve raw distinction until the owning option's legacy rules apply | New binding null is invalid; ordinary nullable legacy settings keep legacy handling |
| Secret placeholder in management request | Restore from trusted authored state before planning; absent prior value stays absent | Placeholder is not a connection value or exportable secret reference |
| Unknown imported property/feature | Preserve authored data; report unresolved enrollment/capability where needed | Do not claim a custom layout ready or erase its settings |
| Schema/pooling explicitly authored | Preserve these independent options and validate shared-context agreement | Named resource first slice defines provider/connection only; no new schema or migration-policy inheritance |
| SQLite/MySQL schema or shared-context mismatch | Apply existing provider-specific rules and EF-owned constraints | SQLite ignores schema; MySQL refuses schema; Runtime participants must agree on context options |
| Migration policy | Retain current host/shell policy and Validate/AutoMigrate behavior | Resource selection grants no migration permission; reconcile current Secrets behavior from #1965/#1900 |
| OpenIddict / host-owned or private store | Explicitly outside automatic enrollment | #1895 owns vendor-provider work; diagnostics and IAM enrollment require their reviewed boundaries |
| Runtime and tooling use different source contexts | Explain checked sources and unverified parity | Never label file-only agreement as live-host agreement |

Resource-mode conflicts must be checked against authored presence before materialization; a CLR SQLite default is not an authored conflict. Layered configuration adapters must retain enough source information to explain and consistently resolve null/removal/array behavior. A generic merge of already-bound options cannot implement this matrix.

## Verification inventory and required first-slice evidence

Reusable source tests (identified, not all executed in this spike):

- [ShellConfigurationTests](../../../tests/essentials/Cli/Tests/ShellConfigurationTests.cs): file overlay, enabled features, provider extraction and shape refusals.
- [FeatureManagementServiceTests](../../../tests/essentials/Modularity/Tests/FeatureManagementServiceTests.cs): full request, secret restoration, guard ordering and refusal before save.
- [JsonShellFeatureConfigurationStoreTests](../../../tests/essentials/Modularity/Tests/JsonShellFeatureConfigurationStoreTests.cs): snapshot, round-trip and revision scope.
- [SecretSettingMaskTests](../../../tests/essentials/Modularity/Tests/SecretSettingMaskTests.cs): unknowns and null/empty/false/zero preservation.
- [EfMigrateOptionsTests](../../../tests/essentials/Persistence/EntityFramework/Tests/EfMigrateOptionsTests.cs): host/shell migration policy.
- [EfPendingMigrationActivationGuardTests](../../../tests/essentials/Modularity/EntityFramework/Tests/EfPendingMigrationActivationGuardTests.cs): restored secrets, provider/connection and policy checks.
- [WorkbenchConfigurationTests](../../../tests/essentials/Architecture/WorkbenchConfigurationTests.cs): configuration-provider ordering.
- The #1965 report records the 38 executed legacy persistence checks and their exact scope; they do not prove resource mode.

Required acceptance evidence for #1968/#1969, refined into executable tests by #1967:

1. Shared PostgreSQL host: Runtime, Workflows Design, Activities Design and Publishing inherit a single target. Design, publish, execute, restart and read persisted state; inspect actual database data and migration histories.
2. Runtime/tooling parity: identical explicit contexts produce matching providers and connection targets before checks; CLI runs inside the built host closure. Demonstrate the unresolved-SQLite regression fails without materialization. A mismatched explicit provider remains refused.
3. Source and presence: base file, environment file, env and CLI overrides; missing/null/false/zero; conflicting legacy fields; unknown settings; shape errors. Characterize legacy Workbench and Foundation Host separately.
4. Reload: changing/removing a binding recomputes effective settings without flattening inheritance into the authored file; external overrides remain authoritative; stale candidate/resource context cannot be reported successfully applied. No unintended database operation occurs on refusal.
5. Secret boundary: representative secrets never appear in plan/export/refusal/stdout/stderr/argv; masked management values restore before validation. Live connection input stays env/stdin and file-only inspection remains honestly scoped.
6. Diagnostics split: both diagnostics consumers use the second database; other participants retain the primary; data survives restart. Tooling targets each module set independently and known shared-context/transaction conflicts are refused.
7. Legacy policy and exceptions: missing explicit connection references still refuse; provider/schema/pooling and migration policy retain behavior; OpenIddict remains host-owned. Existing #1902 guard gaps are coordinated rather than duplicated.

No new resource-mode host, database, reload, management-apply, or full CLI behavior has been implemented or proven by this spike. Exact CShells runtime binding exception messages remain unverified; inspected CLI refusals are not substituted for runtime evidence.

## Executed verification and review

On 2026-09-23, the following focused existing test selection passed **63 tests, zero failures, zero skipped**:

```sh
dotnet test tests/essentials/Modularity/Tests/Elsa.Modularity.Tests.csproj --filter 'FullyQualifiedName~FeatureManagementServiceTests|FullyQualifiedName~JsonShellFeatureConfigurationStoreTests|FullyQualifiedName~SecretSettingMaskTests' --logger 'console;verbosity=minimal'
```

This verifies existing management, store and secret-mask behavior only. The build emitted existing analyzer/compiler warnings; no production source changed. The three production provider-agreement assertions above also passed. All report-relative links resolve and the diff whitespace check passes. No database was opened by the disagreement probe and no resource-mode host behavior is claimed.

Independent review (Sol 5.6 High) approved closure of #1966 with no blocking findings. It specifically checked ADR 0076, authored conflict semantics, explicit-context parity, reload uncertainty and management revision limitations. Root reviewed the supporting source investigations and retained the outstanding implementation gates below.

## Specification checkpoint and decision ledger

**Go for #1967 specification; no-go for production implementation until its review closes these decisions.** The supported layout and shared-resolution ownership are concrete enough to specify. Spike closure does not settle every API/lifecycle choice.

| Decision / remaining investigation | Owner and gate |
|---|---|
| Exact authored resource/binding shape, enrollment identity, presence and conflict contract | #1967; reviewed spec before #1968 |
| Lifecycle seam that recomputes before CShells binding on startup and reload | #1967; inspect framework source or execute bounded hook proof, use a linked spike only if not resolvable within specification |
| Host-closure protocol extension, explicit context identity and safe live target selection | #1967; review against ADR 0076 D1/D4/D7, amend ADR if new behavior extends its contract |
| Minimal safe management resource-mode behavior and revision scope | #1967; do not claim read/save/reload consistency without proof; broader durable operations stay #1964 |
| Shared-context/transaction validation and diagnostics enrollment | #1967 consumes #1965; real acceptance belongs #1968/#1969; coordinate #1902 |
| Framework settings taxonomy | Deferred §2.12 stays deferred; bounded persistence contract must not silently ratify a general policy |
| OpenIddict provider and current Secrets migration-policy issue reconciliation | Existing #1895/#1900; no automatic inclusion or stale workaround |

The next action is to turn both reviewed reports into the repository's specification, plan and tasks, with the above acceptance evidence assigned to the two existing stories. Do not create a parallel generic settings framework or a speculative all-module backlog.
