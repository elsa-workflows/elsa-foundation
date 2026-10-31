# Witness validation guide

Planned entry point (implemented by this work unit):

```sh
python3 tools/studio/Weaver.Conformance.Probe/qualify.py \
  --output /absolute/new-owned-weaver-proof \
  --version 4.0.0-weaver3438
```

Run from committed source with the supported .NET 10/native profile, Python 3, producer Node/pnpm and Chromium automation installed. The output directory must be new. The driver owns isolated product/module producers, consumers, resources and processes; existing developer hosts/credentials are untouched. Nothing publishes remotely.

1. Produce retained product NuGet archives and the real SDK tgz. Build the independent backend and Studio resource packages outside the checkout. Require archive identity/closure and external SDK typecheck evidence.
2. Restore/build/publish ordinary backend and Studio package consumers with frontend tools denied and audited. Start published hosts from explicit isolated state; seed through normal authentication/native APIs. Establish common DLL/browser baseline before resource installation.
3. Install actual resource packages, reconcile and observe serving membership/generation plus module context/tool/renderers. Require a personal native Home layout response and exact revision, not fallback data.
4. Read widget state through Weaver, review and apply a visibility change. Independently read the native personal preference and reload Home; verify visibility and preservation of unrelated canaries. Repeat accepted execution and prove no additional revision/write.
5. Complete the native workflow position journey: changed visible working state, normal validation, whole-change undo, no implicit durable write and separate explicit Save observation.
6. Run authority, stale/concurrent CAS, malformed/foreign/no-op, redaction, cancellation, lost-confirmation and vanished-client controls. Compare actual owning state at every boundary.
7. Remove/restore actual resource packages, including a pending operation and previously consumed prompt. Verify only its contributions change, unavailable outcomes are explicit and intent is not replayed. Run the genuine incompatible SDK variant, retain its failed child, restore original bytes and repeat positives.
8. Run each unchanged provider adapter with its resource-agnostic protocol fixture. Retain deterministic/protocol labels; no live-provider coverage is implied.
9. Require aggregate success only when every required observation and exact identity comparison passes. After generic changes, rerun from a fresh final baseline. Follow review/CI/merge/main gates separately.

See [witness boundaries](contracts/witness.md) for control semantics and [data model](data-model.md) for owner/receipt invariants. Public evidence should link exact source, artifact hashes and case receipts, with credentials/session state kept private. A successful driver is evidence for this witness, not M2–M5 completion.
