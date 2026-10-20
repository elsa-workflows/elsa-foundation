# Research: Elsa 3 converter faithful fixes

Baseline: the new tests in `Elsa3FaithfulFixesTests` run against `main` at `88e513b7e` (spec 247 merged) fail with
exactly the diagnostics the 2026-10-19 measurement reported.

## R-1 WriteLine.text

- **Elsa 3 behaviour**: `WriteLine.Text` is `Input<string>`; unset, `TextWriter.WriteLine(null)` writes an empty line.
- **Converter today**: `Carry("Text", "text")`. Elsa 4 `text` is required, so `CheckRequiredInputs` raises
  `ELS3-ACTIVITY-REQUIRED-INPUT-UNBOUND` for an absent or blank `text`.
- **Decision**: R2 default flip to `""`. `ApplyDefaults` already emits a string default as a literal when the value is
  unbound, but `IsBound("")` is false, so the required check also accepts the literal its entry's R2 rule emitted.
- **Alternatives**: make `IsBound` accept blank literals (rejected: every other required member would then convert
  unbound, and R3's "absent or default" check depends on it); special-case WriteLine in the check (rejected: the R2 rule
  is the general mechanism, and the exemption is phrased by rule, not by activity).
- **Edge**: a whitespace-only `text` literal is unbound by `IsBound` and becomes `""`. That differs from Elsa 3 only in
  the whitespace written; it is accepted as negligible and not worth a special case.

## R-2 canStartWorkflow on a non-trigger

- **Elsa 3 behaviour (3.5.3 `Flowchart.GetStartActivity`)**: the triggered activity if one fired, else `Start`, else the
  first activity with `CanStartWorkflow`, else the first activity with no inbound connection. On a non-trigger the
  flag has no other effect, inside or outside a flowchart.
- **Converter today**: `Elsa3FlowchartGraphBuilder.StartCandidate` already applies that order to `startNodeId`. The
  normalizer then refuses the flag on any entry without a `CanStartWorkflow` rule (`REMOVED-INPUT-BOUND`), and the
  spec 238 check refuses a flagged child that is led into and is not the start (`GRAPH-START-TRIGGER-MISPLACED`).
- **Decision**: drop the flag on non-triggers. A non-trigger is an entry without a `CanStartWorkflow` rule. The graph
  builder's start-trigger check is about where a trigger can fire, so it skips such nodes. Nodes with no map entry
  (alias, verbatim catalog key, replacement) keep today's handling, because the converter cannot tell whether they are
  triggers.
- **Trigger set**: in the Elsa 3 surface, the triggers are Cron, Event, HttpEndpoint, StartAt and Timer. StartAt has no
  counterpart; the other four declare `CanStartWorkflow` rules (carry on HttpEndpoint, R2 on Event, start-only on Cron
  and Timer). A coverage fact pins this so a new trigger entry cannot silently lose the rule.

## R-3 ObjectDictionary

- Elsa 3 declares Dispatch/ExecuteWorkflow `Input` as `ObjectDictionary` (a `Dictionary<string, object>`); the JTest
  definitions bind it to JavaScript returning an object. The JSON-only contract already accepts `IDictionary`,
  `Dictionary` and `ExpandoObject`, so `ObjectDictionary` joins the list.
- Once accepted, lowering compared `ObjectDictionary` (single) with Elsa 4 `Inputs` (dictionary of `JsonElement`) and
  raised `VF-IMP-008`. That is resolved by R-4.

## R-4 VF-IMP-008 against the normalized contract

Measured member types (real catalog) and declared Elsa 3 aliases:

| Member | Elsa 3 declared | `ReadType` today | Elsa 4 member |
|---|---|---|---|
| WriteHttpResponse `StatusCode` | `System.Net.HttpStatusCode, System.Net.Primitives` | `HttpStatusCode` | `Int32` |
| WriteHttpResponse `Headers` | `HttpHeaders` | `HttpHeaders` | `String[]`, dictionary |
| HttpEndpoint `SupportedMethods` | `List<String>` | `List<String>` | `String`, list |
| Dispatch/ExecuteWorkflow `Inputs` | `ObjectDictionary` | `ObjectDictionary` | `JsonElement`, dictionary |

- `ReadType` keeps only the alias's simple name and always returns a single value, so a collection never matches, and
  an enum whose literal normalization already turned into an integer is compared by its Elsa 3 name.
- **Decision**: `ReadType` maps the declared Elsa 3 alias to the Elsa 4 shape it has after normalization: a small table
  for the three Elsa 3-specific names (`HttpStatusCode` → `Int32`, `HttpHeaders` → dictionary of `String[]`,
  `ObjectDictionary` → dictionary of `Object`), and Elsa 3's generic list syntax to a list of its element. `TypesMatch`
  treats an untyped element (`Object`, `JsonElement`, …) as matching when the collection kinds agree, as it already does
  for single values.
- `HttpStatusCode` → `Int32` is safe only because R5 (`EnumNameToInt`) runs first: a literal name is narrowed to an
  integer, and an expression declared `HttpStatusCode` is refused with `VALUE-OUTSIDE-CONTRACT` before lowering.
- **Alternatives**: resolve the alias through `LegacyClrTypeResolver` (rejected: the Elsa 3 types are not loaded here,
  so every unknown alias would fall back to `Object` and match anything); rewrite `typeName` in the normalizer
  (rejected: `Headers` and `SupportedMethods` are R1 carries the normalizer does not inspect, so the check would be
  split over two stages).
