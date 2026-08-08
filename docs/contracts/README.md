# Consumer contracts (generated, committed)

Machine-readable **consumer contract fragments** — the authoring surface each feature assembly
contributes, published as a build output per [RFC #1191](https://github.com/elsa-workflows/elsa-foundation/issues/1191)
(spec `specs/149-consumer-contract-fragments/`). A consumer pins a commit and *reads* everything needed
to author workflow definitions against it: no server boot, no endpoint dumps, no source reading.

## Layout

| Path | Content |
|---|---|
| `fragments/<Assembly>.json` | One fragment per contributing assembly: feature metadata (id, `dependsOn`, options), activity contracts (inputs with `defaultValue`/`hasStaticDefault`, outputs with `isRequired`, ports, container structure), structure kinds with payload schemas, expression surface (descriptors, JS declarations, script-sandbox globals), engine intrinsics. |
| `submit-schema.json` | The workflow-definition submit-body schema — produced by the same handler that serves `GET design/workflows/definitions/submit/schema`. |
| `manifest.json` | Per-fragment `sha256:` fingerprints. Verify "these contracts match my pinned commit" by string compare. |

Deliberately **not** in fragments: assigned activity version ids and availability (`addable`) — server
state is never published as contract. Resolve version ids at submit time via
`GET design/activities/catalog`.

## Consuming

Intersect the merged fragments with your own composition: every contribution entry carries the owning
`featureId`; filter by the feature ids enabled in your `shells.json`. Each fragment also ships inside its
assembly as the embedded resource `elsa.contract.json` (byte-identical to the committed file at any green
commit).

## Regenerating

The artifacts are generated from built assemblies and committed; CI fails PRs whose committed contracts
lag the code:

```bash
dotnet build Elsa.Server.slnx -c Release
dotnet run --project tools/contracts/Elsa.Contracts.Generator -- merge
```

Verify freshness (what CI runs): `dotnet run --project tools/contracts/Elsa.Contracts.Generator -- check`

`git diff docs/contracts/` between two tags is the machine-readable compatibility report.

## One-projection rule

Fragment content is produced by the same product code the runtime serves from: the CLR activity scanner
that reconciliation persists (constitution §E2.8 — the catalog endpoint reads persisted rows), the same
schema exporter, the same manifest-hint options projection, the same structure/intrinsic provider types.
An equivalence test (`tests/Elsa/Contracts/Tests`) asserts catalog endpoint output equals the merged
fragments of the composed features plus the server-state overlay.

## Notes for feature authors

- Contract-projectable contributors (structure handlers, expression providers, JS declaration
  contributors) must be resolvable from their feature's `ConfigureServices` (plus its declared
  `DependsOn` closure) or constructible without dependencies — otherwise the generator raises
  `ELSACT004`.
- Every input with a statically representable CLR default publishes it (`hasStaticDefault: true`);
  computed or conditional initializers publish `hasStaticDefault: false` — an honest "no static default",
  never a guess.
- Generation warnings (`ELSACT006`/`ELSACT009`) mean a feature's registration or some types could not be
  loaded during projection — the fragment may under-describe that assembly. Known cases: features
  requiring configuration at construction (JSON reconciliation sources) and assemblies whose external
  NuGet dependencies are outside the app closure (MongoDB, Fluid/Liquid, GitHub Copilot SDK).
- A same-version content change to an activity (e.g. enriching descriptors) changes its `contentHash`;
  under reconciliation Model X a pre-existing database then throws `ActivityVersionHashMismatchException`
  by design — rebuild against a fresh DB or ship a new activity version.
