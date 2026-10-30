# Independent Weaver conformance witness

Program [#3428](https://github.com/elsa-workflows/elsa-foundation/issues/3428), leaf [#3438](https://github.com/elsa-workflows/elsa-foundation/issues/3438). Implementation is in progress; no completed independent distribution proof or frozen contract is claimed.

This tool builds an external module owning one personal Home widget's context, read/review/application semantics and presentation. The actual Preferences service/native store owns persistence; native Home and authenticated document reads establish observed results. Separate generic provider-protocol fixtures exercise unchanged adapters without resource dependencies or live-provider credentials.

Run the complete qualifier from committed source:

```sh
python3 tools/studio/Weaver.Conformance.Probe/qualify.py \
  --output /absolute/new-owned-weaver-proof \
  --version 4.0.0-weaver3438
```

The output must be outside the checkout and new. Producer/module-author steps need .NET 10, Python 3 and Node/pnpm; browser automation needs Chromium. Ordinary host restore/build/publish/run consumes prebuilt packages with frontend tools denied and audited. Nothing publishes remotely. Exact committed inputs, archives, locks, published bytes, native membership/generations and case evidence must agree; partial execution cannot count as success.

Consumer recipes and validation boundaries are in [spec1171](../../../specs/1171-weaver-package-witness/plan.md). The [canonical contribution contract](../../../specs/1018-weaver-module-contributions/contracts/contributions.md) owns generic API meanings. This directory's `witness.json` contains external author/driver identities; product code must not depend on it.

Only verified owned processes/resources are used. Fixture credentials/auth state stay private; receipts and provider traces omit them. The aggregate preserves expected failing controls as failed children, with separate recognition of the intended cause. A missing/incompatible module, stale/denied operation or uncertain outcome cannot become fabricated success. Supported retention/recovery limits and deterministic/protocol/live evidence remain distinct.

## Cross-domain contributions

The independently authored resource backend contributes `WidgetContextSource`, `WidgetCapabilities`, `ReadWidgetTool` and `SetWidgetVisibilityTool` through `AcmeHomeWidgetFeature` and the existing named collection lifecycle. It consumes Preferences.Core/Identity.Core for its owning authorized resource. Its Studio package uses public Home/widget and `api.agent` context/tool/rendering contracts. The separate protocol package implements public Copilot/Anthropic factories; it neither references the resource package nor interprets its payload. These packages are qualification fixtures, not shipped product modules. The [Agent catalog](../../../Extensions/Agent/src/Elsa.Agent/EXTENSION_POINTS.md) indexes their cross-domain contracts; the [preview migration](../../../specs/1018-weaver-module-contributions/contracts/contributions.md#preview-migration) describes the removed APIs and host package changes.

## Current execution status

Product distributions, external author builds/tests and both consumer builds have bounded receipts on #3438. Both native hosts have now passed activation and authentication. The complete journey coordinator and CI routing are unfinished; the command above is the intended complete entry point, not a passing qualification claim. The startup readability correction has bounded evidence; a subsequent native feature-apply run exposed a separate backend configuration-refresh failure before the resource tools became available. Its owning Modularity API correction passes a causal real-host regression and the whole401testproject, but still needs genuine package requalification before journey qualification resumes.

`browser.mjs` drives actual resource review/readback and native workflow export, whole Undo, explicit Save and validation observations. It accepts an absolute private configuration JSON path; it never fabricates a response or registers a module. `owner_resources.py` seeds through advertised native APIs, and `agent_transport.py` retains real HTTP/SSE/protocol observations without inferring an owning effect. These helpers are in progress. A passing browser subcase alone has no claim to the full witness: the coordinator must correlate the unchanged provider adapter and satisfy all38cases in `evidence.py`. The genuine unsupported-SDK author variant is compiled separately; producing it does not prove native rejection or restoration.

Diagnostics, intermediate consumer builds and individually passing tests do not freeze the contract or establish the complete context → tool → review → applied-result journey. Every required case must run against the real owners before acceptance, followed by final coherent distributions, review/CI/merge and resulting-main verification.
