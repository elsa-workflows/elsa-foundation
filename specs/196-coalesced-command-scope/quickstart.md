# Validation Guide

Use the isolated correction worktree and queued dotnet wrapper; run builds and normal hosts serially. Preserve primary checkout edits and existing databases.

1. Pin failed-base source 932 plus the new test-only diff. Run the focused Coalesced scope-registration controls in the existing EF provider project before the runtime correction; retain the expected DI lifetime failures and TRX.
2. Apply the reviewed default scoped registration, rerun focused controls, and run the registration revert mutation to prove they detect the original defect. Restore the correct source before broader checks.
3. Run the existing runtime suite, relevant provider correctness suites, architecture and Maps check. Local filtered DI proof is not a substitute for required provider/CI gates.
4. Rebuild Workbench, own fresh PostgreSQL runtime/SQLite diagnostic resources, and run the existing HttpEndpoint primary and valid REST companion separately. Record source/DLL/schema/fixture/settings, response/output/status/incidents/cadence and cleanup. The final planned four-client case belongs to downstream T17/T18 acceptance, must retain every attempt, and is not a T19 completion criterion; timing and correctness are separate.
5. Root and independent QA review exact-head evidence. Keep PR draft until review/gates are satisfied, and keep merges held for the existing publication-boundary decision. Resulting-main CI/Maps must pass after any eventual normal merge; a passing diagnostic on changed source does not repair the original main failure.

Expected result: scope separation is deterministically established, all touched behavior remains correct, and evidence states remaining attribution limits. No new elapsed-time budget or automatic package publication is part of this guide.
