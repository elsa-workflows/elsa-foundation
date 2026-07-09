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

## Replaceable single-implementation contracts

### `IHttpRequestBodyParser` *(Core — `Elsa.Http.Core`)*
- **Kind:** Replacement (single implementation, resolved by `GetService`; override by registering your own before `HttpFeature`).
- **Signature:**
  ```
  JsonElement? Parse(string? contentType, string body);
  ```
- **Register:** `services.TryAddSingleton<IHttpRequestBodyParser, MyParser>()` (register yours first — `HttpFeature` uses `TryAdd`, so an earlier registration wins).
- **Default impl:** `Elsa.Http` — `HttpRequestBodyParser` *(intra-domain)*. Stateless content-type dispatch: `application/json` / `text/json` / any `+json` suffix → parsed `JsonElement` (malformed → `null`, never throws); `text/*` → string element; unknown/absent content type or empty body → `null`. A `charset` (or other) parameter is tolerated.
- **Consumed by:** the request middleware (`Elsa.Activities.Http`) to populate `HttpRequestModel.ParsedContent` (spec 089 sub-unit C, research D6).
- **Purpose:** the request-side counterpart to the response-side `IHttpContentParser` set. It shares that set's content-type-dispatch *intent* but not its implementations: the response parsers are `HttpResponseMessage`/`Stream`-shaped and return `object` via a caller `ReturnType` + converter/serializer pipeline, whereas the inbound body is already a string and the only legal output is wire-safe `JsonElement` (ADR 0035/0036). The response-side path is untouched.

---

## HTTP endpoint behaviour contracts *(Core — `Elsa.Http.Core`)*

The `IHttpEndpointAuthorizationHandler` and `IHttpEndpointFaultHandler` contracts (with `AuthorizeHttpEndpointContext`, `HttpEndpointFaultContext`, and `HttpBadRequestException`) live in `Elsa.Http.Core` (spec 089 sub-unit C) so the request middleware in `Elsa.Activities.Http` and the default handlers in `Elsa.Workflows.Runtime.Http` share them without a cross-module edge — same placement logic as the `HttpEndpointRouting` routing vocabulary. Default implementations and override points are catalogued in [`Elsa.Workflows.Runtime.Http/EXTENSION_POINTS.md`](../Elsa.Workflows.Runtime.Http/EXTENSION_POINTS.md).

---

## Cross-references

- HTTP endpoint behaviour overrides (routes resolver, auth handler, fault handler): [`Elsa.Workflows.Runtime.Http/EXTENSION_POINTS.md`](../Elsa.Workflows.Runtime.Http/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.22.1.
