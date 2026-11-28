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
