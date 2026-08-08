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
| `hosts.json` | Which fragments each shipped host (`src/Apps/*`) actually contains — the third term of availability, see below. |
| `manifest.json` | Per-fragment `sha256:` fingerprints, plus fingerprints of `submit-schema.json` and `hosts.json`. Verify "these contracts match my pinned commit" by string compare. |

Deliberately **not** in fragments: assigned activity version ids and availability (`addable`) — server
state is never published as contract. Resolve version ids at submit time via
`GET design/activities/catalog`.

## Consuming — availability is a three-way intersection

```
available features = your shells.json  ∩  hosts.json[your host].features
```

`hosts.json` publishes both `features` (feature ids, the same vocabulary `shells.json` uses — intersect
directly) and `fragments` (the assembly-level view of the same fact). Every fragment contribution entry
carries its owning `featureId`, so you can go from an available feature to the activities, structures and
expression surface it brings.

**The third term is not optional.** A fragment describes what an assembly contributes *if that assembly is
present*; it is not a claim that any particular host ships it. Enabling a feature whose assembly the host
does not carry is a silent no-op — the shell logs `requested N feature(s) that are not available in the
runtime feature catalog` and the activities simply never appear.

This is not a rare edge: 14 of the 94 fragments describe assemblies that `Elsa.Workbench` does not
reference, and two of those carry authoring surface — `ActivitiesScripting` publishes the `RunJavaScript`
activity, and `Liquid` publishes an expression descriptor for a language the Workbench cannot evaluate at
all. Consult `hosts.json` before concluding an activity or expression language is usable.

**Validated against the running server**: requesting all fragment-declared features against a Workbench
image built from this branch made the shell report exactly these 14 as *not available in the runtime
feature catalog* — the same 14, feature id for feature id, that this build-time index excludes. The index
is therefore a faithful stand-in for booting the image and asking it.

A runtime-kind attribute would **not** substitute for this: `Elsa.Activities.Scripting` and
`Elsa.Activities.Http` both declare `elsa.server`, yet only one ships in the Workbench.

If you run a host that is not in this repository, compute the third term from your own image the same way
the generator does — read the library names out of your host's `.deps.json` and intersect with the
fragment names.

Each fragment also ships inside its assembly as the embedded resource `elsa.contract.json` (byte-identical
to the committed file at any green commit), which is another way to answer "is this contract actually in
my image?" for a host built elsewhere.

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
- Activity `version` is published **without SemVer build metadata**: the assembly's informational
  version carries the SourceLink commit sha (`1.0.0+<sha>`), identity is build-metadata-insensitive by
  design, and a committed fragment must not change on every commit.
- Feature option `defaultValue` is **static-only** (explicit `[ManifestSetting(DefaultValue)]`, compiled
  initializer constant, or synthesizable `default(T)`): defaults computed at construction
  (`Path.Combine(Environment.CurrentDirectory, …)`, `TimeSpan.FromSeconds(…)`, `string.Empty`,
  `typeof(…)` names) publish `null` — they embed the generator's environment or are not IL constants.
  The runtime feature catalog still shows live values.
- Generation warnings (`ELSACT006`/`ELSACT009`) mean a feature's registration or some types could not be
  loaded during projection — the fragment may under-describe that assembly. Known cases: features
  requiring configuration at construction (JSON reconciliation sources) and assemblies whose external
  NuGet dependencies are outside the app closure (MongoDB, Fluid/Liquid, GitHub Copilot SDK).
- A same-version content change to an activity (e.g. enriching descriptors) changes its `contentHash`;
  under reconciliation Model X a pre-existing database then throws `ActivityVersionHashMismatchException`
  by design — rebuild against a fresh DB or ship a new activity version.
