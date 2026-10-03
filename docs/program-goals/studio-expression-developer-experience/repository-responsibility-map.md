# Repository responsibility map: Studio expression developer experience

| Concern | Owning repository/surface | Boundary |
|---|---|---|
| Product program, shared queue and evidence index | `elsa-foundation` Program #2310 and Project 53 | Coordinates work; does not redefine Studio glossary or duplicate implementation specs. |
| Workflow-location scope, permissions, policy and persisted draft context | `elsa-foundation` Workflows Design | Supplies bounded metadata only; no language-specific UI. |
| JavaScript runtime grammar, globals, provider metadata and authoritative diagnostics | `elsa-foundation` Expressions JavaScript | No browser/Node APIs unless runtime-supported; no evaluation for assistance. |
| Liquid runtime profile, filters/tags, provider metadata and authoritative diagnostics | `elsa-foundation` Expressions Liquid | Binding-pure runtime metadata is shared; no live Fluid context or values in authoring. |
| Consequential Test Run/publication validation | `elsa-foundation` Workflows Design/Publishing | Existing spec 143 gates remain authoritative. |
| Syntax picker, activity-property context and tooling transport | `elsa-foundation-studio` Workflows | Language-neutral; does not invent runtime symbols. |
| Shared editor sessions, themes, previews, help panels and keyboard/accessibility behavior | `elsa-foundation-studio` CodeEditor | CodeMirror remains internal; public contracts stay engine-neutral. |
| JavaScript/Liquid local parsing and language projection | `elsa-foundation-studio` expression-editor modules | Each module projects the shared context into its own syntax. |
| Real actor browser acceptance | Matching Studio and Foundation reviewed heads | Must traverse module discovery, persisted workflow, endpoints and providers; fixtures alone are insufficient. |

The canonical domain terms live in Studio `CONTEXT.md`; Studio ADRs 0002/0006 and specs 094/143 govern the implementation boundaries.
