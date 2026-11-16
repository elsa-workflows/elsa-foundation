# Reproduce the round-5 mailbox finding

This reproduces the already recorded correctness finding. It neither starts another review round nor changes the product. The final review verdict remains one P2 finding at head `1e707d58fc2148be3715d50616dfdb69dc82ae65` against base `7b49513650665606e5f52fdc08c5831f60802dac`. Original executing products report `f6d3c370e5c3295bb9ea0499b0ea6adbc533b8a1`; `provenance.json` proves the implicated source/test trees are unchanged at the reviewed head.

Keep this file, `generate-mailbox-probe.py` and `mailbox-probe/Program.cs` together. Supply a coherent .NET10 product build plus the repository's existing `Elsa.Activities.Testing.dll` harness and dependencies. The built BPMN test output directory contains both. No NuGet package download or product rebuild is required by the probe project. The local .NET10 SDK and ASP.NET Core framework must already be installed.

Generate into a NEW/empty temporary directory:

```sh
python3 generate-mailbox-probe.py --products-directory /path/to/coherent/net10.0/products --output-directory /fresh/temp/mailbox-probe
```

If the test harness is in a separate directory, also pass `--harness-directory /path/to/coherent/net10.0/harness`. Conflicting copies of the same DLL are refused. The supplied directories must retain the matching `runtimes/<local-rid>/native` SQLite library (or its top-level copy). The generator supports local macOS/Linux/Windows on x64/arm64, finds the appropriate SQLite library and includes it with output-copy metadata. It generates the references from the supplied directories and records their hashes; it does not rely on historical machine paths.

Run the two commands the generator prints, using the normal machine `dotnet` wrapper for the build. The run argument must be a fresh result directory so prior state cannot turn the start commands into replays. Do not use a production database. The script writes only its temporary project/native copy; the executable writes only its isolated SQLite store/result.

The original counterexample has default `PassivateOnTerminal=true`, `before=0`, `beforeModelRedrive=8`, `afterModelRedrive=8`, and `afterExplicitPassivation=0`. All eight starts are consumed and Completed/TerminalSettled with empty queues. The ordinary redrives do not emit terminal metadata. Explicit passivation releases all eight, and all eight original starts then replay without activation. The process has completed when its JSON is emitted; retain only the result and a coherent closed backup if desired, not live database sidecars.

The sealed generator verification uses the original product set, passes all eleven recorded checks and yields a result byte-identical to `mailbox-probe/completed-models/result.json`. Supplied future products may have different behavior; `generation.json` and the result's assembly informational versions identify what actually ran. The seal does not relabel a future product as the reviewed source.
