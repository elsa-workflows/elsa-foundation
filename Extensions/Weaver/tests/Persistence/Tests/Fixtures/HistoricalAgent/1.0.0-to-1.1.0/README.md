# Agent 1.0 → 1.1 captured fixtures

These five whole-row pairs were captured from the actual pre-M4 1.0 writer and target 1.1 writer. Every content-column string is byte-identical, including all three session content columns. [Capture evidence](https://github.com/elsa-workflows/elsa-foundation/pull/3845#issuecomment-6670446473) and [provenance.json](provenance.json) record exact source/tree, input, executed assembly, row-file and unchanged SQLite DDL hashes. An independent coordinator verified the retained capture artifacts and recorded resource closeout.

The `test-only-agent-fixture:` transport is synthetic plaintext through the production codec/store; it is not encryption or authentication evidence. The actual migrated SQLite databases were disposable; these original JSON rows are retained, not database files. Each fresh database admitted its writer’s format; capture does not prove fleet finalization or a mixed-version running fleet.

Keep captured files unchanged and never regenerate them during tests or CI. `AgentEffectsV1CanonicalTests` forwards the three unchanged shared [upcaster proof algorithms](../../../../../../../../../Essentials/tests/Persistence/EntityFramework/Tests/EfSchemaUpcasterFixtureSupport.cs) through the internal Agent upcaster. Capture alone does not establish those proof results, native providers, binary consumers or application acceptance.
