# Refactoring Methodology

Supports framework §2.18. Moved verbatim from framework constitution v5.0.1 (#2578). The constitution keeps the three checks that are gates; this page keeps the method.

The framework is built to be applied to existing modular monoliths, not only to greenfield projects. The recommended approach is a three-step methodology — *sequential by description, inductive in practice.* §2.18.0 frames the shape; §2.18.1–§2.18.3 are the steps; §2.18.4 closes the loop on refactor-cost discipline.

## §2.18.0 Shape of the methodology

The steps are a **checklist of perspectives to apply, not a sequence to march through.** Step 1 and Step 2 inform each other across repeated passes — domain hypotheses get tested against the actual code; the actual code suggests revised domain hypotheses. Step 3 closes the loop.

**Domains are semantic, not testable.** A domain is a logical, semantic group that describes a process or area of the application. There is no mechanical test for whether something *is* a domain. A domain may be an entire app, a group of features, a specialised single feature, or a group of domains working together towards a goal — all of these are legitimate. The judgement is human and based on intent, not falsifiable by a rule. The methodology is a way to *discover* and *organise* domains in practice; it is not an algorithm for *deciding* whether something qualifies.

> *How it actually happens.* Domain boundaries are typically discovered by clustering existing implementations and asking *"what purpose are these features collectively serving?"* The shared interfaces extracted from implementations are what populate the domain's `.Core`. The methodology below is the explicit framing of what already happens implicitly when a thoughtful architect reads a codebase.

## §2.18.1 Step 1 — Identify the domains

Map the application into coherent processes, products, or services. Each domain is a *mental model first* — described by its purpose in human terms — and only then realised in code.

**Test.** A domain's purpose MUST be expressible in one verb-led sentence. If the description requires "and" between two unrelated capabilities, the candidate is two domains.

## §2.18.2 Step 2 — Extract the implementations

Within each domain, separate contracts from implementations:

- **Contracts that other domains or features may want to consume** → move to the domain's `.Core` library. They are the consumable surface.
- **Contracts truly internal to one feature implementation** (no other domain or feature consumes them) → keep them inside the implementation library. They are not part of the consumable surface.

**Test.** Would another domain or feature plausibly need to reference this contract? Yes → `.Core`. No → impl-private. Wrong calls get reversed during Step 3 when reuse evidence appears.

## §2.18.3 Step 3 — Resolve cross-domain reuse

When two or more domains contain similar features (e.g., both perform serialization, both schedule background work), promote the feature: place its `.Core` library once and reference it from implementations in each consuming domain. Each domain configures the feature differently to its own needs — the `.Core` is the shared contract; the configuration is per-domain.

**Inductive feedback.** Step 3 frequently produces evidence that revises Step 1 (a feature group originally identified as one domain turns out to be two; a candidate domain turns out to be a feature shared by two larger domains). Treat such revisions as a healthy signal of the inductive loop closing, not as a failure of the prior pass.

## §2.18.4 Refactor-cost discipline

The constitution's refactor-cost test (§2.16) keeps Step-3 revisions affordable: where NuGet identity is preserved, consumers are insulated from the restructuring.

**All refactor work performed under this methodology is governed by the golden rule of refactoring (§2.21.1).** Tests that still describe valid behaviour MUST keep passing; a test whose code was removed, or whose behaviour is proven elsewhere, MAY be rewritten or deleted, with the PR naming which applies.
