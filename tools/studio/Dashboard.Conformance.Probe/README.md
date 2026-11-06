# Independent native Dashboard package witness

This probe builds real product/module archives, restores and publishes an independent package-only consumer in an owned directory, and exercises normal authenticated native record, discovery, query, saved-question and drill operations. It installs `Acme.Processing` through NuGet; neither the consumer nor its generic query engine references product source. It verifies the loaded module/Core bytes against retained nupkg entries and checks actor, tenant, record and category-denial controls. A disabled-contribution restart keeps installed bytes unchanged and makes its dataset unavailable rather than substituting a source.

The consumer uses Foundation's actual normalized-principal and permission services plus native Dashboard API/SQLite resource persistence. A private local authentication fixture supplies predeclared canonical actors and fresh random tokens; no permission evaluator, actor resolver, query implementation or source result is mocked. Its token-refresh authority boundary is explicit. Processing data is created through the module's ordinary API and its ephemeral default store is reseeded after restart. This does not establish durable business-data storage, production authentication, Nuplane dynamic unload, Studio UX or live-provider behavior.

Run with a new absolute directory outside the checkout and a unique local-only version:

```sh
python3 tools/studio/Dashboard.Conformance.Probe/qualify.py \
  --output /absolute/new-owned-dashboard-package-proof \
  --version 4.0.0-dashboard3604.local1
```

Final-source qualification requires a clean committed checkout. `--allow-dirty` is explicitly preliminary and retains source-input hashes plus dirty paths; it never claims exact-commit or resulting-main qualification. Every command/status/log, package identity/hash, locked package-only dependency graph, offline restore and published/loaded archive comparison is retained. A failed stage remains failed; the driver does not retry it automatically.

All hosts bind only a random loopback port, use owned private profiles/stores/caches, and are stopped by their owner on completion/failure. The output directory contains private fixture tokens and should not be uploaded wholesale. Public receipts omit tokens and provider/shared credentials. No package is published remotely and no model/provider is configured or invoked. Existing hosts, stores, credentials and frontend builds are untouched.
