# Clean-slate execution kernel: design record

Status: a proposed design, not approved. No product code exists for it. It supersedes the journal-first approach proposed as ADR 0098 in open PR #4176 (not merged). Program: #4229.

| File | What |
|---|---|
| [design-sketch-draft-5.md](design-sketch-draft-5.md) | The design sketch, draft 5 (12 November 2026), with every owner decision taken so far: the three layers, partitions only, the kernel defaults, the deployment profiles, turn/job/pass, the container semantics, standalone jobs, the seven design-now questions, the release 1 scope and the architecture documentation plan |
| [log-profile-contract-sketch.md](log-profile-contract-sketch.md) | Round 5 H paper: four proposed contracts, the accepted recorded-send boundary and virtual-partition journal checks. Owner review pending; Log remains planned, not designed; no API split or provider implementation |
| [walkthrough.html](walkthrough.html) | The interactive walkthrough of the same design, built from the parts below; open it in a browser |
| [walkthrough-src/](walkthrough-src/) | The walkthrough in five parts: the files to edit |
| [kernel-in-motion.html](kernel-in-motion.html) | The kernel in motion: one order workflow animated through every part, the three persistence modes side by side, three faults, the fence lab and a visual glossary. A single self-contained file; also published at https://crystal-pellet-h5m3.here.now/ |
| [kernel-demo.html](kernel-demo.html) | The kernel demo: six slides for a mixed audience (arrow keys; N for notes). The three layers, one request animated, the persistence modes, and database commands for Elsa 3, Elsa 4 today and the kernel prototype on the same three workflows. Published at https://quartz-island-6myr.here.now/ |
| [evidence/elsa3-cost/](evidence/elsa3-cost/RECEIPT.md) | The Elsa 3.10.1 command-count measurement behind the demo: receipt, harness and per-case SQL logs |

The walkthrough is also published at https://vowful-mustard-pd8t.here.now/. To rebuild it after editing a part:

```bash
cd docs/reports/clean-slate-kernel
cat walkthrough-src/01-head.html walkthrough-src/02-body1.html walkthrough-src/03-body2.html walkthrough-src/04-body3.html walkthrough-src/05-script.html > walkthrough.html
```

The standalone model that tested the design (`prototypes/kernel-model/`) is on main (landed through PRs #4250 to #4260); the commit hashes its documents cite are kept at the tag `prototype/kernel-model-history`.
