# Extension points — Elsa3 Activities Import (legacy)

The per-domain catalog (framework §2.22.1). Anchored at `Elsa3.Activities.Design.Import` — the legacy import feature that reconciles Elsa3 activity definitions into the Elsa4 catalog. One contributor interface applies.

---

## Implementable contributor interfaces

### `IActivityCollectionJsonSource` *(Feature contract — `Elsa3.Activities.Design.Import`)*
- **Kind:** Source (opens a stream of activity JSON — pull pattern).
- **Contract defined in:** `Elsa3.Activities.Design.Import` (this feature project). Implementing it requires a reference to this feature.
- **Signature:** `Task<Stream> OpenStream(CancellationToken cancellationToken);`
- **Register:** `services.AddScoped<IActivityCollectionJsonSource, MySource>()`.
- **Consumed by:** the import startup task (this feature), which reads each source's JSON stream and feeds the parsed activity definitions into the Activities.Design reconciliation pipeline.
- **Purpose:** plug in a new source of Elsa3-format activity JSON (e.g. embedded resource, remote URL, filesystem path).

**Known implementations (shipped):**
- None currently in-repo. Implement to point the importer at your Elsa3 activity definition file.

---

## Cross-references

- Activity catalog reconciliation that consumes the imported data: [`Elsa.Activities.Design.Reconciliation/EXTENSION_POINTS.md`](../Elsa.Activities.Design.Reconciliation/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.22.1.
