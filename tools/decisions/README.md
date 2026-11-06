# Decisions qualification

`generate-decisions-migrations.sh <MigrationName> [Sqlite|PostgreSql|SqlServer|MySql]` scaffolds one provider migration through the normal shared tooling. It refuses replacing a retained migration name or redirecting the output.

With Studio dependencies installed, run the committed native/BPMN/restart witness from a clean checkout:

```sh
python3 tools/decisions/qualify-execution.py --output /tmp/decisions-execution-evidence
```

The runner builds the normal Workbench with locked restore, copies it to a fresh private content root and owns every host process/store. It uses actual login, authoring, publication, start, inspection and stimulus APIs. It compares direct, native Sequence and ordinary BPMN task results. A further workflow suspends before the decision; a new decision version is published, the host process stops, and recovery disables Decisions authoring/API/EF services. The waiting workflow must still complete with its original frozen result and content hash. Runtime/identity/inspection remain the ordinary host services. Missing Design is observed as HTTP404, not simulated by a fake reader.

The report records exact source/binary identities, process IDs, disabled recovery features and API receipts. Dirty source, changed source, missing captures, timeouts or incomplete phases fail. Outputs and browser traces from the composed Studio journey are retained by the CI UX artifact. The source gate and shared host helpers live in [UX acceptance tooling](../ux-acceptance/run.py).

For the four-project Decisions editor journey:

```sh
python3 tools/ux-acceptance/run.py --journey 'business decisions authoring and explanations' --output /tmp/decisions-studio-evidence
```

Omitting `--journey` still requires every registered product journey. Scoped qualification never substitutes for the full CI acceptance job.

The execution runner also provisions seven restricted users across two tenants through the normal Identity provider ports. It verifies cookie401/403 behavior, separate domain grants, protected reads and current-grant revocation after restart. Private credentials never leave the disposable store root; CI retains only public receipts and safe logs. See [identity qualification](../../specs/1195-business-decisions/evidence/identity-qualification/README.md).
