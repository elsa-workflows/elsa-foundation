# Independent native Dashboard package witness

This probe builds real product/module archives, restores and publishes an independent package-only consumer in an owned directory, and exercises normal authenticated native record, discovery, query, saved-question and drill operations. It installs `Acme.Processing` through NuGet; neither the consumer nor its generic query engine references product source. It verifies the loaded module/Core bytes against retained nupkg entries and checks actor, tenant, record and category-denial controls. A disabled-contribution restart keeps installed bytes unchanged and makes its dataset unavailable rather than substituting a source.

The consumer uses Foundation's actual normalized-principal and permission services plus native Dashboard API/SQLite resource persistence. A private local authentication fixture supplies predeclared canonical actors and fresh random tokens; no permission evaluator, actor resolver, query implementation or source result is mocked. Its token-refresh authority boundary is explicit. Processing data is created through the module's ordinary API and its ephemeral default store is reseeded after restart. This does not establish durable business-data storage, production authentication, Nuplane dynamic unload, Studio UX or live-provider behavior.

Use Python 3.12 or newer (the package archive inspection uses the standard-library tar extraction filter). Run with a new absolute directory outside the checkout and a unique local-only version:

```sh
python3 tools/studio/Dashboard.Conformance.Probe/qualify.py \
  --output /absolute/new-owned-dashboard-package-proof \
  --version 4.0.0-dashboard3604.local1
```

Final-source qualification requires a clean committed checkout. `--allow-dirty` is explicitly preliminary and retains source-input hashes plus dirty paths; it never claims exact-commit or resulting-main qualification. Every command/status/log, package identity/hash, locked package-only dependency graph, offline restore and published/loaded archive comparison is retained. A failed stage remains failed; the driver does not retry it automatically.

All hosts bind only a random loopback port, use owned private profiles/stores/caches, and are stopped by their owner on completion/failure. The output directory contains private fixture tokens and should not be uploaded wholesale. Public receipts omit tokens and provider/shared credentials. No package is published remotely and no model/provider is configured or invoked. Existing hosts, stores, credentials and frontend builds are untouched.

The separate opt-in [live runner](live_qualify.py) prepares a composed source-host witness for actual Copilot authoring. It does not change the package witness above or the provider-disabled native UX runner. Its default only materializes a host and isolated lock copies; it reads no credential, starts no host and contacts no provider:

```sh
python3 tools/studio/Dashboard.Conformance.Probe/live_qualify.py \
  --output /absolute/new-owned-dashboard-live-preparation \
  --model an-explicit-supported-model
```

`--mode build` additionally restores and builds the dedicated backend and ordinary Studio host, tests the observer offline, and builds the frontend from this checkout's frozen installation. The [host template](live/Host.csproj.in) lists unconditional reviewed dependencies, including Acme. The runner refuses template drift from the current Workbench native composition. It copies the current native entrypoint into the owned host and applies three exact-count instrumentation hooks; it does not change Workbench sources or add conditional project dependencies. Every .NET project uses owned artifacts and a project-specific owned lock through a shared post-common-props hook. Existing lock copies must remain byte-for-byte unchanged, followed by locked restore and no-restore build. Source and generated-input hashes and command/log receipts are retained.

Live execution is currently **not run**. The last account check was unavailable (models access 403, subscription ended/CLI disabled). Do not retry until the owner confirms account access is restored. After that confirmation, a clean committed checkout and a new owned output are required:

```sh
# The approved credential is already in the named environment variable.
# The runner has no credential-file reader or token command-line option.
python3 tools/studio/Dashboard.Conformance.Probe/live_qualify.py \
  --output /absolute/new-owned-dashboard-actual-live-proof \
  --model an-explicit-supported-model \
  --mode live --account-access-restored \
  --credential-environment-variable APPROVED_COPILOT_TOKEN_VARIABLE
```

The runner performs one normal authenticated Agent bootstrap preflight against the actual SDK adapter. An unavailable/403 result is retained as unavailable and exits nonzero, with no automatic retry and no browser turn. A declared supported explicit model is required. The temporary SDK home/state/workspace, normal IAM and source stores are owned and removed when the host exits. Acme's reference source is ephemeral; the witness establishes page reload and saved-definition persistence, not Acme business-record durability across a host restart.

The two [browser journeys](../../../Studio/tests/acceptance/liveDashboardsJourney.ts) drive actual Workflow and Acme questions through Ask, a consequential module clarification, answer selection, refinement, native preview, Use, explicit Save, reopen, native refresh and drill. They never supply a model response or intercept a source response. Numeric expectations derive from normal source create/read APIs and seeded timestamps/runs. Acme checks per-viewer record access and live field-grant revocation. The same saved native view is captured at desktop/narrow widths and light/dark themes without new inference.

The [qualification-only observer](live/DashboardLiveQualification.cs) refuses replacement/protocol factories and forwards the actual installed SDK factory. Both create and resume paths return wrapped sessions; every forwarded `SendAsync` increments the same host counter. Outbound prompt/system context and custom-tool return checks plus bounded synthetic record-id canaries run in memory; counters retain method/event/tool names and counts, never prompts, arguments, result values, credentials or provider transcripts. Source-result row objects are refused before SDK disclosure. The separate [offline observer tests](live/ObserverTests.cs.in) verify forwarding and denial with local test doubles; those tests make no live-provider claim and their doubles are refused by the live host feature. These are SDK send counts: the public factory seam does **not** expose physical model HTTP-request counts or prove the provider's internal model routing.

Successful process exit alone is insufficient. The verifier requires both journeys exactly once, no retries/skips, the complete named screenshot/ARIA/trace inventory, source/readback/oracle/counter attachments, at least three observed actual authoring sends, and zero sends for every required native phase with no privacy guard failures. A prepared or build-only report always has `liveQualified: false`. Even a successful live report requires independent inspection of its current-commit visual evidence before UX acceptance. Treat the full owned output as private; publish only reviewed evidence, never runtime/authentication stores or unreviewed traces.

Offline runner/verdict checks (no host or provider):

```sh
python3 -m unittest discover -s tools/studio/Dashboard.Conformance.Probe -p test_live_qualification.py
```
