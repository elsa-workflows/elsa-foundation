# Exact successful review commands

Working directory: `/Users/sipke/.codex/worktrees/f236/elsa-foundation`.

The fixture publication, actor commands, exact selectors and every state read are fully contained in `probe/Program.cs`. Its input copy/provenance is `original-input-manifest.json`; source files were decompressed unchanged into the private `prior-cancellation/` directory before each independent trial. The final trial takes the eight SQLite backup snapshots within `View`.

```sh
dotnet build /tmp/elsa-2898-review9-correctness/probe/probe.csproj --nologo
cp -R tools/bpmn/NativeWorkDispatch/bin/Release/net10.0/runtimes /tmp/elsa-2898-review9-correctness/probe/bin/Debug/net10.0/
cp tools/bpmn/NativeWorkDispatch/bin/Release/net10.0/runtimes/osx-arm64/native/libe_sqlite3.dylib /tmp/elsa-2898-review9-correctness/probe/bin/Debug/net10.0/
dotnet /tmp/elsa-2898-review9-correctness/probe/bin/Debug/net10.0/probe.dll

dotnet test Essentials/tests/Activities/Bpmn/Tests/Elsa.Activities.Bpmn.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~BpmnDirectExecutionTests|FullyQualifiedName~BpmnDurationBindingTests|FullyQualifiedName~BpmnCommandAuthorizationOrderTests|FullyQualifiedName~BpmnNativeEventBindingTests|FullyQualifiedName~BpmnServiceFaultPolicyTests' --results-directory /tmp/elsa-2898-review9-correctness/results --logger 'trx;LogFileName=focused-bpmn.trx'

dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~EfRuntimeModelCheckpointTests|FullyQualifiedName~EfSchedulerWorkQueueModelFormatTests|FullyQualifiedName~EfModelAdmissionLeaseTests|FullyQualifiedName~RuntimeModelMigrationTests' --results-directory /tmp/elsa-2898-review9-correctness/results --logger 'trx;LogFileName=focused-ef.trx'

git diff --exit-code 20bca048737cdbcceb95cc4541643077d3a307a5 ca1b3ea8b28a10409c58da9ff49884f6370864fd -- Essentials/src Essentials/tests/Activities/Bpmn tools/bpmn Studio/src
git diff --check d63e18a47 ca1b3ea8b -- Essentials/src Studio/src
/Users/sipke/.local/bin/gh-codex api repos/elsa-workflows/elsa-foundation/pulls/3410 --jq '{head: .head.sha, base: .base.sha, draft: .draft, state: .state}'
```

The native-library copy corrects only private launcher discovery. For portable reproduction substitute a fresh private folder and explicit current product paths, then restore the declared original stores before running. No historical build-output directory is asserted to be a portable prerequisite.
