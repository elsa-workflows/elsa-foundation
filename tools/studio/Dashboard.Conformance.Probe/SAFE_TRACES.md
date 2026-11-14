# Downloadable Dashboard trace evidence

Raw Playwright archives contain credentials, protected API bodies and signed drill tokens. Keep them in the private qualification output. Never attach an unprocessed archive or whole HTML report to an issue or PR.

`export_safe_trace.py` reconstructs Playwright trace version 8 browser records, retains ordered actions and DOM snapshots for every recorded context, and copies only referenced validated rendering assets. It removes request bodies, dynamic response bodies, headers, cookies, API results, executable source, console arguments and authentication screenshots. It redacts known credentials and their encoded forms. The index records the exact product-source SHA, exporter hash, archive hash, preserved context/action/snapshot counts and omissions. Source links whose snapshots Playwright never recorded are omitted and counted rather than invented.

```sh
python3 export_safe_trace.py /private/output/trace.zip /private/review/safe-trace.zip --source-sha <40-character-source-sha>
python3 -m unittest discover -s . -p test_export_safe_trace.py
```

A successful export is a privacy transformation, not visual acceptance. Before publication:

1. Independently scan the archive against the private credential inventory; never publish that inventory or password hashes.
2. Open the transformed archive in Playwright Trace Viewer and inspect ordered owner and reader actions, resolved DOM snapshots, styles and screenshots. Select a later assertion to inspect resolved drill rows; a click's immediate snapshot may still show loading.
3. Retain original inspected PNGs and reviewed ARIA snapshots, plus an allowlisted qualification summary. Preserve zero, explicit null reasons, unknown coverage, stale provenance and failure states. Do not publish the private source oracle or retained-state manifest, which can embed drill tokens.
4. Publish the archive and its matching index only after that review. Record omitted or unavailable states alongside accepted ones.

The exporter deliberately refuses unknown trace schemas and unsafe archives. Update it explicitly when Playwright changes format. It supports the local owned qualification hosts; external URL targets are withheld. Acme reference business data is ephemeral and is not a process-restart durability promise.
