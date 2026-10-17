# Microsoft Agent Framework execution semantics probe

This isolated executable probes the released `Microsoft.Agents.AI.Workflows` 1.25.0 NuGet package with deterministic executor doubles. It performs no LLM calls and does not contact external systems.

From a clean checkout, prepare the ignored probe-local package feed and global cache from the committed manifest, then build and run:

```bash
cd tools/experiments/UnifiedExecution.AgentFrameworkProbe
python3 fetch_feed.py
python3 prepare_package_cache.py
dotnet run --configuration Release
```

`fetch_feed.py` reads the fixed `feed-manifest.json`; it does not resolve dependencies or alter the manifest. Each manifest SHA512 was checked against NuGet's registration catalog `packageHash`. The script verifies each downloaded package's length and SHA512 before writing it to the ignored local feed. `prepare_package_cache.py` repeats that verification before expanding packages into the ignored probe-local cache. `packages.lock.json` records the restored graph and NuGet content hashes. The executable writes the checked-in `evidence.json` beside this README.

The three assertions cover receiver-level concurrency versus per-receiver serialization, a round barrier that holds a fast branch's next executor until a slow receiver completes, and fan-in duplicate buffering retained across an in-memory partial-fan-in checkpoint/resume.

This is package behavior evidence only. It does not establish distributed execution, durable/cross-process checkpoint restore, connector behavior, Elsa runtime semantics, retries, exactly-once effects, or a production design choice. The package nuspec identifies the repository URL but does not declare a source commit. The separately cited `44fe223da7ea6853eefb7053e25c2c213a2e5c15` is a source observation, not a proven source revision for this NuGet artifact. The receiver probes use the package's concurrent off-thread in-process environment and factory-created executors; they test one workflow run, not concurrent runs sharing executor instances.
