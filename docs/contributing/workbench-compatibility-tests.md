# Classic Workbench compatibility tests

Stock Workbench uses Actor Foundation. Run previous classic execution witnesses in
the matching immutable source checkout. They start owned child processes and use
disposable test databases. The [Workbench compatibility guidance](../docker.md#workbench-mixed-preview-compatibility)
identifies the earlier installed image for operator drain and container smoke.

Create the two isolated checkouts from a repository that contains the commits.

```sh
compatibility_root="$(mktemp -d)"
git -c core.fsmonitor=false worktree add --detach "$compatibility_root/workbench-overlay" e758c2cc86ccdb4294be0aa63bdfe933060db75e
git -c core.fsmonitor=false worktree add --detach "$compatibility_root/workbench-classic-default" 18a16dee878106ac525aa9a861d851a200e43c55
```

The first revision supplies the explicit classic test shell for the Agent,
Compensation, Background Activities and BPMN apphost tests. Run each command
from that checkout so the test and host use the same source and configuration.

```sh
(
  cd "$compatibility_root/workbench-overlay"
  dotnet test essentials/tests/Workbench/Tests/Elsa.Workbench.Tests.csproj -c Release -p:RestoreLockedMode=true \
    --filter 'FullyQualifiedName~WorkbenchAgentRuntimeTests|FullyQualifiedName~WorkbenchCompensationRecoveryTests|FullyQualifiedName~WorkbenchBackgroundActivityRecoveryTests'
  python3 tools/bpmn/restore_evidence.py
  dotnet test extensions/Bpmn/tests/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj -c Release -p:RestoreLockedMode=true
)
```

The archived BPMN suite contains its [capture and qualification guide](https://github.com/elsa-workflows/elsa-foundation/blob/e758c2cc86ccdb4294be0aa63bdfe933060db75e/extensions/Bpmn/tests/Hosting/Tests/README.md).
Run its apphost upgrade and evidence commands from the same pinned checkout.
The current suite still tests the independent read-only SQLite snapshot inspector.

The ETL and Rules apphost fixtures rely on the classic development shell as
their starting configuration. Use the second checkout for the held delivery
and restart tests.

```sh
(
  cd "$compatibility_root/workbench-classic-default"
  dotnet test extensions/Etl/tests/Hosting/Tests/Elsa.Etl.Hosting.Tests.csproj -c Release -p:RestoreLockedMode=true
  dotnet test extensions/Rules/tests/Hosting/Tests/Elsa.Rules.Hosting.Tests.csproj -c Release -p:RestoreLockedMode=true
)
```

Current test tiers still run stock Workbench process, authoring and configuration
checks, along with the independent Agent, Compensation, BPMN, ETL and Rules
module and provider suites.

The optional Studio live compensation, nesting and operator journeys require an
owned classic Workbench and matching Studio. Set `ELSA_CLASSIC_COMPENSATION_LIVE=1`
with `ELSA_STUDIO_URL` when running them against that pair.

## Standalone trigger browser journeys

The historical seven-appearance harness includes direct BPMN hosting and classic
Runtime EF setup and checkpoint witnesses. Use the pinned classic-default
checkout above and its matching Studio dependencies and Chromium. Run the full
suite into a new evidence directory.

```sh
(
  cd "$compatibility_root/workbench-classic-default"
  pnpm --dir studio install --frozen-lockfile
  pnpm --dir studio exec playwright install chromium
  python3 tools/ux-acceptance/standalone.py --output "$compatibility_root/standalone-trigger-evidence"
)
```

For a single reported journey, pass `--journey` using the title from the pinned
runner. Use `--studio-shape application` for the ordinary Studio application or
`--studio-shape coordinated-sdk` for the independently enrolled SDK consumer.
The source SHA in the report belongs to the classic checkout. Stock Workbench
uses the Actor Foundation source and schedule witnesses for the supported engines.

The former nightly used the application for six journeys and the coordinated SDK
consumer for access. Replay the access part explicitly with the same checkout.

```sh
(
  cd "$compatibility_root/workbench-classic-default"
  python3 tools/ux-acceptance/standalone.py --output "$compatibility_root/standalone-trigger-sdk-access" \
    --journey 'standalone access and availability' --studio-shape coordinated-sdk
)
```

## Classic composed Studio and Decision runtime

The classic composed browser runner covers task forms and approvals, Decision
activities, BPMN and compensation execution, the old run-health projection,
and the classic history, playback and process restart routes. Use its matching
Workbench and Studio in the same classic-default checkout. Add `--journey` to
select a specific title listed by the archived runner. The Decision runtime
probe also qualifies an actual restart of a workflow waiting on the classic
checkpoint implementation.

```sh
(
  cd "$compatibility_root/workbench-classic-default"
  python3 tools/ux-acceptance/run.py --output "$compatibility_root/composed-studio-classic"
  python3 tools/decisions/qualify-execution.py --output "$compatibility_root/decision-execution-classic"
)
```

## Classic BPMN direct execution

The classic BPMN activity host, its embedded `RunBpmn` activity and its
direct-run fixtures use the previous execution core. Run the original
module tests and the checked workflow-history witness from the same pinned
source. Keep the original Workbench image, package versions and data for
existing deployments.

```sh
git -c core.fsmonitor=false worktree add --detach "$compatibility_root/classic-bpmn" ef70e5acd7fa8b07cfad4a6c0ca9a229a039f20a
(
  cd "$compatibility_root/classic-bpmn"
  python3 tools/bpmn/restore_evidence.py
  dotnet test extensions/Bpmn/tests/Tests/Elsa.Activities.Bpmn.Tests.csproj -c Release -p:RestoreLockedMode=true
  dotnet test essentials/tests/Activities/Behavioral/Tests/Elsa.Activities.Behavioral.Tests.csproj -c Release -p:RestoreLockedMode=true
  dotnet test essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj -c Release -p:RestoreLockedMode=true \
    --filter FullyQualifiedName~RuntimeHistoryBpmnOrderingTests
)
```

Use the last source before the shared classic BPMN fixture was retired for the
linked Runtime EF called-workflow control, checkpoint upgrade and combined BPMN
child outcome tests. The upgrade tests extract and verify the SQLite snapshots
tracked in that checkout under `specs/1006-bpmn-library-adoption/evidence/legacy`.

```sh
(
  git -c core.fsmonitor=false worktree add --detach "$compatibility_root/classic-bpmn-ef" bc616099bd58d75e962ce6cb9f53c8f68c2e772e
  cd "$compatibility_root/classic-bpmn-ef"
  dotnet test essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj \
    -c Release -p:RestoreLockedMode=true --filter 'FullyQualifiedName~BpmnCalledWorkflowControlEfTests|FullyQualifiedName~BpmnUpgradeRecoveryEfTests|FullyQualifiedName~Bpmn_combined_outcome'
)
```

The main BPMN test project in this checkout also runs the archived classic
checkpoint/child/compensation journeys. For the actual BPMN CallActivity REST
witness, run the following script from the classic-default checkout above
against its matching owned Workbench URL:

```sh
pwsh -NoProfile -File e2e-tests/bpmn/Test-BpmnCallActivity.ps1 -BaseUrl "$classic_base_url"
```

Current source keeps BPMN structure, Design and XML interchange packages and
their authoring tests. Its retained BPMN publication tests exercise provenance
without starting a classic workflow.

## Classic executable import and HTTP response replay

The classic artifact importer accepts portable executable closures and can start their
workflows in a runtime-only host. The classic HTTP response replay tests use that importer
and a disposable process fixture to verify recovery after process loss. Use the same source
for the producer, importer, and fixture.

```sh
(
  git -c core.fsmonitor=false worktree add --detach "$compatibility_root/classic-artifact-http" ef70e5acd7fa8b07cfad4a6c0ca9a229a039f20a
  cd "$compatibility_root/classic-artifact-http"
  dotnet test essentials/tests/Workflows/Runtime/Reconciliation/Tests/Elsa.Workflows.Runtime.Reconciliation.Tests.csproj \
    -c Release -p:RestoreLockedMode=true
  dotnet test essentials/tests/Workflows/Publishing/Tests/Elsa.Workflows.Publishing.Tests.csproj \
    -c Release -p:RestoreLockedMode=true --filter FullyQualifiedName~WorkflowArtifactExportImportExecutionTests
  dotnet test extensions/Http/tests/Activities/IntegrationTests/Elsa.Activities.Http.IntegrationTests.csproj \
    -c Release -p:RestoreLockedMode=true --filter 'FullyQualifiedName~ResponseReplaySafetyProcessTests|FullyQualifiedName~ResponseReplayObservationTests'
)
```

Current source retains the Publishing executable export and material checks, the Design
JSON workflow reconciliation path, and the Actor HTTP provider witnesses.

## Classic reviews, Decisions, ETL and Rules in workflows

`AgentReview`, `AgentReviewApi` and the Decisions, ETL, and Rules workflow
adapters use the previous runner. Use the immutable sources below for those
workflow activities and preserve any existing authored definitions, published
material, and deployment data with their original host. Current source keeps
the independent Agent and Decisions services, ETL pipelines and workers,
Rules services, and their provider and Studio modules.

The classic-default Workbench checkout above carries the matching API,
publication, and runtime features. From that checkout, run the Decision
browser and execution commands above, or the ETL HTTP and file process witnesses:

```sh
(
  cd "$compatibility_root/workbench-classic-default"
  dotnet build server/apps/Elsa.Workbench.Host/Elsa.Workbench.Host.csproj -c Debug -p:RestoreLockedMode=true
  pwsh -NoProfile -File e2e-tests/etl/Test-FiniteFileEtl.ps1
  pwsh -NoProfile -File e2e-tests/etl/Test-FiniteHttpEtl.ps1
)
```

The workflow adapter tests and the ETL frozen upgrade runner are available at
the final source revision before this retirement. Use the framework and stores
from that same revision:

```sh
git -c core.fsmonitor=false worktree add --detach "$compatibility_root/classic-workflow-adapters" af562301713e49d879c7c0bf362efdbfaeb8a94e
(
  cd "$compatibility_root/classic-workflow-adapters"
  dotnet test extensions/Agent/tests/Tests/Elsa.Agent.Tests.csproj -c Release -p:RestoreLockedMode=true \
    --filter 'FullyQualifiedName~Editorial|FullyQualifiedName~HumanReview'
  dotnet test extensions/Decisions/tests/Workflows/Tests/Elsa.Decisions.Workflows.Tests.csproj -c Release -p:RestoreLockedMode=true
  dotnet test extensions/Decisions/tests/Workflows/Api/Tests/Elsa.Decisions.Workflows.Api.Tests.csproj -c Release -p:RestoreLockedMode=true
  dotnet test extensions/Etl/tests/Tests/Elsa.Etl.Tests.csproj -c Release -p:RestoreLockedMode=true \
    --filter 'FullyQualifiedName~EtlNative|FullyQualifiedName~EtlPublishedWorkflow|FullyQualifiedName~EtlWorkflowPublication|FullyQualifiedName~RunEtl'
  dotnet test extensions/Rules/tests/Tests/Elsa.Rules.Tests.csproj -c Release -p:RestoreLockedMode=true \
    --filter 'FullyQualifiedName~RulesNative|FullyQualifiedName~RulesPersisted|FullyQualifiedName~RulesCompiledPublication|FullyQualifiedName~RulesPublication|FullyQualifiedName~RulesWorkflows|FullyQualifiedName~RulesCheckpoint|FullyQualifiedName~RulesIntent|FullyQualifiedName~RunRules'
  python3 extensions/Etl/tests/Shared/test-v1-native-upgrade.py --evidence-root "$compatibility_root/etl-v1-native-upgrade"
)
```

The package-backed Rules Workbench witness has its own matching source and
publish-state pins. Follow the immutable input recipe in the
[Rules acceptance quickstart](../../specs/999-durable-rules/quickstart.md).

## Classic BPMN delivery inspection and recovery probes

Use the classic-default checkout above for the preflight validator, checked
snapshot reader, and the Workbench build that includes the recovery probe.
From that checkout:

```sh
(
  cd "$compatibility_root/workbench-classic-default"
  dotnet build tools/bpmn/DeliveryPreflight/DeliveryPreflight.csproj -p:RestoreLockedMode=true
  dotnet build tools/bpmn/DeliveryRecoveryRead/DeliveryRecoveryRead.csproj -p:RestoreLockedMode=true
  python3 -m unittest discover -s e2e-tests/bpmn -p test_inspect_broadcast_delivery.py -v
)
```

The [broadcast recovery commands](../../e2e-tests/bpmn/RecoveryProbe/README.md)
build the matching probe and standard Workbench. Run the model broadcast,
rollback, signal interruption, and native Blocks controllers from that same
source. Use the matching closed snapshots and original evidence receipts.

For the final deterministic snapshot-reader tests, including corrupt-copy
controls, use the last source revision that still built the reader and linked the
closed snapshot into the tests:

```sh
git -c core.fsmonitor=false worktree add --detach "$compatibility_root/classic-bpmn-reader" 45a594857c700de2fe76de9cbcb62d06bbf6876b
(
  cd "$compatibility_root/classic-bpmn-reader"
  python3 tools/bpmn/restore_evidence.py
  dotnet test extensions/Bpmn/tests/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj \
    -c Release -p:RestoreLockedMode=true --filter FullyQualifiedName~ReadOnlyRuntimeSnapshotTests
)
```

Current source keeps evidence restoration, the offline capture and upgrade receipt
verifiers, and source comparison. BPMN document authoring and XML interchange keep
their existing suites.
