# Validation

Run the existing Runtime Scheduling, Runtime, Activities Scheduling, Tasks and Runtime EF projects as whole suites; compare discovery/execution counts. Run provider smoke coverage with existing required-provider configuration. Cover future/earlier deadlines, missed hints, visibility, failure backoff, partitions and disposal. Mutate deadline scheduling off, observe failure, restore and pass.

Build `Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj`, start compiled output on a private port with fresh SQLite and existing scheduling composition. Run `e2e-tests/scheduling/Test-Delay.ps1` with five seconds and Timer/Cron regressions. Retain activity timestamps: never early, with no additional recovery-poll wait on an idle host. Do not modify or stop the user's running host.

Run architecture and generated maps gates from `docs/contributing/merging.md`. Record commands, counts, failures, review and exact-head/resulting-main CI on #3478 and the PR.
