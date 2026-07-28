# durability - checkpoint survival across suspend, resume, and restart

End-to-end durability tests: a suspended workflow's persisted state survives the checkpoint round-trip and a real
**server-process restart**, then resumes correctly. These exercise the on-disk SQLite substrate + the
`WorkflowsRuntimeResumption` / `WorkflowsRuntimeCheckpointPersistence` (Coalesced/50) pipeline the reference server
composes — the parts the in-process C# crash tests stub out (in-memory store + hand-driven sweep).

| Script | What it exercises |
|--------|-------------------|
| `Test-VariableSurvivesSuspend.ps1` | Set a workflow variable, suspend at an `Event`, resume — the post-wait node reads the SAME variable back. Proves the root variable frame (#972) is materialized from the persisted checkpoint across a suspension. No restart. |
| `Test-RestartRecovery.ps1` | Set a variable, suspend, then **kill the `Elsa.Server` process and relaunch it** against the same SQLite DB. Asserts the instance is still `Suspended` with identical state (the pre-suspend node ran **exactly once** — no duplicate replay), then resumes it to completion with the variable intact. The full black-box durability proof. |
| `_DurabilityCommon.ps1` | shared helpers: mid-flow `Event` wait node, ResumeOnly stimulus (input required, #1014), and **server lifecycle control** (`Stop-ElsaServer` / `Start-ElsaServer` / `Restart-ElsaServer`). |

## Server restart mechanics (important)

`Restart-ElsaServer` kills the process on port 5095 and relaunches the **already-built server DLL directly**
(`dotnet <…>/bin/Debug/net10.0/Elsa.Server.dll`) with `ASPNETCORE_URLS=http://localhost:5095` +
`ASPNETCORE_ENVIRONMENT=Development`, working-dir = the project directory so the on-disk SQLite files under
`src/Apps/Elsa.Server/` are reused. It launches the DLL directly (not `dotnet run`) because `dotnet run`
re-evaluates this large solution's MSBuild graph on every relaunch (minutes); the DLL comes up in ~5-8s.

Requirements / caveats:
- **The server must already be built** (`dotnet build src/Apps/Elsa.Server`), since restart uses the compiled DLL.
- Restart control is **Windows/PowerShell-specific** (`Get-NetTCPConnection` + `Stop-Process`). `Test-RestartRecovery`
  accepts `-RestartServer:$false` to run the suspend→resume assertions **without** a restart (weaker, but portable to
  environments where the runner can't own the server process).
- The Event wait is resumed by a stimulus, so a restart leaves the instance parked (unlike a `Delay`, which the
  durable-timer pump auto-resumes); the test drives the resume explicitly after the server is back.
- A full restart run takes ~40-90s (process kill + relaunch + resume); budget accordingly. Do not run overlapping
  copies — competing servers contend for port 5095 and the SQLite lock.
