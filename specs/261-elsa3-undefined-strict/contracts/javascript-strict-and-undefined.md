# Contract: Elsa 3 JavaScript — undefined results and strict mode

Extends spec 247's [javascript-conversion.md](../../247-elsa3-js-host-functions/contracts/javascript-conversion.md).
The surface is the output and the diagnostics of `Elsa3ExpressionRewriter` for `JavaScript`. Liquid is unchanged.

## Rewrites (faithful)

Expression mode only. `<e>` is the original expression text after the analyzer's edits.

| Elsa 3 source | Elsa 4 source |
|---|---|
| One expression `e` (optionally `;`) | `(<e>) ?? null`, plus `\n` when a line comment follows, as today |
| Statements ending in expression `e` | `(function () {\n…\nreturn (<e>) ?? null\n})()` |
| Top-level `return e` | `return (<e>) ?? null` inside the function wrapper |
| Top-level `return;` | `return null;` (unchanged, spec 247) |
| `return e` inside a declared function | unchanged |

Script mode (RunJavaScript): no `?? null`. When the last line contains `//`, the output ends with `\n`.

## Refusals

Every refusal uses `VF-IMP-007`. `{…}` is filled in per occurrence. All of them refuse the whole definition, like every
other `VF-IMP-*` refusal.

| Case | Message | Guidance |
|---|---|---|
| Strict parse of the output fails | `JavaScript only works in Elsa 3's sloppy mode; Elsa 4 runs it in strict mode, where it does not parse: {parser description}.` | `Rewrite it in the Elsa 3 workflow without sloppy-mode syntax: no with statement, no legacy octal literal such as 010 (write 0o10 or 8), no duplicate parameter names, no delete of a variable, and no reserved word such as let, static or yield as a name, before import.` |
| Write to an undeclared name | `JavaScript assigns to '{name}', which it never declares. Elsa 3 created a global; Elsa 4 runs in strict mode, where the assignment fails.` | `Declare it with let in the Elsa 3 workflow (let {name} = …;) before import.` |
| `this` outside every non-arrow function | `JavaScript uses 'this' outside a function. In Elsa 3 it was the script engine's global object, which Elsa 4 does not have.` | `Read the value through getVariable or getInput, or a local variable, instead of this, before import.` |
| Write through `arguments` aliasing | `JavaScript changes '{target}' in a function that reads 'arguments'. Elsa 3 kept arguments and parameters linked; Elsa 4 runs in strict mode, where they are not.` | `Copy the value into a local let variable and change that, in the Elsa 3 workflow, before import.` |
| `arguments` outside every non-arrow function | `JavaScript uses 'arguments' outside a function. Elsa 3 failed there; Elsa 4 would return an empty value.` | `Remove the reference to arguments in the Elsa 3 workflow before import.` |

Stable parts: the code and the guidance text. Messages may be reworded without a contract change, as long as they name the
variable or target where the table says so.

## Order of checks

1. Sloppy parse. On failure, today's `VF-IMP-007` parse refusal; nothing else runs.
2. Analyzer: existing refusals, then this contract's write, `this` and `arguments` refusals, all collected. On any issue,
   the rewrite fails with every issue found.
3. Rewrite, including `?? null` and the Script-mode newline.
4. Strict parse of the output. On failure, the strict-mode refusal.
