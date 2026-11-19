# Witness validation guide

Run the implemented qualifier from committed source, using a new output directory and a unique local version:

```sh
python3 tools/studio/Weaver.Conformance.Probe/qualify.py \
  --output /absolute/new-owned-weaver-proof \
  --version 4.0.0-weaver3438.local1
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

## Qualified code baseline

The [curated 76ec receipt](evidence/qualification-76ec58d00.json) and [owning issue record](https://github.com/elsa-workflows/elsa-foundation/issues/3438#issuecomment-6538498830) retain the complete witness:

| Observation | Recorded result |
|---|---|
| Qualified source / main base | `76ec58d0061135cfa028e1bc47ec7639fd139e7f` / `a01980064aed293502b9ada57a682286d950c7fd` |
| Local product version | `4.0.0-weaver3438.20261031.8`; 80 product packages plus retained SDK tgz |
| Stages / cases | 135 stages exit 0; all 38 cases: 16 positive, 22 expected failed controls with their precise causes |
| Original and restored positives | Independent resource and native workflow through both actual Copilot and Anthropic adapters |
| Immutable common files | 155 backend and 10,108 Studio baseline files unchanged through the extension sequence |
| Consumer closure | Zero product source references and zero frontend build invocations for both consumers |
| Cleanup / safe export | Both hosts exit 0; runtime caches empty; 11,823 exported evidence references exclude private runtime/auth state |
| Qualification report SHA-256 | `7dd769bf6cfeb109f0019c4e080c7cd126e5767c5474c57fd3106c1afb233760` |
| Safe-export index SHA-256 | `17a352e1cdcf91d038d37aac23b7ff0234f6b40719eb93c61645a0bb3cd20d79` |

Expected controls remain Failed children; their intended causes are separately recognized. The packed author tests comprise 78 resource cases and 21 protocol cases. Provider evidence uses separate factory implementations with the actual unchanged adapters; live-provider access is unclaimed.

The [M1 minimum contract](../1018-weaver-module-contributions/contracts/contributions.md) is recorded in implementation [PR #3497](https://github.com/elsa-workflows/elsa-foundation/pull/3497). This receipt describes the qualified code baseline; [owning issue #3438](https://github.com/elsa-workflows/elsa-foundation/issues/3438) records current final-head and resulting-main evidence. Integration requires fresh qualification and required CI, independent review, authorized merge and resulting-main verification ([T026–T028](tasks.md)). This M1 witness does not qualify M2–M5, durable recovery or broader workflow/current-module coverage.
