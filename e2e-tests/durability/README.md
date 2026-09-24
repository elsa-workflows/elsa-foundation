# durability - checkpoint survival across suspend, resume, and restart

End-to-end durability tests: a suspended workflow's persisted state survives the checkpoint round-trip and a real
**server-process restart**, then resumes correctly. These exercise the on-disk SQLite substrate + the
`WorkflowsRuntimeResumption` / `WorkflowsRuntimeCheckpointPersistence` (Coalesced/50) pipeline the reference server
composes — the parts the in-process C# crash tests stub out (in-memory store + hand-driven sweep).

| Script | What it exercises |
|--------|-------------------|
| `Test-VariableSurvivesSuspend.ps1` | Set a workflow variable, suspend at an `Event`, resume — the post-wait node reads the SAME variable back. Proves the root variable frame (#972) is materialized from the persisted checkpoint across a suspension. No restart. |
| `Test-RestartRecovery.ps1` | Set a variable, suspend, then stop and relaunch the **Workbench process owned by this test** against the same isolated SQLite DB. Assert the instance is still suspended with identical state and the pre-suspend node ran exactly once, then attempt to resume it to completion. A precise `KNOWN ISSUE #1761` branch records the current post-restart stimulus miss and runs the full completion assertion automatically once matching works. |
| `_DurabilityCommon.ps1` | Shared mid-flow `Event` wait and ResumeOnly stimulus helpers. Its older server-lifecycle functions remain for other callers; `Test-RestartRecovery.ps1` does not use them. |

## Server restart mechanics (important)

`Test-RestartRecovery.ps1` starts the already-built Debug Workbench DLL directly. It copies the committed
appsettings and `shells.json` into a fresh temporary content root, checks that the legacy fixture has no
persistence-resource selection, and uses a free loopback port. The same temporary SQLite database is used
before and after restart. It stops only the process it launched, even if another Workbench is listening elsewhere.
On success the temporary root is removed; on failure it is retained with server logs for diagnosis.

Requirements / caveats:
- **Build first:** `dotnet build src/apps/Elsa.Workbench/Elsa.Workbench.csproj`.
- Run `pwsh ./e2e-tests/durability/Test-RestartRecovery.ps1` from the repository root. `-RestartServer:$false`
  exercises the same isolated fixture without restarting it. To target a separately started server, pass
  `-UseExternalServer -RestartServer:$false`; this mode never stops that server.
- The Event wait is resumed by a stimulus, so a restart leaves the instance parked (unlike a `Delay`, which the
  durable-timer pump auto-resumes); the test drives the resume explicitly after the server is back.
- The full restart test currently reports `KNOWN ISSUE #1761` at `resumedCount=0` after checking the persisted
  instance is unchanged, then exits green as a tracker. A no-restart control run resumes and completes. If the
  post-restart stimulus starts matching, the script automatically runs the strict completion and variable assertions.
  Do not report the full durability journey as passing while the known-issue branch is taken.
