# Extension points — Elsa 3 Activity Design Import

The one-way Elsa 3 compatibility boundary. Reusable workflows are analyzed as a collection and
applied only as a reviewed dependency-closed mutation; Runtime never consumes these contracts.

---

## Implementable contributor interfaces

### `IReusableActivityImportMaterializer`

- **Kind:** Design mapping strategy.
- **Purpose:** converts a reviewed collection plan into Activity/Workflow Design mutations.
- **Default implementation:** `Elsa3ReusableActivityImportMaterializer` from `Elsa3.Mapping`.
- **Invariant:** exact planned reference rewrites only; recursive composition is never replaced by separate-workflow execution.

### `IReusableActivityImportCommand`

- **Kind:** atomic persistence command.
- **Purpose:** commits one selected dependency closure across Activity Design and Workflow Design.
- **Default implementation:** `GroundworkReusableActivityImportCommand`, registered by `Elsa3ImportActivitiesGroundworkFeature`.
- **Invariant:** all candidate documents are preflighted before one cross-kind commit; identical reapply is a no-op.

### `IReusableActivityCollectionAnalyzer`

- **Kind:** replaceable pure analysis strategy.
- **Default implementation:** `ReusableActivityCollectionAnalyzer`.
- **Output:** deterministic identities, exact rewrites, direct-start wrapper facts, missing/unsupported diagnostics, and complete cycle paths.

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

- Activity catalog reconciliation that consumes the imported data: [`Elsa.Activities.Design.Reconciliation/EXTENSION_POINTS.md`](../../../../Elsa/Activities/Design/Reconciliation/EXTENSION_POINTS.md).
- Repo-wide index: [`EXTENSION_POINTS.md`](../../../../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.22.1.
