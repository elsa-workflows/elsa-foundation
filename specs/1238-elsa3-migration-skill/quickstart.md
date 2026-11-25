# Quickstart: verify spec 1238

## Catalog and sample

```powershell
python tools/skills/check_migration_skill.py --self-test
python tools/skills/check_migration_skill.py
dotnet test extensions/Elsa3/tests/Migration/Sample.Tests/Elsa3.Migration.Sample.Tests.csproj
```

All three pass. Rename a sample method used in a region without updating the catalog: the check fails.

## Adapters

1. In Claude Code: add this repository as a plugin marketplace, install `elsa3-migration`, and confirm the skill
   `elsa3-workflows-migration` is listed.
2. Copy `plugins/elsa3-migration/skills/elsa3-workflows-migration/` into another workspace's
   `.agents/skills/` and confirm an agent that reads that folder lists the skill.

## Walkthrough (needs spec 1237's tool and spec 1236's migration host, in a clone with the .NET SDK)

1. Prepare a synthetic Elsa 3 export using one custom activity, one JavaScript function, one custom variable type and
   one custom storage driver, with matching Elsa 3 source. Keep it outside the repository; it is test input.
2. Without an AI, follow the README section to `SKILL.md`; run the inventory and port the activity (SC-001).
3. With two different AI assistants, start the skill; compare the plans (SC-002).
4. Stop one session during `prepare`, start a new one, and confirm it resumes (SC-005).
