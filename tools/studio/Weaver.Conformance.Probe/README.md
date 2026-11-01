# Independent Weaver conformance witness

The complete package witness at `76ec58d0061135cfa028e1bc47ec7639fd139e7f` qualifies the bounded M1 contribution contract: all 38 cases completed across 135 stages, with 16 positives and 22 recognized Failed controls. Original and restored resource/workflow journeys run through both actual Copilot and Anthropic adapters. See the [owning #3438 receipt](https://github.com/elsa-workflows/elsa-foundation/issues/3438#issuecomment-6538498830), [curated evidence](../../../specs/1174-weaver-package-witness/evidence/qualification-76ec58d00.json), and [artifact identities](../../../specs/1174-weaver-package-witness/quickstart.md#qualified-code-baseline).

The qualified code baseline has 80 product packages and a retained SDK tgz, external module authors, and two package-only consumers. All 155 backend and 10,108 Studio common baseline files remain identical through native add/remove/incompatible/restore transitions. Consumer product-source references and frontend invocations are zero. Both owned hosts exit 0 with empty runtime caches. This is deterministic provider-protocol evidence; live-provider success is unclaimed.

Follow [owning issue #3438](https://github.com/elsa-workflows/elsa-foundation/issues/3438) and [PR #3497](https://github.com/elsa-workflows/elsa-foundation/pull/3497) for current final-source qualification, required exact-head CI, independent review, merge and resulting-main verification. [T026–T028](../../../specs/1174-weaver-package-witness/tasks.md) define those mandatory integration gates. This M1 proof does not establish durable recovery, full current-module/workflow coverage, new binary compatibility or M2–M5 completion.

## Reproduce

Run from committed source with a new absolute output directory outside the checkout and a unique local version:

```sh
python3 tools/studio/Weaver.Conformance.Probe/qualify.py \
  --output /absolute/new-owned-weaver-proof \
  --version 4.0.0-weaver3438.local1
```

Producer/module-author steps need .NET 10, Python 3, Node/pnpm 11.9 and Chromium automation. Ordinary consumer restore/build/publish/run uses the prebuilt distributions with frontend tools denied and audited. The driver retains locks, closure/cache/archive hashes, common published bytes, actual native membership/serving generations and owner/browser receipts. It only starts owned processes with isolated native state and normal authentication. Nothing publishes remotely or consumes existing developer credentials/hosts. [Consumer recipes](../../../specs/1174-weaver-package-witness/contracts/hosts.md) and the [validation guide](../../../specs/1174-weaver-package-witness/quickstart.md) describe the sequence.

The [integration workflow](../../../.github/workflows/weaver-conformance.yml) runs the same complete qualifier with read-only repository permissions. Partial execution, missing observations, wrong-cause failures, altered artifacts and source/frontend consumer shortcuts cannot qualify. Common changes require fresh complete distributions and all cases again.

## Cross-domain contributions

The separate `Acme.Home.Weaver` package contributes `WidgetContextSource`, `WidgetCapabilities`, `ReadWidgetTool` and `SetWidgetVisibilityTool` through `AcmeHomeWidgetFeature` and the existing named collection lifecycle. It owns the personal Home widget's identity, bounded context/guidance, validation, permissions, CAS and observed-result semantics. The actual Preferences service/native store owns persistence; independent native document reads and Home observations establish the effect while preserving unrelated JSON.

`Acme.Home.Studio` uses the public SDK for the Home widget, settings route, context, prompt/tool declarations and review/result rendering. `Acme.Weaver.ProtocolFixtures` separately implements the public Copilot and Anthropic client factories. Both adapter artifacts are installed in the common baseline, and exactly one provider feature/profile is enabled at a time. The protocol fixture neither references the resource package nor interprets its payload; the real provider adapters and generic engine execute the journeys.

These external packages are qualification fixtures, not shipped product modules. The M1 baseline's packed external tests comprised 78 resource and 21 protocol cases. The #3440 size extension preserves those 78 and adds 45 resource cases: compiled discovery and packed execution match all 123 in component qualification, with the separate Studio author typechecked/built/packed against the retained SDK. Its probe has 160 passing component checks, including distinct automatic SSE and reviewed HTTP receipt paths. This component evidence reuses source-bound product archives and does not establish fresh native or final #3440 qualification. The [Agent catalog](../../../Extensions/Agent/src/Elsa.Agent/EXTENSION_POINTS.md) indexes their public seams; the [canonical M1 contract and preview migration](../../../specs/1018-weaver-module-contributions/contracts/contributions.md) own generic API meanings, removed surfaces and the supported workflow subset.

## Evidence and control boundaries

`browser.mjs` drives real resource review/readback and native workflow export, validation, whole Undo, explicit Save and reload. Controlled ACK faults alter or lose an actual exchange after the native browser edit. `owner_resources.py` seeds through native APIs; `agent_transport.py` retains actual HTTP/SSE/provider observations. Reads require matching native ContributedResult and provider payloads; execution requires the owning receipt. Transport success alone cannot establish an effect.

Expected negative children stay Failed with separately recognized precise causes. Before-effect controls retain unchanged owner state; concurrent CAS preserves its actual winner, and post-dispatch uncertainty/confirmation retry cannot replay an accepted edit. The general private IAM setup uses normal persisted principals and predeclared grants, followed by normal sign-in. Module-owned CAS rendezvous and provider pre-call gates only coordinate real native operations; they do not replace stores or the engine or add product contracts.

The exporter selects evidence references rather than uploading the run directory. The 76ec safe export contains 11,823 references with original statuses and no private authentication, seed configuration, runtime databases or keys. Report/index hashes are in the curated receipt. Earlier failed runs and diagnostic component versions remain preserved on the owning issue/private outputs; this complete run does not relabel them. [Tasks](../../../specs/1174-weaver-package-witness/tasks.md#causal-corrections-retained-for-review) retain useful causal correction references.


## Current-authority context qualification (#3439)

The same immutable package run now additionally requires four native cases in
`contextDisclosureQualification`, separate from the original38case records. Both actual adapters
must receive no request/call/continuation after accepted-message read authority is withdrawn and
after an actual accepted effect's receipt becomes undisclosable. Native Identity revision/grants,
fresh same-actor authentication, the exact owner403, strict SSE cause, unchanged trace prefixes,
retained receipt equality and final independent owner readback establish these outcomes. Wrong
transport/auth/session errors cannot substitute for disclosure denial. Each stage is bounded and
never retried automatically. The original M1 cases and their recognized Failed controls remain
mandatory. This evidence is deterministic provider-protocol qualification, not a live-provider claim.

Modules link tools to their exact disclosure source and own `AdmitDisclosureAsync`; see the
[Agent catalog](../../../Extensions/Agent/src/Elsa.Agent/EXTENSION_POINTS.md#context-and-result-disclosure-3439-under-qualification).
Session stores must preserve complete journal provenance before a result can be sent to a provider.
