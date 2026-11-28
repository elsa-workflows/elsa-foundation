# Classic Compensation execution tests

The earlier Runtime's Compensation journey, checkpoint, retry and provider
witnesses use the immutable source and package locks below. Run the archived
test projects in an owned checkout with disposable databases.

```sh
archive="$(mktemp -d)"
git -c core.fsmonitor=false worktree add --detach "$archive/source" 0866f655606838d26fe8b628f1e5709b9fee3109
(
  cd "$archive/source"
  dotnet test essentials/tests/Activities/Compensation/Tests/Elsa.Activities.Compensation.Tests.csproj \
    -c Release -p:RestoreLockedMode=true
  ELSA_COMPENSATION_EF_REQUIRE_NATIVE_PROVIDERS=1 \
    dotnet test essentials/tests/Activities/Compensation/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Activities.Compensation.Persistence.EntityFrameworkCore.ProviderTests.csproj \
    -c Release -p:RestoreLockedMode=true
)
```

The provider replay can start disposable PostgreSQL, SQL Server and MySQL
containers. The first project runs the matching SQLite coverage. The current
test project keeps authored structures and validators, the journal's stored
state projection and its value-disclosure checks.
