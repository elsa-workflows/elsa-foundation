The independently restored old/current operator consoles are package consumers.
Preparing or publishing them does not start them, open a database, place/release a
hold or prove an upgrade outcome. Their existing tooling dependency owns all
finalization semantics. No operator action forces finalization.

`prepare_upgrade_distributions(run)` returns each console's retained metadata at
`prepared[generation]["operator"]`: source/product identity, package-only restore
closure and lock, archive/cache/published managed/native byte joins, the complete
published-file map and digest, command prefix, and filtered runtime environment.

After the shared content root exists and the real backend has created/migrated
the database, `upgrade_operator_command(run, prepared, generation, database)`
checks the retained console manifest and returns:

```
dotnet /owned/publish/Acme.Weaver.UpgradeOperator.dll --owned-root /owned/runtime --database /owned/runtime/state/weaver-records.db
```

The root driver supplies a single bounded JSON document on stdin. For example:

```json
{"version":1,"command":"hold","selection":{"kind":"modules","modules":["Weaver"]},"provider":"Sqlite","connection":"Data Source=/owned/runtime/state/weaver-records.db","finalization":{"family":"Weaver","version":null,"reason":"T038 controlled upgrade","operator":"T038 fixture operator"}}
```

For `release`, omit `reason`; retain the operator identity and null version. For
`status`, omit both reason and operator; retain family Weaver. The console
rejects another command, provider, connection, module selection, family or
version-specific hold before invoking `EfToolingHost.RunAsync`. The envelope
version must be an Int32 JSON number matching the existing tooling version.
Duplicate JSON members, noncanonical/symlink paths, trailing database-path
whitespace, nonprivate runtime directories and overlap
with its immutable publish also refuse. The SQLite database must already exist
under the private runtime root. Requests are limited to 64 KiB, depth eight and a
sixty-second cancellation deadline. Tooling handles all remaining closed-contract
validation and emits its normal JSON response on stdout. A console guard refusal
has `operatorRefused:true`, its own fixed code and a nonzero exit code.

The root driver owns actual invocation, stdout/stderr/exit receipts, process
closeout, hold/status verification and the native wait loop. All new source and
controls remain UNRUN at this source checkpoint. Operator preparation does not
promote any case or witness.

`guard-controls.json` supplies six UNRUN malformed-version inputs for a future
root-owned operator run. Its retained reference is exposed as
`prepared[generation]["operator"]["guardControls"]`. These inputs require the
fixed guard-refusal response and exit code 2, including for fractional and
out-of-range numeric versions; they are not native proof or executed results.
