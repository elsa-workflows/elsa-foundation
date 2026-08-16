# Data Model: Workflow-authored Dynamic HTTP Publication

## Dynamic route metadata

- `HttpRouteOwnershipMetadata`: `OwnerKind`, `OwnerId`, `ShellId`, and non-negative `Generation`; dynamic routes require all four.
- `HttpRouteSecurityDispositionMetadata`: exactly one `Kind` with public category/reason, permission values, host credential, or named policies as appropriate.
- `HttpRouteData`: route template, optional method set, route values/tokens, compiled matcher, and immutable metadata snapshot.

## Route generation

- `HttpRouteTableSnapshot`: monotonically increasing generation and an immutable ordered route collection.
- `HttpRouteTableSnapshotLease`: request-owned lease over one snapshot; exposes a drain task completed only after retirement and lease release.

## Manifest validation

- Canonical route key: slash-normalized route with parameter names erased but constraints/defaults preserved.
- Method overlap: intersection of explicit methods, or overlap with the compatibility wildcard when one side has no methods.
- Conflict diagnostic: canonical route, overlapping method, and both owner identities.
- Static composition input: `IHttpRouteManifestProvider`, registered by the ASP.NET adapter before CShells builds a shell;
  its entries are validated but are not copied into the workflow route table.
