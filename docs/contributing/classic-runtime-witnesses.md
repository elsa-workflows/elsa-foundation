# Classic Runtime test archive

Run the classic Runtime resumption pump tests and EF command-cost measurements
from the exact source revision that owns their engine, fixtures and dependency locks.
The measurements describe the classic checkpoint and coalescing path.

```sh
archive="$(mktemp -d)"
git -c core.fsmonitor=false worktree add --detach "$archive/source" af155fa58651f147160c840924685ed9610a299c
cd "$archive/source"
dotnet test essentials/tests/Workflows/Runtime/Resumption/Tests/Elsa.Workflows.Runtime.Resumption.Tests.csproj \
  -c Release -p:RestoreLockedMode=true
ELSA_COMMAND_COST_OUT="$(mktemp -d)" \
  dotnet test extensions/Http/tests/Activities/CommandCost/Tests/Elsa.Activities.Http.CommandCost.Tests.csproj \
  -c Release -p:RestoreLockedMode=true
```

The command-cost PostgreSQL run needs Docker. See its
[archived measurement instructions](https://github.com/elsa-workflows/elsa-foundation/blob/af155fa58651f147160c840924685ed9610a299c/extensions/Http/tests/Activities/CommandCost/Tests/README.md)
for report fields and the dedicated test database setup.

## Classic Publishing and Runtime integration

Use `45a594857c700de2fe76de9cbcb62d06bbf6876b` for the classic
Publishing/Runtime API join, activity input hydration, checkpoint execution,
and optional executable collection witnesses. The two projects at that source
build their original runtime fixtures and use their matching package locks.

```sh
dotnet test essentials/tests/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj \
  -c Release -p:RestoreLockedMode=true
dotnet test essentials/tests/Workflows/Publishing/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests.csproj \
  -c Release -p:RestoreLockedMode=true
```

Current Publishing tests retain protected source references, executable hash
compatibility, publication and journal behavior, and real migration and test-run
transport checks through their current storage and execution providers.
