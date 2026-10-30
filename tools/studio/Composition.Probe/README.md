# Studio composition qualification

Actual package/HTTP/browser/native-store qualification for [#3364](https://github.com/elsa-workflows/elsa-foundation/issues/3364). Run from the repository root after building Studio producer frontend assets and committing product inputs:

```sh
python3 tools/studio/Composition.Probe/qualify.py
```

Requires the supported .NET10/native profile, Python3, producer Node/pnpm and the Studio Playwright Chromium install. The producer builds/packs the actual22Studio projects plus lightweight Modularity.Core/Nuplane at one injected candidate version. Nothing publishes remotely. `--version` chooses that local version and `--output` chooses a new owned artifact directory. For supplied archives use `--packages /absolute/candidates --reference-web /absolute/matching-published-Web`; producer provenance is external in that mode.

The qualifier creates four external PackageReference compositions: minimal, Themes-only, Nuplane administration and a combined reference-style host with root Console Stream. Static profiles reference the owned custom module; native profiles load its actual archives from an explicitly enrolled immutable extracted store. The deployed runtime feed is an intentional input, distinct from hidden build feed/source/caches. The actual unchanged reference Web distribution is separately published and exercised with its14module/Home/root/deep/shared-runtime journey; the smaller external combined composition is not called the full distribution.

Every consumer performs fresh isolated/locked restore and verifies package-only graphs, intended dependency absence and archive/cache/published DLL hashes. The independently authored Acme module contributes navigation and root/direct routes through Core/CShells/public browser SDK contracts, with shared React and exact packaged JS/CSS bytes. It registers an immutable string witness per serving generation; no events, contributor interfaces or scheduled tasks. This is an owned test fixture, not a shipped package/default catalog addition.

Execution includes16fresh-process cold authorization cases; trusted hub query tokens versus ordinary endpoints; read/manage policies and server-only management key; static/native client-module suppression; managed theme/PNGmaterial/default persistence through actual restart and fresh-browser application; and a real authenticated WebSocket SignalR connection across actual v1->v2 reconciliation. Current/retained scopes must expose different expected services, the same connection must acknowledge pre/post records and disconnect cleanly.

The default run includes meaningful causal controls. Missing customJS is a failed404 consumer observation, then identical restored bytes/browser succeed. AutoReload=false completes the genuine expected v2 package update, including its actual archive hash, but fails the specific serving-generation assertion; normal and restored network runs succeed. A separate actual no-update reconciliation must be rejected as invalid evidence for that control. Child failed cases stay `status: failed` with a separate expectedFailureObserved assertion. Arbitrary restore/network/SDK/native refusals never count as expected controls. The process exits0 only when the complete positive/control/restored qualification passes; it exits1 for any unmet condition. The [CI orchestrator](../README.md) runs this alongside the existing archive, API, Hosting and adapter/pruning proofs.

Consumer restore/publish/runtime deny and audit node/npm/pnpm/npx; producer and browser automation stay outside that denied environment. Reports retain actual source/tool/template/driver hashes, archives, lock/restore graphs, HTTP/browser/auth/state/lifecycle observations, precise controls and owned process cleanup. Only owned server/client processes are stopped. Native cutover is an explicit selection for the owned fixture; no automatic enrollment of an operator's store or local prune substitute is introduced. Actual supported-native refusal is a failed qualification, not a skip.

The controlled loopback backend follows the existing session/registry contracts; this proves Studio transport/authorization, not a deployed Elsa Server. No arbitrary filesystem mutation, linked-source cycle recovery, performance measurement, release/deployment or publisher activation is implied. See the existing [Hosting browser](../HostingAssets.Probe/README.md), [Api closure](../ApiPackageClosure.Probe/README.md) and [accepted hosting contract](../../../docs/reports/studio-hosting-contract.md) for their distinct scopes.

## Weaver candidate source fixture

Program #3428 uses the `WeaverJourney.Backend.*.in` and `WeaverJourney.Studio.*.in` templates for the bounded #3437 journeys described in the [candidate contract](../../../specs/1018-weaver-module-contributions/contracts/contributions.md). The Studio fixture consumes public module/SDK surfaces and owns its note context, review rendering and resource refresh. Its assembly/module identity is `Weaver.Journey`; the RCL package identity is `Weaver.Journey.Studio`.

Instantiate the frontend fixture into a new owned directory with:

```sh
python3 tools/studio/Composition.Probe/prepare_weaver_studio.py --output /absolute/new-owned/studio-fixture
```

This command writes source and hash evidence only. It does not build or start a host. The M1 fixture has source project references and explicitly records `independentPackageWitness: false`; #3438 must separately establish real package consumption before contract freeze.

After the owning delivery run starts fresh Studio/backend processes with the public composition APIs and supplies a seeded workflow, run the browser observations against their explicit URLs:

```sh
ELSA_WEAVER_OWNED_URL=http://127.0.0.1:owned-studio-port \
ELSA_WEAVER_OWNED_BACKEND_URL=http://127.0.0.1:owned-backend-port \
ELSA_WEAVER_WORKFLOW_ID=owned-definition-id \
ELSA_WEAVER_ACTIVITY_ID=owned-node-id \
pnpm --dir Studio exec playwright test --config tests/browser/playwright.weaver-composed.config.ts
```

The dedicated suite starts no host, uses no mocked network routes, and rejects the known developer ports. It observes native working state/undo without implicit persistence, and the unrelated note's actual file-backed revision/value/write count, including duplicate execution. Its results are deterministic composed-host evidence; they are separate from provider-protocol tests and any opt-in live-provider qualification.
