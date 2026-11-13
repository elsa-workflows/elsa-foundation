# Clean-slate execution kernel: design record

Status: a proposed design, not approved. No product code exists for it. It supersedes the journal-first approach proposed as ADR 0098 in open PR #4176 (not merged). Program: #4229.

| File | What |
|---|---|
| [design-sketch-draft-5.md](design-sketch-draft-5.md) | The design sketch, draft 5 (12 November 2026), with every owner decision taken so far: the three layers, partitions only, the kernel defaults, the deployment profiles, turn/job/pass, the container semantics, standalone jobs, the seven design-now questions, the release 1 scope and the architecture documentation plan |
| [walkthrough.html](walkthrough.html) | The interactive walkthrough of the same design, built from the parts below; open it in a browser |
| [walkthrough-src/](walkthrough-src/) | The walkthrough in five parts: the files to edit |

The walkthrough is also published at https://vowful-mustard-pd8t.here.now/. To rebuild it after editing a part:

```bash
cd docs/reports/clean-slate-kernel
cat walkthrough-src/01-head.html walkthrough-src/02-body1.html walkthrough-src/03-body2.html walkthrough-src/04-body3.html walkthrough-src/05-script.html > walkthrough.html
```

The standalone model that tested the design (`prototypes/kernel-model/`) is on main (landed through PRs #4250 to #4260); the commit hashes its documents cite are kept at the tag `prototype/kernel-model-history`.
