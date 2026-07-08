# Serialization Unifies On The Alias Registry And Retires Open-Object Polymorphism

Status: accepted (2026-07-08; amends constitution §E2.9 and §E6 — see D3/D5; implementation sequenced across #553 (done) and the converter-removal unit).

**Date:** 2026-07-07

**Context:** [#552](https://github.com/elsa-workflows/elsa-foundation/issues/552) (investigation). Relates to
[#549](https://github.com/elsa-workflows/elsa-foundation/pull/549) / `specs/086` (deterministic serializer),
`specs/081` (typed argument model), [ADR 0034](0034-workflow-definitions-reconcile-from-and-export-to-git.md)
(git content-hash). Implementation sequenced across
[#553](https://github.com/elsa-workflows/elsa-foundation/issues/553) and
[#551](https://github.com/elsa-workflows/elsa-foundation/issues/551).

---

**The problem this fixes.** Elsa's shared `IPayloadSerializer` carries **two competing mechanisms** for
expressing .NET type identity inside JSON, wired as global defaults over everything it touches:

1. **Alias registry** — `TypeJsonConverter` + `IWellKnownTypeRegistry`. Type identity is a short *registered
   alias*, resolved registry-only, with **no `Type.GetType`** fallback (`specs/081` FR-004a). This is the
   go-forward model and is already what runtime durable values use.
2. **Open-object polymorphism** — `PolymorphicObjectConverter` / `PolymorphicObjectConverterFactory` /
   `PolymorphicDictionaryConverter`. Type identity is a *simple-assembly-qualified name* embedded per value as
   `_type`, plus collection wrapping (`_items`/`$values`), JSON islands (`_island`), and dead reference ids
   (`$ref`/`$id`). Reconstruction falls back to `Type.GetType(typeName)`. Inherited from Elsa 3.

Mechanism (2) is the most complex code in serialization, is a deserialization-gadget surface
(`PolymorphicObjectConverter.cs:307`), embeds assembly-version-bearing type names that make a content hash
brittle (an AQN like `…, Version=10.0.0.0, …` leaks into the bytes), and hosts a latent stack overflow
(#551). The investigation behind #552 established that it is now **load-bearing in only a small, identifiable
set of places**, not the general case it appears to serve.

**What the investigation found.**

- **Runtime state already left it behind.** `DurableValueState.InlineValue` is a `JsonElement?` plus a
  separate alias-based `RuntimeValueTypeDescriptor` — a *typed envelope*, not an `object` graph with embedded
  `_type`. Runtime input materialization already deserializes against a *resolved* type
  (`RuntimeActivityInputMaterializer`), not through open polymorphism. For core runtime state the `_type`
  path is essentially vestigial.
- **Design-time `StateSource` depends on it only narrowly.** `WorkflowDefinitionState` is strongly typed
  except for the `IDictionary<string, object>? PropertyInfo` / `UISpecifications` designer bags on
  `InputDefinition`/`OutputDefinition` — the last thing routing the canonical StateSource through the
  polymorphic path. Those bags are pass-through UI metadata (the one backend read *generates* them from
  `input.Type`), not CLR-typed values.
- **Genuinely dynamic JSON is the only real need** — a workflow variable holding a schemaless payload (e.g.
  from an HTTP Endpoint activity) that is read by JavaScript, Liquid, Python, C#, and any module-provided
  language. Crucially, **that need never required `_type`**: there is no CLR type to preserve; it is just
  JSON.

The polymorphic converter conflated two needs. One is gone; the other never needed it.

---

## Decisions

### D1 — One type story: identity is a registry alias

All persisted type identity is expressed as an `IWellKnownTypeRegistry` **alias** and resolved registry-only.
Assembly-qualified names as a persisted discriminator are removed. Where open handling survives (D2) it emits
the alias, never an AQN, and never calls `Type.GetType`.

*Consequences.* Type identity becomes refactor-proof (renaming a CLR type or bumping an assembly version no
longer changes the wire form or a content hash — directly helps ADR 0034's hash tripwire). The
deserialization-gadget surface disappears. Types that must round-trip through an open payload must be
*registered*; the failure mode for an unregistered type is decided per context in D2 (fail-fast for the typed
model; degrade-to-opaque for the dynamic `Any` kind).

### D2 — There is no open-object CLR polymorphism; there is an `Any` (dynamic JSON) value kind

The typed-argument model already carries CLR type out-of-band via the declared alias, so per-value `_type`
tagging is redundant. Serialization collapses to a clean dichotomy:

- **Declared concrete alias** → typed round-trip through plain System.Text.Json. No polymorphism.
- **Declared `Any` alias** (the existing `WellKnownTypeNames.Any`) → **stored as opaque JSON**, no
  discriminator, materialized to a dynamic .NET type at runtime.

Type fidelity therefore *requires declaration*: an undeclared value set to a domain POCO round-trips as JSON,
not as that POCO. This is the deliberate trade and matches `specs/081`.

**Direction for the runtime representation (implementation sequenced separately — #553).** The canonical
in-memory dynamic type is **`JsonNode`** (mutable, STJ-native, lossless, no custom serialization converter);
`ExpandoObject` is retired as the `Any` representation. Each expression module owns a **per-engine adapter**
from `JsonNode` to its value model — the extension seam (Jint already bridges JSON via `JsonElementConverter`;
Liquid/Fluid has STJ support; new languages add one adapter, touching nothing in storage). This ADR fixes the
*direction*; the ExpandoObject→JsonNode refactor and its dedicated expressions ADR are #553, so the
serialization-layer win is not blocked by the runtime change.

### D3 — Designer bags become opaque JSON *(amends constitution §E2.9)*

`InputDefinition`/`OutputDefinition.PropertyInfo` and `UISpecifications` change from
`IDictionary<string, object>?` to `JsonElement?` (opaque, Studio-authored UI metadata). This removes open
polymorphism from `StateSource` entirely.

*Extended beyond `StateSource` (spec 088):* the same move applies to the designer **layout** document —
`DesignMetadataRecord.AdditionalProperties` (the opaque, Studio-authored per-node layout bag, carried on
`WorkflowDefinitionVersionLayout`/`DraftLayout` and surfaced via `IWorkflowDefinitionLayout`) changes from
`Dictionary<string, object?>?` to `JsonElement?`, kept verbatim end-to-end. Layout is a separate document from
`StateSource`, so it was the last open-object-polymorphism holdout in serialized design content; it is now
opaque too, satisfying the rider's "never round-tripped through a `Dictionary<string, object>`" invariant.

*Rider — opaque JSON stays verbatim, and that is already deterministic.* An initial worry (#555, since
withdrawn) was that the deterministic serializer sorts dictionary keys and object members but not the contents
*inside* an embedded `JsonElement`, so D3's opaque bags (and the existing `ActivityNode.Structure.Payload`)
would leak key-order noise into the content hash. That worry was wrong: a `JsonElement` re-emits **verbatim in
parse order** with no hashing, so a given stored blob is already byte-stable across processes — the
cross-process nondeterminism the serializer fixes is specific to hash-seeded `Dictionary` enumeration and
unfixed reflection order, neither of which applies to a stored `JsonElement`. Reordering an opaque blob would
only make two *differently-authored* blobs hash equal — asserting a semantic equality Elsa does not own over
opaque data, and mutating the author's bytes. So the rule is: **opaque JSON is stored as `JsonElement` and
never rewritten.** The only invariant to preserve is that an opaque bag is kept as `JsonElement` end-to-end
and never round-tripped through a `Dictionary<string, object>` (which *would* reintroduce hash-seeded order);
D3's move from `IDictionary<string, object>?` to `JsonElement?` is exactly what secures that.

Because this changes authored-content modeling (§E2.9), it is an architecture-meeting decision, folded into
this ADR.

### D4 — Remove the `Type.GetType` fallback immediately (independent hardening)

`PolymorphicObjectConverter.cs:307`'s `Type.GetType(typeName)` last resort is a reachable gadget path. It is
independent of the larger refactor and ships **first**, as a small hardening PR: registered types still
resolve via the alias registry; unregistered AQNs simply stop resolving, which is the intent. Closes the
security surface ahead of the sequenced work.

### D5 — Retire the frozen wire identifiers *(amends constitution §E6)*

Under D2, `_type` / `_items` / `$ref` / `$values` / `$id` disappear (nothing wraps), and the System.Text.Json
`_island` handler becomes redundant (`JsonNode` serializes natively); the Newtonsoft `_island` handler
survives only as long as that opt-in module exists. These identifiers are **frozen by §E6** and pinned by
`PolymorphicObjectConverterReferenceTests`, so retiring them is a **constitutional amendment**, not a silent
change — folded into this ADR for ratification.

### D6 — #551 is resolved by deletion, not patched

The #551 stack overflow lives in the exact read branch D2 removes. It is fixed *when* the converter is
deleted (with/after #553), not before — pulling the converter early would break dynamic values that still
flow through it at runtime. If #553 proves distant, an optional interim guard is to make that branch throw a
clear error instead of recursing.

### D7 — Sequencing and tests (unreleased → no shims)

Ship-now, independent: **D4** (Type.GetType removal). Coupled sequence: **#553 (JsonNode runtime) → converter
removal + D3 (designer bags opaque) + D5 (retire wire ids)**. Unreleased software means no data migration —
frozen `StateSource` is never re-serialized (`specs/086` constraint). `PolymorphicObjectConverterReferenceTests`
is *retired* (not re-baselined) when the converter is deleted; the `specs/086` golden digest is deliberately
re-based when designer-bag handling changes. (Embedded opaque JSON needs no canonicalization — see the D3
rider — so no embedded-JSON fixture is required.)

---

**Consequences.** One type mechanism (the alias registry) instead of two. The most complex, riskiest, and
least safe converter in serialization is deleted rather than maintained. Content hashing (ADR 0034) becomes
sound: no AQN/version noise, and no embedded-JSON key-order noise (opaque JSON is stored verbatim as a
byte-stable `JsonElement`, D3 rider). Dynamic JSON gets a single canonical representation (`JsonNode`) with a
clean per-language extension seam. The cost is a real
runtime/expressions refactor (#553) and two constitutional amendments (§E2.9, §E6).

**Alternatives considered.** *Keep the converter but only harden it* (do D4, stop) — leaves two type systems,
the #551 class of bugs, and the AQN hash brittleness. *Quarantine the converter to a dynamic-only options
variant rather than deleting it* — still maintains the `_type` machinery and its wire contract for a need
(dynamic JSON) that does not actually require type tags; strictly more code than D2 for no benefit.
*`ExpandoObject` as the canonical dynamic type* — best raw `dynamic`/Jint ergonomics and least per-engine glue
today, but requires a serialization converter, is lossy on number kinds, and perpetuates the ExpandoObject
special-casing this ADR removes; rejected in favour of lossless STJ-native `JsonNode`.

**Follow-up.** D4 → standalone hardening PR (#558). #553 → JsonNode adoption + expressions ADR. Converter
removal + D3 + D5 → lands after #553; closes #551 and #552. §E2.9 and §E6 amendments ratified with this ADR.
(#555 was withdrawn — opaque JSON needs no canonicalization; see the D3 rider.)
