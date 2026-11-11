# Contract: Elsa 3 converter — variable reads

**Spec**: [../spec.md](../spec.md) · Provisional (initiative #3052). Reconciles spec 247 with the D-7 read model:
static pinning for literal names plus a run-time fallback for computed names.

## Emission

| Elsa 3 source | Elsa 4 output | Served by |
|---|---|---|
| `Variables.X` (JS) | `variables.X` | publish pin |
| `getVariable("X")` | `variables.X` | publish pin |
| generated `get<Name>()` | `variables.<declaredName>` (via `Elsa3JavaScriptNames`, decision 6 naming rule) | publish pin |
| `getVariable(expr)` (non-literal) | `getVariable(expr)` | run-time fallback |
| `Variables[expr]` (non-literal) | `variables[expr]` | run-time fallback |
| `{{ Variables.X }}` (Liquid) | `{{ variables.X }}` | publish pin |
| `getInput("X")`, activity results, instance metadata | `args.X` (unchanged) | publish pin |

Spelled as the variable is declared. Variable reads are **not** lowered to `args` (this removes the #3296 collision for
variable reads). Computed reads are converted faithfully, not refused.

## Refusals (faithful-or-refuse)

1. **#3296 — author `args` collision.** When a script declares its own `args` (parameter or local) in a scope that also
   contains lowered `args` reads (inputs / activity results / metadata), refuse with guidance (e.g. rename the Elsa 3
   local), rather than emitting reads that silently bind to the author's `args`.
2. **Resolution differs.** Refuse a literal variable read when Elsa 4's nearest-scope resolution would pick a different
   declaration than Elsa 3's resolution.

## Import warnings

1. **Shadowed candidate.** A computed read whose candidate names include a name declared in more than one visible scope
   gets an import warning: the Elsa 4 run-time lookup resolves nearest-scope-first.
2. **Transient-in-Elsa-4.** A converted read of a variable that is **transient** in the Elsa 4 workflow is flagged at
   import with its own diagnostic, so the failure points at the Elsa 3 source rather than at publish.

## Fixes

1. **#3287 — arguments type.** `Elsa3ValueFlowLowerer.LowerScript` gives the `RunJavaScript` `arguments` expression its
   real result type from the input contract instead of the hard-coded `Object`, so a converted script that reads a
   variable or input is not refused at publish for a type mismatch. Full fix pairs with **#3167** (S-0: `JsonElement`
   alias inference).
2. **#3010 — no IIFE wrap.** Statement bodies are emitted as-is on the `RunJavaScript` body path (no
   `(function(){…})()`). Confirm the Expression-mode completion-value wrap is a separate concern (outcomes / S-3), out
   of scope here.

## Parser parity

The converter and publish parse the same source the runtime parser accepts (the trailing-line-comment bug is the #3171
side track on `main`, consumed here).
