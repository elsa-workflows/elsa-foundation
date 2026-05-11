# CLAUDE.md — Elsa Foundation (code repo)

This is the **code** repo for the Elsa refactor. The constitution governs what's built here. Meta-work (meetings, follow-ups, agendas, personal todo, the constitution-in-progress) lives in a sibling repo: `../elsa-foundation-project-management/`.

**First action for any new session here: read [`../elsa-foundation-project-management/CLAUDE.md`](../elsa-foundation-project-management/CLAUDE.md).** That file contains the full working conventions, file-naming rules, follow-up-items pattern, and the working loop. This file only covers what's specific to *this* repo.

---

## Repo split

| Repo | Role | What lives here |
|---|---|---|
| `elsa-foundation/` *(this one)* | **Code under refactor + speckit flow** | `src/`, `Elsa.Server.slnx`, `.specify/` (speckit), `.claude/skills/` (speckit skills), feature specs under `specs/` (once created) |
| `../elsa-foundation-project-management/` | **Meta-knowledge** | `CLAUDE.md` (master conventions), `epic1-elsa-refactor-constitution/` with `PERSONAL_TODO.md`, `ARCHITECTURE_v2.md` (working constitution, source-of-truth), `follow-up-items/`, dated meeting artefacts |

When working here, add the meta-repo as an additional dir: `claude --add-dir ../elsa-foundation-project-management`.

---

## Speckit

This repo is initialised for [speckit](https://github.com/github/spec-kit) (v0.8.0). The 15 speckit skills live in `.claude/skills/` and are user-invocable in any session rooted here.

- **Constitution (v1 split landed 2026-05-11):** two layers under `.specify/memory/`:
  - `constitution.md` — **Elsa Workflow Engine Constitution** (speckit canonical slot). Derives from the framework constitution; pins Elsa's root domain, decomposition, and specializations.
  - `constitution-framework.md` — **Modular Software Design Framework Constitution** (generic). Framework-neutral rules that the Elsa constitution cites by reference.
  - Both are v1.0.0 (draft), pending ratification by Joey + Sipke + Frans.
  - `../elsa-foundation-project-management/epic1-elsa-refactor-constitution/ARCHITECTURE_v2.md` is the **archive of the drafting process** — no longer the working source-of-truth.
- **Templates:** `.specify/templates/` — plan, spec, tasks, checklist, constitution.
- **Feature flow:** `speckit-specify` → `speckit-plan` → `speckit-tasks` → `speckit-implement`. Specs land in `specs/NNN-feature-name/`.

---

## Code layout

```
src/
├── Api/                                                # Elsa.Server.csproj — host application
├── <Domain>.Core/                                      # contracts (interfaces, value objects, zero external deps)
├── <Domain>/                                           # umbrella module — only if real shared cross-provider code exists
├── <Domain>.<Provider>/                                # provider-specific implementations (e.g. Elsa.Locking.FileSystem)
└── Elsa.Primitives/                                    # zero-dep domainless building blocks (formerly Elsa.Common, renamed 2026-05-10)
```

Build: `dotnet build Elsa.Server.slnx`. Solution is `.slnx`, not `.sln`.

Per the constitution v1 (2026-05-11):
- Three-layer separation per feature (framework §2.1)
- Domain-language naming, no `Features` segment (framework §2.2)
- Feature inheritance is the only sanctioned cross-feature coupling (framework §2.5)
- Adapters for heavy external deps (framework §2.7), provider/handler interfaces for contributions (framework §2.6, §2.6.1)
- Provider module decomposition: no umbrella unless real shared code; replace meta NuGet packages with specific provider sub-packages (framework §2.20)
- **Elsa-specific:** `Elsa.Workflows.Runtime.*` MUST NOT depend on `Elsa.Workflows.Design.*` (Elsa §E2.2)

---

## When uncertain

1. Read `../elsa-foundation-project-management/CLAUDE.md` first.
2. Check `../elsa-foundation-project-management/epic1-elsa-refactor-constitution/PERSONAL_TODO.md` for current state of Joey's action list.
3. The working constitution lives at `.specify/memory/constitution.md` (Elsa) + `.specify/memory/constitution-framework.md` (framework). Read **both**. `ARCHITECTURE_v2.md` in the meta-repo is the drafting archive only.
4. Auto-memory: project-scoped, persists across sessions. Carries Joey's role, preferences, and architectural constraints.

<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the current plan
<!-- SPECKIT END -->
