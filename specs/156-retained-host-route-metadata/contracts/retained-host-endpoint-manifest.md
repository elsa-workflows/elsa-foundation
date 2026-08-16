# Retained Host Endpoint Manifest Contract

Every retained root-host endpoint appears as one manifest entry with:

| Field | Requirement |
|---|---|
| `ownerKind` | `Host` |
| `owner` | Stable host owner id |
| `authoringModel` | `Minimal API` |
| `securityDisposition` | Exactly one typed `HostCredential`, `NamedPolicy`, or `Public` record |
| `route` / `methods` | Normalized route and one or more HTTP methods (SignalR entries use the framework endpoint methods) |

The manifest validator rejects missing/duplicate ownership, authoring, or security metadata; public dispositions
must carry anonymous metadata, protected dispositions must carry standard authorization metadata or the explicit
custom host-credential enforcement marker.
