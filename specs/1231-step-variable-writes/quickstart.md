# Quickstart: verify spec 1231 (S-7)

Build and test only the touched projects (AGENTS.md, *Builds on a shared machine*).

1. **Script writes** — publish a workflow with `count = 0`, `total = 10` and a `RunJavaScript` body
   `variables.total += 5; variables.count++;`. Run it. Expect `total = 15`, `count = 1`, two write-history records with
   source `SessionChange`. Add `throw new Error()` at the end: expect no change.
2. **Read only** — a body that only reads a variable holding `{"id": 9007199254740993, "amount": 1.0}`: the stored
   text and version are unchanged.
3. **C# reference** — an activity with `VariableReference<decimal> Balance` bound to `balance = 100` running
   `Set(Balance, Get(Balance) + 5)`: `balance = 105`. Unbind it: publish refuses.
4. **Lost update** — a step reads `count`, suspends, another branch writes `count`, the step resumes and writes:
   default → applies with a lost-update warning; `[ActivityLostUpdateMode(Fault)]` → faults.
5. **C9** — place each of: a `[BackgroundExecutionSafe, AccessesUndeclaredVariables]` activity, one with a
   `VariableReference<T>` input, one with a `ReadOnlyVariableReference<T>` input, a `RunJavaScript` reading
   `variables.x`, in the background: publication returns `VF-BG-001 (C9)`; the worker re-check returns the same. A
   `RunJavaScript` with no variable access stays eligible.
6. **Converter** — convert an Elsa 3 `RunJavaScript` with `setVariable('Total', 1); setTotal(2);
   getVariable('Order').Status = 'Paid';`: the output uses `variables.Total = …` and `variables.Order.Status = …`, and
   it publishes and runs. The same in an input expression is refused.
7. **Corpus** — re-run the 36 JTest definitions and post conversion, publication and run counts on #3052.
