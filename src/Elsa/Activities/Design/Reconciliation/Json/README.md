# Elsa.Activities.Design.Reconciliation.Json

A reconciliation **source** (§2.6.1 DI-source pattern) that reads a JSON file holding an array of
activity-version reconciliation models and contributes one catalog row per element. It plugs into the
universal reconciliation lifecycle owned by `Elsa.Activities.Design.Reconciliation` without depending
on that feature project — it references only `…Reconciliation.Core` (for the
`IActivityReconciliationSource` contract + model) and `Elsa.Serialization.Core` (for the shared
`IPayloadSerializer`).

## When to use this vs. the CLR source

The CLR source (`…Reconciliation.Clr`) derives everything structurally from runtime assemblies and
deliberately leaves author-facing UI metadata (`DisplayName`, `Description`) null — a runtime assembly
is not allowed to dictate design-time UI. This JSON source is the opposite end: the file is
**author-authored design-time data**, so it may carry whatever UI metadata the author wants
(`displayName`, `category`, `description`, …) with **no restrictions**. It is the right choice for a
verbose, UI-driven definition of an activity-version catalog.

## Single file or an ordered set

Configure **exactly one** of:

- `FilePath` — the one-file shorthand for the common case; or
- `Files` — an ordered set, each tagged with an `Order`. The source reads them in ascending `Order` and
  concatenates the results, so an author can stage dependencies first: e.g. put the plain activities
  authors depend on in `Order: 1` and the `Workflow`-kind activities that reference those versions in
  `Order: 2`, and the earlier file is reconciled into the catalog before the later one.

The feature validates this at registration: it throws if **both** are set, if **neither** is set, or if
`SourceId` is empty. `SourceId` is required (a multi-file source has no single natural path to derive
identity from), and validation lives in the feature — not in a property getter on the source.

## What this feature provides

- **`JsonActivityReconciliationSource`** — `IActivityReconciliationSource` with `SourceKind => "Json"`.
  Its `Read` reads either the single `FilePath` or every `Files` entry in ascending `Order` and returns
  the concatenated models. `SourceId` is the configured `JsonReconciliationOptions.SourceId`.
- **`JsonActivityCatalogReader`** (`IJsonActivityCatalogReader`) — reads the file text and deserializes
  it into `ActivityVersionReconciliationModel[]` via the shared `IPayloadSerializer` (so casing/naming
  conventions match the rest of the system). A missing or malformed file raises a domain-scoped
  `InvalidActivityCatalogJsonException` carrying the file path (§2.23.5) — never a raw IO/JSON fault.
- **`JsonActivityReconciliationFeature`** — `IShellFeature` that registers the options plus the reader
  and the source, each against its contract so an inheriting feature can replace one in isolation
  (§2.5). `ConfigureServices` is `virtual`; the options instance is the only singleton (an
  application-wide static value), everything else is scoped (§2.5.1). It does **not** derive from the
  reconciliation feature.

## How the polymorphic descriptor is handled

The model's `implementationDescriptor` is typed `object`, so the serializer binds it to a raw
`JsonElement` rather than a concrete descriptor type. The JSON source does **not** know about any
descriptor type — it just preserves the element and the `implementationKind` discriminator. The
reconciliation feature's universal `ActivityVersionsReconcilingHandler` then resolves the descriptor
type from `IImplementationDescriptorRegistry` by kind, deserializes the element, and validates that the
descriptor's `Kind` agrees with the entry's `implementationKind`. So a `"Clr"` descriptor in the JSON
deserializes to a `ClrImplementationDescriptor` exactly as if it had come from the CLR scanner, as long
as the owning module registered that kind.

## Registration

Compose the feature alongside `ActivitiesDesignReconciliationFeature`; the universal
`ActivityVersionsReconcilingHandler` discovers the source from DI:

Single file:

```csharp
shell.AddFeature(new JsonActivityReconciliationFeature
{
    Options =
    {
        SourceId = "my-activity-catalog",
        FilePath = "/path/to/activity-catalog.json",
    }
});
```

Ordered set (staged dependencies):

```csharp
shell.AddFeature(new JsonActivityReconciliationFeature
{
    Options =
    {
        SourceId = "my-activity-catalog",
        Files =
        [
            new JsonActivityReconciliationFileOption(Order: 1, FilePath: "/path/to/primitives.json"),
            new JsonActivityReconciliationFileOption(Order: 2, FilePath: "/path/to/workflows.json"),
        ],
    }
});
```

## Example JSON

```json
[
  {
    "version": "1.2.3",
    "activityTypeKey": "Acme.Activities.SendEmail",
    "displayName": "Send Email",
    "category": "Communication",
    "description": "Sends an email to a recipient.",
    "implementationKind": "Clr",
    "implementationDescriptor": {
      "typeInfo": {
        "typeName": "SendEmail",
        "namespace": "Acme.Activities",
        "assemblyName": "Acme.Activities",
        "assemblyVersion": "1.2.3.0"
      }
    },
    "inputs": [],
    "outputs": [],
    "designFacets": []
  }
]
```

## Options

- **`JsonReconciliationOptions.FilePath`** — the single-file shorthand. Mutually exclusive with `Files`.
- **`JsonReconciliationOptions.Files`** — the ordered set of JSON files read for reconciliation models.
  Each is a `JsonActivityReconciliationFileOption(int Order, string FilePath)`; files are read in
  ascending `Order` and their models concatenated. Mutually exclusive with `FilePath`.
- **`JsonReconciliationOptions.SourceId`** — the source identity recorded on every contributed row.
  Required.

The feature rejects an invalid composition (both file options set, neither set, or empty `SourceId`) at
registration with an `InvalidOperationException`.

## Constitutional basis

§2.6.1 (DI-source pattern), §2.5 / §2.5.1 (contract registration, scoped-by-default lifetimes),
§2.23.5 (domain-scoped faults).
