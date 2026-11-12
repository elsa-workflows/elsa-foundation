# Clean-slate execution kernel: design record

Status: a proposed design, not approved. No product code exists for it. It supersedes the journal-first approach of ADR 0098 (#4176). Program: #4229.

| File | What |
|---|---|
| [design-sketch-draft-5.md](design-sketch-draft-5.md) | The design sketch, draft 5 (12 November 2026), with every owner decision taken so far: the three layers, partitions only, the kernel defaults, the deployment profiles, turn/job/pass, the container semantics, standalone jobs, the six design-now questions, the release 1 scope and the architecture documentation plan |
| [walkthrough.html](walkthrough.html) | The interactive walkthrough of the same design, built from the parts below; open it in a browser |
| [walkthrough-src/](walkthrough-src/) | The walkthrough in five parts: the files to edit |

The walkthrough is also published at https://vowful-mustard-pd8t.here.now/. To rebuild it after editing a part:

```bash
cat walkthrough-src/01-head.html walkthrough-src/02-body1.html walkthrough-src/03-body2.html walkthrough-src/04-body3.html walkthrough-src/05-script.html > walkthrough.html
```

The standalone model that tested the design is `prototypes/kernel-model/`.
