# Dynamic (`Any`) Expression Values Materialize As `JsonNode` With Per-Engine Adapters

**Status:** Proposed — requires architecture-meeting ratification (no constitution amendment; realises the direction fixed by [ADR 0035](0035-serialization-unifies-on-the-alias-registry-and-retires-open-object-polymorphism.md) D2).

**Date:** 2026-07-08

**Context:** [ADR 0035](0035-serialization-unifies-on-the-alias-registry-and-retires-open-object-polymorphism.md) D2
(serialization direction), [#553](https://github.com/elsa-workflows/elsa-foundation/issues/553) (this work unit),
[#552](https://github.com/elsa-workflows/elsa-foundation/issues/552) (investigation),
[ADR 0030](0030-runtime-expression-evaluation-uses-a-parameter-threaded-live-carrier.md) (expression carrier),
`specs/081` (typed argument model).

---

**The problem this fixes.** A workflow variable can hold *schemaless, dynamic* JSON — the canonical example is an
HTTP Endpoint activity payload — declared with the `Any` alias (`WellKnownTypeNames.Any`). It is stored as opaque
JSON (`DurableValueState.InlineValue` is already a `JsonElement?`) and must be readable from every expression
language: `input.customer.name` in JavaScript, `{{ Input.customer.name }}` in Liquid, and the equivalents in Python,
C#, and any module-provided language.

Today that dynamic value is materialized as `System.Dynamic.ExpandoObject`, and the expression stack is threaded
with `ExpandoObject` special-cases: the JavaScript type descriptors and declaration contributor map
`WellKnownTypeNames.Any` to `typeof(ExpandoObject)`; the Jint execution context has a `value is not ExpandoObject`
branch; three JavaScript pre-processors build `ExpandoObject` containers for `variables` / `input` / `output` /
`args`; Liquid registers `ExpandoObject` member access. `ExpandoObject` also **requires a custom serialization
converter** (it is the `PolymorphicObjectConverter`'s internal materialization target) and is **lossy on JSON
number kinds** (it boxes to `long`/`double`, discarding the original token). ADR 0035 D2 already ratified the
*direction* — retire `ExpandoObject`, adopt `JsonNode` — and deferred the representation refactor and its own
expressions ADR to this unit. This ADR fixes the runtime contract; the refactor is #553; the global-converter and
polymorphic-converter deletion is the unit *after* this one.

---

## Decisions

### D1 — The canonical in-memory type for a dynamic (`Any`) value is `JsonNode`

An `Any`-declared value, once it enters the expression execution context (as a workflow variable, workflow input,
activity output, or `args` entry), **is a `System.Text.Json.Nodes.JsonNode`** (`JsonObject` / `JsonArray` /
`JsonValue`). `ExpandoObject` is retired as the `Any` representation.

`JsonNode` is mutable (workflows can build and modify objects, unlike read-only `JsonElement`), System.Text.Json
native (no bespoke serialization converter — it round-trips through STJ directly), and **lossless on number kinds**
(it preserves the original JSON number token rather than boxing to `long`/`double`). These are exactly the
properties `ExpandoObject` lacks. Rationale and the rejected `ExpandoObject` alternative are recorded in ADR 0035 D2.

### D2 — Storage stays opaque `JsonElement`; materialization to `JsonNode` is a single shared boundary step

The *stored* form is unchanged and remains the opaque, verbatim `JsonElement` of ADR 0035 D3 —
**never canonicalized, never rewritten.** The `JsonElement → JsonNode` conversion is a single, engine-independent
step performed once when a durable `Any` value is materialized into the live execution context; the reverse
(`JsonNode → JsonElement`) happens once when a dynamic value is captured back to durable state. This keeps the
storage layer ignorant of `JsonNode` and keeps the runtime representation out of the persistence contract.

Materialization is a structural copy, not a re-serialization of author bytes: it introduces no key reordering and
no number reformatting, so it does not disturb the deterministic content hash (ADR 0034 / `specs/086`) or the
opaque-verbatim guarantee (ADR 0035 D3).

### D3 — The per-engine adapter is the only extension seam; no new cross-engine abstraction

Each expression module owns an **adapter** that binds `JsonNode` into that engine, using the engine's *existing*
idiomatic interop point. There is **no new shared Elsa interface** for dynamic values — the seam is the engine's
own extension mechanism:

- **JavaScript (Jint):** Jint (4.9) **binds `JsonObject` / `JsonArray` / `JsonValue` natively** — `SetValue` /
  `JsValue.FromObject` surface member access, indexing, enumeration, and scalar coercion without a custom
  converter (verified empirically; the existing `JsonElementConverter` already relies on this by converting
  `JsonElement → JsonObject → JS`). No new Jint `IObjectConverter` is therefore required; `JsonElementConverter`
  is retained to bridge the `JsonElement` form until the next unit. The engine's `IObjectConverter` set remains
  the extension seam should a future Jint version need explicit `JsonNode` handling.
- **Liquid (Fluid):** a `FluidValue` / `MemberAccessStrategy` registration for `JsonObject` / `JsonArray` /
  `JsonValue`. Fluid (2.31) ships native `System.Text.Json.Nodes` support, so `{{ var.field }}` resolves through
  the built-in JSON value model.
- **Python and any module-provided language:** each provides one adapter of the same shape. Nothing in storage,
  materialization, or the other engines changes when a language is added.

**Adapter contract (per engine).** The adapter has two directions:

1. **Bind (inbound, required):** given a `JsonNode`, expose it to the engine so member access (`x.field`),
   indexing (`x[i]`), enumeration, and scalar coercion behave as the language expects. Objects → the engine's
   map/object model, arrays → its array model, `JsonValue` scalars → the corresponding primitive, JSON `null` →
   the engine's null.
2. **Lift (outbound):** a dynamic value *produced* by an expression (a script that returns/mutates an object)
   destined for an `Any` slot is normalised back to `JsonNode` so it re-enters the canonical form. An engine that
   has no bespoke outbound representation may satisfy this by the D2 serialization round-trip (engine value →
   JSON → `JsonNode`), which is how the runtime captures a dynamic result to durable state today.

Adapters convert **at the engine boundary only.** They do **not** register a global `JsonConverter<JsonNode>` /
`<JsonElement>` — see D4.

### D4 — No global `JsonNode` serialization converter in this unit

A *global* `JsonConverter<JsonNode>` or `<JsonElement>` collides with the still-present `PolymorphicObjectConverter`
(both are that converter's internal buffer types) and stack-overflows (ADR 0035 gotcha; #551). This unit therefore
uses `JsonNode` **only as the runtime representation**, with conversion localised to the per-engine adapters and the
D2 boundary step. Introducing a global `JsonNode` converter becomes safe only **after** the polymorphic converter is
deleted — that is the next sequenced unit (ADR 0035 §D7 / D5), not this one. `JsonNode` still serializes correctly
today because STJ has built-in `JsonNode` support; we simply do not *register a global override* for it.

### D5 — `ExpandoObject` special-casing is removed, not relocated

The migration deletes rather than moves the `ExpandoObject` coupling: the `typeof(ExpandoObject)` entries in the
JavaScript type descriptors and declaration contributor (the `WellKnownTypeNames.Any` mapping becomes the dynamic
`JsonNode` binding), the `value is not ExpandoObject` branch in the Jint execution context, the `ExpandoObject`
container construction in the `variables` / `materialization-accessors` / `args` pre-processors, and the
`ExpandoObject` Liquid member-access registration. Dynamic container objects (`variables`, `input`, `output`,
`args`) are built as `JsonObject`. Unreleased software (ADR 0035 D7) → no back-compat shim.

---

**Consequences.** One representation for dynamic JSON (`JsonNode`) with a single, uniform per-engine extension
seam, and a clean split between the *stored* opaque `JsonElement` (verbatim, deterministic) and the *runtime*
mutable `JsonNode`. The `ExpandoObject` special-cases and their number-fidelity loss disappear. This is the runtime
half of ADR 0035 D2; it unblocks the next unit (delete `PolymorphicObjectConverter` + designer bags → `JsonElement`
+ retire the `_type`/`_items`/`$ref` wire ids), after which a global `JsonNode` converter becomes safe and #551
closes. Read-parity across engines is pinned by a JS+Liquid test: an HTTP payload → `Any` variable → the same field
read identically from both engines, including round-trip through durable `JsonElement` and resume.

**Alternatives considered.** *Keep `ExpandoObject`* — rejected in ADR 0035 D2: best raw `dynamic`/Jint ergonomics,
but requires a serialization converter, is lossy on number kinds, and perpetuates the special-casing this unit
removes. *Materialize as read-only `JsonElement` instead of `JsonNode`* — `JsonElement` is immutable, so workflows
could read but not build/modify dynamic objects, and each engine would still need a bind adapter; `JsonNode` gives
mutability for free at no extra seam cost. *Introduce a new shared `IDynamicValue` abstraction across engines* —
more surface than the problem needs; each engine already has an idiomatic interop point (Jint `IObjectConverter`,
Fluid `MemberAccessStrategy`), so a cross-engine abstraction would be a redundant indirection (D3). *Register a
global `JsonNode` converter now* — collides with the live `PolymorphicObjectConverter` and stack-overflows (D4);
deferred to the converter-deletion unit.

**Follow-up.** #553 implements the site migration and the read-parity test against this contract. The subsequent
unit (ADR 0035 D3/D5 + converter deletion) removes the polymorphic converter, makes designer bags opaque
`JsonElement`, retires the frozen wire ids, and — once the converter is gone — may add a global `JsonNode` converter.
