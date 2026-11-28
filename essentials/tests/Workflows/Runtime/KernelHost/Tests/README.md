# Workflow kernel host tests

The tests compose the retained publication, runtime artifact, test-scope and
standalone delivery owners. Provider cases use real Actor Foundation reads and
writes. The prepared delivery tests seed the historical execution, deletion
and bookmark shapes in the shared Runtime database to verify its admission
checks against existing data.

The legacy publisher comparison requires the previous source producer and
runner registrations. That checkout also retains the former broad API
co-selection witness and the classic collector sweeps for unpublished child
and held HTTP executables. The current tests inspect the physical material
retention roots before and after retiring the Actor run.

Run the classic comparisons in a disposable checkout:

```sh
archive="$(mktemp -d)"
git -c core.fsmonitor=false worktree add --detach "$archive/source" 415905150f6060a2b257649fe48c645873a428cc
(
  cd "$archive/source"
  dotnet test essentials/tests/Workflows/Runtime/KernelHost/Tests/Elsa.Workflows.Runtime.KernelHost.Tests.csproj \
    -c Release -p:RestoreLockedMode=true \
    --filter 'FullyQualifiedName~neutral_runtime_admits_and_drains_only_prepared_standalone_sources|FullyQualifiedName~Retained_api_refuses_co_selection_with_the_broad_runtime_routes|FullyQualifiedName~Sqlite_shell_pins_the_canonical_material_of_a_child|FullyQualifiedName~Sqlite_held_HTTP_pins_its_executable_through_cold_resume_and_actor_retirement'
)
```

Set `KERNEL_STANDALONE_FOUNDATION_POSTGRES` and
`KERNEL_STANDALONE_RUNTIME_POSTGRES` to separate disposable databases to
include the PostgreSQL publisher comparison.
