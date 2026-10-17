# Orleans provider preflight

This is the real framework bootstrap precursor to #2900, not its provider conformance proof.
Pinned Orleans 10.4.0 and Hosting 10.0.5; exact transitive packages are locked. The isolated project disables central version management so it cannot change production dependencies.

Initial execution from the same source in `/tmp/elsa-orleans-preflight`:

- Focused build succeeded, zero warnings/errors, SDK 10.0.300.
- Two separate silo processes announced ready on silo ports19111/19112, gateway ports19211/19212.
- A separate client resolved grain key `same-execution` on PID88088 (second silo).
- SIGKILL terminated that confirmed experiment process. The immediate client failed with OrleansMessageRejectionException; routing does not hide all failure windows.
- A later new client resolved the same grain key on PID88031 (surviving first silo).

The result proves basic multiprocess addressing/reactivation, not durable state, workflow semantics, fencing, capability placement, or exactly-once effects. No network partition was simulated. The in-memory membership seed is a local-development topology and its loss remains untested. Do not use this preflight as #2900 completion evidence.

Run `dotnet build tools/experiments/UnifiedExecution.ProviderProbe`. Then run its generated DLL in separate terminals with `silo 19111`, `silo 19112` and `client`. Ports must be free. Stop only these owned processes afterward. The next step replaces Ping with the same shared-work coordinator used by the built-in host and adds durable failure/conformance assertions.

## Automated reproduction and retained evidence

After building, run `python3 tools/experiments/UnifiedExecution.ProviderProbe/preflight.py`. It checks availability of the expected local ports, starts two owned silo processes, directs initial placement through the second gateway with PreferLocalPlacement, kills that activation, retries through the surviving gateway, and terminates all remaining owned silos in a finally block. Port availability checking is not a race-free reservation; a conflicting process makes the probe fail, not pass.

The automated run passed: same key moved from PID89281 to PID89280 after seven failed recovery attempts. [Evidence](evidence/preflight.json), [raw logs](evidence/preflight-logs.zip) and [digests](evidence/sha256.json) are retained. Durations are diagnostic observations, not a performance claim. `preflight.json` references log names inside the ZIP. The supplied preflight source is reproducible; source inspection and package lock pin the framework. Full workflow-provider acceptance remains pending.
