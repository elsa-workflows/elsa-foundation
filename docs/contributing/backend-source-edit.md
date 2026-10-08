# Try a backend source edit

This short exercise changes a disposable checkout, proves the change with a focused test, then observes it in a running workflow. It is a learning exercise, not a product feature or a real contribution PR. Start with the [backend source quickstart](backend-source-quickstart.md) and use a fresh disposable checkout to contain its SQLite data and temporary edits.

From the repository root, open `tests/essentials/Activities/Runtime/Tests/WriteLineBoundInputExecutionTests.cs`. In `WriteLine_hydrates_plain_text_without_an_argument_or_memory_wrapper`, change only the captured-output assertion to:

```csharp
Assert.Equal("Onboarding: Hello World!", output.Trim());
```

Keep the following `writeLine.Text` assertion at `Hello World!`; this exercise changes output formatting, not the activity input. Run the focused test in locked mode:

```bash
dotnet test tests/essentials/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj --filter FullyQualifiedName~WriteLineBoundInputExecutionTests -p:RestoreLockedMode=true
```

It should fail because the activity still writes `Hello World!`. Now, in `src/essentials/Activities/Primitives/Activities/WriteLine.cs`, change:

```csharp
Console.WriteLine(Text);
```

to:

```csharp
Console.WriteLine($"Onboarding: {Text}");
```

Run the same locked-mode focused test again. It should pass. Build the default Workbench:

```bash
dotnet build src/apps/Elsa.Workbench/Elsa.Workbench.csproj -p:RestoreLockedMode=true
```

Start the HTTP profile in one terminal and wait for readiness as shown in the quickstart:

```bash
dotnet run --no-build --project src/apps/Elsa.Workbench/Elsa.Workbench.csproj --launch-profile http
```

In a second terminal, check readiness and submit a one-child Sequence workflow. The script verifies the actual run result: one completed Sequence root, one completed WriteLine child, matching activity counts, and zero reported or returned incidents. The server terminal must separately show the exact line `Onboarding: Contributor exercise`.

Linux or macOS, using PowerShell 7:

```bash
curl -fsS http://localhost:5095/health/ready
pwsh -NoProfile -File ./e2e-tests/Test-SequenceWorkflow.ps1 -BaseUrl http://localhost:5095 -Lines 'Contributor exercise'
```

Windows, using Windows PowerShell 5.1:

```powershell
curl.exe -fsS http://localhost:5095/health/ready
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\e2e-tests\Test-SequenceWorkflow.ps1 -BaseUrl http://localhost:5095 -Lines 'Contributor exercise'
```

The script's echoed input is not proof of activity output. Stop only the Workbench process you started with Ctrl+C.

Finally, undo just the two exercise edits. Inspect `git diff --` for those paths and confirm both are back to their original contents; do not discard unrelated work. Rerun the focused test without the temporary expectation and confirm it passes. The Workbench build output still contains the exercise edit until rebuilt, so restore the original host output before reusing this checkout:

```bash
dotnet build src/apps/Elsa.Workbench/Elsa.Workbench.csproj -p:RestoreLockedMode=true
```

The hosted [backend source platform workflow](../../.github/workflows/backend-source-platform.yml) replays the same exercise on Ubuntu 24.04 with PowerShell 7 and Windows Server 2025 with Windows PowerShell 5.1. It classifies the expected-red test from its TRX result and assertion text, restores both files byte-for-byte even on failure, reruns the original focused test, and rebuilds the original Workbench output. This is automated command replay; it does not claim an interactive human trial.

## Verified baseline

The exercise and cleanup build were verified on 6 October 2026 on macOS 26.6 arm64 with .NET SDK 10.0.300 at Foundation revision `70f8db49f4f33a5a2a64eda23d5b0437548e2227` ([evidence #2437](https://github.com/elsa-workflows/elsa-foundation/issues/2437#issuecomment-6008113532)). That historical run used plain `dotnet test` and `dotnet build`; it does not verify the locked-mode command forms documented above or establish a fresh-install, cross-platform, or human-newcomer result.
