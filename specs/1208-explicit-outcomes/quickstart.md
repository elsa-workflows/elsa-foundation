# Quickstart: check explicit outcomes by hand

1. In Studio, add a RunJavaScript with possible outcomes `Approved`, `Rejected`. The node shows `Done`, `Approved` and
   `Rejected`, and no `Unmatched`.
2. Connect `Approved` and `Done` to two WriteLines. Script: `return complete("Rejected", "Approved");`. Publish and
   run: only the `Approved` WriteLine runs, and the result is `"Rejected"`.
3. Change the script to `return "Approved";`: publication warns `VF-AUTH-012`, and the `Done` WriteLine runs.
4. Change it to `return complete(null, "Aproved");`: publication reports `VF-AUTH-010`.
5. Return `complete(null, variables.status)` with the variable set to `Aproved`: the step faults with `VF-ACT-006`.
6. Import an Elsa 3 workflow with RunJavaScript → `Done` → WriteLine: the WriteLine runs (#3295).
