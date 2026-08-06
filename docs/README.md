# Elsa Foundation Docs

This docs library is the knowledge lookup layer for `elsa-foundation`. Prefer these docs for orientation and terminology; use the constitution files as quality gates.

## Start points

- [Architecture tour](architecture-tour.md) - concise tour of the architecture and core workflows.
- [Skill catalog](skills/catalog.md) - AI-provider-neutral workflow descriptions.
- [First-user prompt options](reference/first-user-prompts.md) - simple prompts for new architects and engineers entering the workspace.
- [Root/framework glossary](glossary/root.md) - modular-framework terms.
- [Elsa glossary](glossary/elsa.md) - Elsa-specific architecture terms.
- [Reference docs](reference/README.md) - worked examples, case studies, and explanatory walkthroughs extracted from gate documents.
- [Maps index](maps/README.md) - repo maps and generated-map expectations.
- [Reports index](reports/README.md) - inventory, unfinished work, and future verification reports.

## Worked references

- [Docker & compose](docker.md) - production image for Elsa.Workbench plus the PostgreSQL + Elsa.Workbench + Elsa Studio reference stack.
- [Docker Hub quickstart](docker-hub-quickstart.md) - run the prebuilt Docker Hub images with plain `docker run`, supply a custom `shells.json`, and understand how Studio feature toggles persist.
- [Seams and bridges](seams.md) - worked activities/workflows boundary example.
- [Serialization rule](serialization.md) - canonical payload-serialization rule and exceptions.
- [Durable resumption](runtime-durable-resumption.md) - durable storage vs durable resumption, crash windows A/B/C, and the at-least-once/at-most-once asymmetry.
- [Durable timers](runtime-durable-timers.md) - the `Delay` activity, the durable timer store + hosted pump, and the three timer correctness cruxes (delete-on-Dispatched safety, bookmark-expiry, idempotent fire).

## Documentation rule

Define each concept once in the glossary. Other docs may summarize briefly, then link back to the canonical term.
Constitution draft history and open work belong in reports, not in the gate text.
Worked examples and long explanatory walkthroughs belong in reference docs, not in the gate text.
