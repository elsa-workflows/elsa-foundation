# Quickstart: check explicit outcomes by hand

1. In Studio, add a RunJavaScript with possible outcomes `Approved`, `Rejected`. The node shows `Done`, `Approved` and
   `Rejected`, and no `Unmatched`.
2. Connect `Approved` and `Done` to two WriteLines. Script: select `Approved`, return `"Rejected"`. Publish and run:
   only the `Approved` WriteLine runs, and the result is `"Rejected"`.
3. Change the script to select nothing: the `Done` WriteLine runs.
4. Change it to select the literal `Aproved`: publication reports `VF-AUTH-010`.
5. Select a name read from a variable set to `Aproved`: the step faults with `VF-ACT-006`.
6. Import an Elsa 3 workflow with RunJavaScript → `Done` → WriteLine: the WriteLine runs (#3295).
