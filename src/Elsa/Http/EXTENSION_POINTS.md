# Extension points — Http domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Http` — the composition root where `HttpFeature` registers the downloadable-content handler stack. One section applies (contributor interface; no overridable contracts or published events).

---

## Implementable contributor interfaces

### `IDownloadableContentHandler` *(Core — `Elsa.Http.Core`)*
- **Kind:** Contributor (handles a specific content type for download — priority-ordered multi-implementation).
- **Signature:**
  ```
  float Priority { get; }
  bool SupportsContent(object content);
  IEnumerable<Func<ValueTask<Downloadable>>> GetDownloadablesAsync(object content, CancellationToken cancellationToken);
  ```
- **Register:** `services.AddScoped<IDownloadableContentHandler, MyHandler>()`.
- **Consumed by:** `MultiDownloadableContentHandler` (this feature) — resolves all registered `IDownloadableContentHandler` implementations, filters by `SupportsContent(content)`, and uses the first matching handler (ordered by `Priority`). Not event-driven; `MultiDownloadableContentHandler` does the resolution directly.
- **Purpose:** add support for downloading a new content shape (custom object, service, projection type).

**Known implementations (shipped):**
- `Elsa.Http` — `UrlDownloadableContentHandler` *(intra-domain — default; handles URL strings)*
- `Elsa.Http` — `StringDownloadableContentHandler` *(intra-domain)*
- `Elsa.Http` — `StreamDownloadableContentHandler` *(intra-domain)*
- `Elsa.Http` — `HttpFileDownloadableContentHandler` *(intra-domain)*
- `Elsa.Http` — `FormFileDownloadableContentHandler` *(intra-domain)*
- `Elsa.Http` — `DownloadableDownloadableContentHandler` *(intra-domain)*
- `Elsa.Http` — `BinaryDownloadableContentHandler` *(intra-domain)*

---

## Cross-references

- HTTP endpoint behaviour overrides (routes resolver, auth handler, fault handler): [`Elsa.Workflows.Runtime.Http/EXTENSION_POINTS.md`](../Elsa.Workflows.Runtime.Http/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.22.1.
