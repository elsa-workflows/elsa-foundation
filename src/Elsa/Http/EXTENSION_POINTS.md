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
- **Consumed by:** the `HttpEndpoint` activity (`Elsa.Activities.Http`) to derive its `ParsedContent` output from the raw `HttpRequestModel.Body` + the `Content-Type` header at execution time (spec 089 sub-unit C, research D6). Since #592 item 9 the parsed value is NOT persisted on the wire model — it is derived at the activity, so the durable stimulus payload carries the body once rather than the body plus a re-encoded copy. Null semantics (item 16): empty/unparseable → `null` (parser returns `null`), while an explicit JSON `null` body returns a present `JsonElement` of kind `Null`, so `object?` consumers distinguish "no content" from "explicit null".
- **Purpose:** the request-side counterpart to the response-side `IHttpContentParser` set. It shares that set's content-type-dispatch *intent* but not its implementations: the response parsers are `HttpResponseMessage`/`Stream`-shaped and return `object` via a caller `ReturnType` + converter/serializer pipeline, whereas the inbound body is already a string and the only legal output is wire-safe `JsonElement` (ADR 0035/0036). The response-side path is untouched.

---

## Dynamic workflow route publication *(Core + `Elsa.Http`)*

`HttpRouteData` is the compatibility carrier for workflow-authored routes. Published entries include a normalized
method set (an empty set retains the pre-metadata wildcard behavior) and an immutable `HttpRouteOwnershipMetadata`
plus exactly one `HttpRouteSecurityDispositionMetadata`. The production `RouteTable` supplies the
`DynamicShell` owner (`Elsa.Http`, shell discriminator, and monotonically increasing generation) and a public
compatibility disposition for legacy callers that provide no metadata. `HttpRouteManifestValidator` can also
validate a complete host/module/dynamic manifest, canonicalizing parameter names and rejecting overlapping methods
with both owner identities in the exception.

`HttpFeature` registers the shell-scoped `IHttpRouteManifestProvider` after CShells has composed root endpoint
sources into the activated shell. The adapter projects root and current-shell `EndpointDataSource` entries, filters
other shell generations, and converts shared ownership/security metadata into this lower-layer contract. Its
host/module-owned `HttpRouteData` entries are a validation-only composition manifest: workflow refreshes merge the
provider snapshot with the enriched candidate, reject same-method collisions before publication, and leave the
previous generation untouched on failure. Shells that do not expose endpoint sources retain the empty-provider
compatibility path.

Workflow routes stay endpoint-relative for middleware matching. Before manifest validation, `RouteTable` resolves each
candidate beneath `HttpRoutePublicationOptions.BasePath`, configured from the Activities HTTP feature's request base
path, so absolute host/module endpoints and authored routes share one collision coordinate system. A disabled base path
has no published dynamic address and therefore cannot conflict with a live host endpoint.

Refresh constructs, compiles, validates, and enriches a complete candidate before one shell-state publication. A
rejected candidate leaves the previous snapshot intact. One child-provider singleton owns the current generation and
synchronization gate; the public `IMemoryCache` constructor parameter remains source-compatible but is not authoritative,
so cache eviction cannot reset routes and no process-global shell-key dictionary retains shells.
`IRouteTableSnapshotProvider` is an additive seam: requests lease one immutable generation through matching and dispatch,
and the retired generation reports drained only after the lease is released. Existing `IRouteTable` implementations
remain valid and use the enumerable fallback.

The authoritative generation never exposes its mutable legacy `HttpRouteData` carriers. Snapshot inspection produces
defensive route/dictionary copies, while the production lease uses the shared lower-layer route resolver directly over
its private ordered generation. This keeps inspection mutation isolated without deep-cloning the table per request.
Incremental publication accepts same-template entries with disjoint explicit methods; overlapping methods and wildcard
claims still reject, and the historical methodless exact-duplicate exception remains compatible.

## HTTP endpoint behaviour contracts *(Core — `Elsa.Http.Core`)*

The `IHttpEndpointAuthorizationHandler` and `IHttpEndpointFaultHandler` contracts (with `AuthorizeHttpEndpointContext`, `HttpEndpointFaultContext`, and `HttpBadRequestException`) live in `Elsa.Http.Core` (spec 089 sub-unit C) so the request middleware in `Elsa.Activities.Http` and the default handlers in `Elsa.Workflows.Runtime.Http` share them without a cross-module edge — same placement logic as the `HttpEndpointRouting` routing vocabulary. Default implementations and override points are catalogued in [`Elsa.Workflows.Runtime.Http/EXTENSION_POINTS.md`](../Elsa.Workflows.Runtime.Http/EXTENSION_POINTS.md).

---

## Cross-references

- HTTP endpoint behaviour overrides (routes resolver, auth handler, fault handler): [`Elsa.Workflows.Runtime.Http/EXTENSION_POINTS.md`](../Elsa.Workflows.Runtime.Http/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.22.1.
