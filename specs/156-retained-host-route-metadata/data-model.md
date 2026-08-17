# Data Model: Retained Host Route Ownership and Security Metadata

## Endpoint ownership metadata

- `Kind`: `Host` for root-hosted routes.
- `OwnerId`: stable host identifier (`Elsa.Workbench` or `Elsa.Foundation.Host`).
- `ShellId` / `Generation`: absent for root-hosted routes.

## Security disposition metadata

- `HostCredential`: credential reference and owning host, used by management-key endpoint filters.
- `NamedPolicy`: policy reference and host owner, used by the established console-log default policy.
- `Public`: category and non-empty reason, used by health/readiness routes.

## Retained endpoint manifest entry

The deterministic manifest records normalized route, HTTP methods, owner kind/id, authoring model, security
disposition, content types, response metadata, and source identity. It rejects entries that do not have exactly one
owner, one authoring model, and one security disposition.
