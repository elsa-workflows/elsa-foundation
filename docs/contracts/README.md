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
| `openapi.json` | The HTTP surface as an **OpenAPI 3.1** document: every published route, its verb, path parameters, request/response body schemas (hoisted into `components/schemas`), the permissions it accepts (`x-elsa-permissions`) and the assembly that provides it (`x-elsa-assembly`). Readable by any OpenAPI client generator or validator. |
| `hosts.json` | Which fragments each shipped host (`src/Apps/*`) actually contains, plus the features and expression types it serves — the third term of availability, see below. |
| `vocabularies.json` | The closed value spaces you must match exactly: variable type aliases (the reserved bare set plus the rule for everything else), collection kinds, intrinsic kinds, the `authoredVia` terms, and what each expression type means on the wire. Generated from the product types that define them. |
| `manifest.json` | Per-fragment `sha256:` fingerprints, plus fingerprints of `submit-schema.json`, `hosts.json`, `openapi.json` and `vocabularies.json`. Verify "these contracts match my pinned commit" by string compare. |

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

The same intersection governs **expression types**. The set a host actually serves is the intrinsics
(`Literal`, `Object`, `Input` — always present, published in `Elsa.Expressions.Core`'s fragment with a
null `featureId` because no feature gates them) plus the descriptors of the expression features that host
ships. `Liquid` is the worked example in the other direction: its descriptor is published, but
`Elsa.Expressions.Liquid` is absent from the Workbench, so that host cannot evaluate Liquid at all.

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

## Calling the API

`openapi.json` publishes the HTTP surface so you do not have to probe for it — the running server serves
neither `/swagger` nor `/openapi`, which the benchmark named the single largest gap. It is a standard
**OpenAPI 3.1** document, so point a client generator, a validator or an agent at it rather than teaching
them a private schema. Body schemas are hoisted into `components/schemas` keyed by the CLR body type, so
endpoints sharing a request or response type share one component.

Elsa-specific facts ride as extensions on each operation: `x-elsa-permissions` (the permissions the
endpoint accepts), `x-elsa-allows-anonymous`, `x-elsa-assembly` and `x-elsa-feature-id` — intersect the
latter two with `hosts.json` the same way as features, because an operation whose assembly your host does
not ship is not routable there.

**Success statuses are published only where the endpoint declares one** (`[SuccessStatus]`). An endpoint
that does not declare one publishes a `default` response rather than an asserted `200`, deliberately: a
guessed `200` is precisely the confidently-wrong fact that cost every benchmarked session a failed
assertion. Where an endpoint can answer more than one status, both the set and the rule between them are
published. The workflow publish endpoint is the worked example — `POST /publishing/workflows/{versionId}/publish`
documents `201` *and* `200`, each description stating when it applies (201 creates the publication, 200
updates an existing publication in place).

Path templates are **normalised for OpenAPI**: ASP.NET route constraints are stripped
(`{versionId:regex(^(?!drafts$).+$)}` is published as `{versionId}`) and every remaining placeholder is
declared as a required path parameter, so the template is directly usable as a URL. The constraint itself
is still visible on the endpoint entry in the owning fragment.

A handful of endpoints configure themselves from host options — the identity token endpoint reads which
authentication schemes the host registered, the diagnostics and OTLP ingestion endpoints take their routes
from options. Those are projected against an *empty* host and carry
`"x-elsa-configuration-dependent": true`: the route and authentication shown are this build's defaults, and
a host that overrides those options serves something else. Publishing them flagged beats omitting them —
the token endpoint is the first call a consumer has to make, and it was invisible while these were dropped.
Endpoints that cannot be projected at all would be reported as `ELSACT012` warnings during generation
rather than silently omitted; there are currently none, so `openapi.json` is the complete surface.

The document is regenerated from the same endpoint projection as the fragments on every build, so a new
FastEndpoint appears in it without anyone remembering to update a list; the freshness gate fails the PR
if it does not.

## Behavioural rules the fields alone do not tell you

These are the load-bearing facts a consumer needs that no descriptor field expresses. They are stated
here because omitting them measurably costs more than publishing nothing: a contracts-only consumer
trusts the structural answers and then burns publish cycles guessing the rest.

The machine-readable, test-pinned form of these lives in
[`docs/consumer-guide/claims.json`](../consumer-guide/README.md) — one sentence per claim, each named by a
test that fails if the behaviour changes, gated in both directions by CI. Read that if you are consuming
programmatically; read on for the narrative version.

### Binding a required output

`isRequired` on an output does **not** mean "the runtime fills this in". It means: *if you author an
output target for it, publication demands one, and that target must be a `Variable` expression naming a
**workflow-scope** variable.* Concretely, publication rejects (HTTP 400) when a required output is:

- authored with anything other than a `Variable` expression;
- targeting a variable declared in a container scope rather than workflow scope;
- targeting a variable the workflow does not declare.

**The enforcement is per node, all-or-nothing.** For a leaf activity, if you author **zero** outputs on
the node, no required-output check runs at all and the definition publishes. Author **one** output and
every required output on that node becomes mandatory. This is why the flag looks inconsistent in
practice — `HttpEndpoint` rejects a missing `RouteData` once you have bound `Request`, while
`WriteHttpResponse` publishes happily with all four of its required outputs unbound. It is one rule about
binding state, not a difference between activities.

Two consequences worth internalising:

- Do not pre-bind outputs "to be safe". Binding one output opts that node into full required-output
  enforcement, which is what turns a working definition into a 400.
- `[Output]` defaults `IsRequired` to `true`, so most published `isRequired: true` values are an
  unreviewed default rather than a deliberate declaration. Treat the flag as "must be bound *if you bind
  anything here*", not as "this output matters".

### Choosing an expression type

`enumValues` now publishes the accepted members of every enum-typed input, so `ResponseMode` states
`["async", "sync"]` alongside its `"async"` default rather than leaving the alternatives to be guessed.
What the fragments still do not tell you is what each member *does* — that is per-member consumer notes
(RFC step 3, G3/F1), not yet published. For `ResponseMode` specifically the observable difference is
large: the default `async` answers the triggering request `202` with no incident recorded, and a
`WriteHttpResponse` body never reaches that caller.

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
