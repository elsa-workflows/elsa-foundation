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
  dotnet test extensions/Bpmn/tests/Tests/Elsa.Activities.Bpmn.Tests.csproj -c Release -p:RestoreLockedMode=true
  dotnet test essentials/tests/Activities/Behavioral/Tests/Elsa.Activities.Behavioral.Tests.csproj -c Release -p:RestoreLockedMode=true
  dotnet test essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj -c Release -p:RestoreLockedMode=true \
    --filter FullyQualifiedName~RuntimeHistoryBpmnOrderingTests
)
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
