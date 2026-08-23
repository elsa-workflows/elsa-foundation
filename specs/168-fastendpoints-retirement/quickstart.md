# Quickstart: Final FastEndpoints Retirement

How to execute and verify this unit. Commands are macOS/Linux; the repository also ships PowerShell
equivalents.

## Governing constraint

Read framework constitution §2.25.3 before deleting anything:

> A static census, a text search, or a similarity judgement is not sufficient evidence to remove
> anything.

The scan below finds candidates. It authorizes nothing. Evidence is the build and the suite.

## 1. Establish the candidate set

```bash
grep -rn "FastEndpoints" --include="*.cs" --include="*.csproj" --include="*.json" --include="*.md" src/ tests/ tools/ docker/ specs/ docs/ | grep -v "/obj/\|/bin/"
```

Record every hit as a classification entry. This produces `Unresolved` rows, not deletions.

## 2. Capture the before-state of the guards

This is the measurement SC-003 depends on. A green summary line cannot distinguish a passing guard
from a deleted one, so capture test *names*:

```bash
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --list-tests > /tmp/fe-tests-before.txt
```

Repeat for each suite touched by the classification. Keep these files; step 6 diffs against them.

## 3. Classify before removing

Fill in the classification artifact until zero entries are `Unresolved`. Every `Preserve` reason must
name the guarantee it protects. This artifact is the reviewable checkpoint; it is worth review before
any deletion lands, because a wrongly-removed guard cannot fail afterwards.

## 4. Remove in batches, gating each batch

One batch per category, so a red gate attaches to a specific removal:

```bash
dotnet build Elsa.Server.slnx --nologo
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --nologo
```

The retirement guard must pass in every batch:

```bash
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --filter "FullyQualifiedName~FastEndpointsTransitionTests" --nologo
```

## 5. Verify configuration by activation, not by reading

String-keyed feature names are invisible to the compiler:

```bash
docker compose -f docker/compose/<compose-file> config
```

Then start the Workbench composition and confirm the shell activates with no unresolved feature.

## 6. Prove no preserved guard vanished

```bash
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --list-tests > /tmp/fe-tests-after.txt
diff /tmp/fe-tests-before.txt /tmp/fe-tests-after.txt
```

Every disappearance must correspond to a `Remove` or `Archive` entry. Any other disappearance is a
defect, not a cleanup. Report executed counts with `Skipped`, never a bare "green".

## 7. Sweep stale prose

After removal, search for surviving mentions of deleted types and read the hits:

```bash
grep -rn "FastEndpointsFeatureBase\|ElsaEndpoint\|PermissionNames\|ApiSecurityFeature" --include="*.cs" --include="*.csproj" --include="*.md" src/ docs/ | grep -v "/obj/\|/bin/"
```

A hit is not automatically wrong; a hit that *describes* a removed type is.

## 8. Refresh generated maps

Removing projects and specs moves the maps. The check is the authoritative answer:

```bash
dotnet run --project tools/maps/Elsa.Maps.Generator -- all
dotnet run --project tools/maps/Elsa.Maps.Generator -- check
```

Stage every changed map by explicit path, including `docs/maps/manifest.json` when it changed. It is
byte-identical when nothing moved, and then there is nothing to stage.

## 9. Report both lists

§2.25.4 requires what was retired **and** what was examined and deliberately kept. The second list is
what stops the next review re-deriving these conclusions.

## Rollback

This is a subtractive unit, so rollback is reverting the merge commit. There is no data migration and
no persisted state to unwind. A partial rollback that restores the abstractions without restoring
their registrations would recreate the zero-assembly activation failure described in the Wave 8
report, so the revert must be whole.
