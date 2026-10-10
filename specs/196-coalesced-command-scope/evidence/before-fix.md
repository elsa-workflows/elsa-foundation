# Before-fix contract proof

Root and independent Sol 5.6 High accepted the test-only handoff at spec-only HEAD `d1d7117946483eb0eaaf876e8c30bb8976a18c5f`, over unchanged runtime source `932562a7d2b96536a0f9b0355a273bc081f2fe3a`.

The queued Release run filtered to `RuntimeCoalescingScopeRegistrationTests` compiled successfully with existing warnings, returned wrapper exit 1, and recorded exactly two failed tests (zero passed/skipped):

- `ValidateScopes=true`: DI rejected scoped `RuntimeCheckpointCommitter` consumed from singleton `IRuntimeCoalescingDrainScopeFactory`.
- `ValidateScopes=false`: the cross-scope factory `Assert.NotSame` failed. The singleton was reused across independent scopes.

These are the intended lifetime failure oracles, not setup/dependency failures. No database query, migration, container or application host was invoked. SQLite composition and provider metadata are resolved only. The runtime source was unchanged for this run.

- `src/essentials/Workflows/Runtime/Api/Coalescing/CoalescingRuntimeCheckpointPersistenceExtensions.cs` SHA-256 `9acbc901ccb472a890b48acb39d09745925c715a5a2bd67e312ddcaa4c273d99`.
- `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/ProviderTests/RuntimeCoalescingScopeRegistrationTests.cs` SHA-256 `04cb1cc43a62e43e94274f3d9d860dda805b08d15ff5d994e01ea0f608e6ea75`.
- `tests/essentials/Workflows/Runtime/Tests/RuntimeCheckpointCoalescingTests.cs` SHA-256 `25186f820c5aff66cfee97979fee39ec30e0d1f9df764202323436cfcc0a8a67`.

Retained private TRX SHA-256 `1708e4928f3798651014591e780ca7abadb7d3a495d919dd04ea98ede8fbcf8e`, build/test log SHA-256 `e7bb419392246d140ab7bea8df1470d7af4bae5a877aa635c6a42522d17210a2`. Full raw evidence is retained in the control-room journal; public repository evidence excludes local paths and secrets.

Command (queued wrapper on PATH):

```bash
dotnet test tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests.csproj --configuration Release --filter FullyQualifiedName~RuntimeCoalescingScopeRegistrationTests --logger 'trx;LogFileName=before-fix.trx' --results-directory <owned-evidence-directory>
```

Separate pre-fix custom Singleton/Scoped registration controls passed 2/2 (wrapper exit 0). This proves existing custom registration compatibility, not the default lifetime repair. No attribution of the individual historical C4 responses is made.
