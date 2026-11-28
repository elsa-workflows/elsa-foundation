# Decisions qualification

`generate-decisions-migrations.sh <MigrationName> [Sqlite|PostgreSql|SqlServer|MySql]` scaffolds one provider migration through the normal shared tooling. It refuses replacing a retained migration name or redirecting the output.

Run the committed Native/BPMN/restart witness from the [pinned classic Workbench and Studio checkout](../../docs/contributing/workbench-compatibility-tests.md#classic-composed-studio-and-decision-runtime) with its Studio dependencies installed:

```sh
python3 tools/decisions/qualify-execution.py --output /tmp/decisions-execution-evidence
```

The runner builds the normal Workbench with locked restore, copies it to a fresh private content root and owns every host process/store. It uses actual login, authoring, publication, start, inspection and stimulus APIs. It compares direct, native Sequence and ordinary BPMN task results. A further workflow suspends before the decision; a new decision version is published, the host process stops, and recovery disables Decisions authoring/API/EF services. The waiting workflow must still complete with its original frozen result and content hash. Runtime/identity/inspection remain the ordinary host services. Missing Design is observed as HTTP404, not simulated by a fake reader.

The report records exact source/binary identities, process IDs, disabled recovery features and API receipts. Dirty source, changed source, missing captures, timeouts or incomplete phases fail. The corresponding classic composed Studio journey retains its outputs and browser traces in that run’s evidence directory. The source gate and shared host helpers live in [UX acceptance tooling](../ux-acceptance/run.py).

For the four-project Decisions editor journey:

```sh
python3 tools/ux-acceptance/run.py --journey 'business decisions authoring and explanations' --output /tmp/decisions-studio-evidence
```

The classic checkout requires every registered journey when run without `--journey`.

The execution runner also provisions seven restricted users across two tenants through the normal Identity provider ports. It verifies cookie401/403 behavior, separate domain grants, protected reads and current-grant revocation after restart. Private credentials never leave the disposable store root; The evidence directory retains only public receipts and safe logs. See [identity qualification](../../specs/1195-business-decisions/evidence/identity-qualification/README.md).
