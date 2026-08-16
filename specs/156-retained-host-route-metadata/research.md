# Research: Retained Host Route Ownership and Security Metadata

## Decision: Reuse the shared endpoint metadata and manifest seams

`Elsa.Api.AspNetCore` already provides typed ownership, security disposition, and authoring metadata;
`EndpointManifestBuilder` already enforces one owner/disposition and standard authorization consistency.
Host routes should use those seams rather than introduce host-local records.

## Decision: Represent custom management-key filters explicitly

Management-key routes intentionally use endpoint filters because the key is not an ASP.NET Core authentication
scheme. A typed host-credential enforcement marker lets the manifest recognize the existing filter boundary without
adding a fake authentication scheme or changing request behavior.

## Decision: Protect CShells management with the existing server-side key

`CShells.Management.Api` applies no authorization. Workbench must chain the existing management-key validation
filter and publish a host-credential disposition. This preserves ADR 0037: the key stays server-side and is not
converted into a Foundation Identity permission.

## Decision: Use default policy metadata for console-log streaming

Console-log HTTP and SignalR routes are mapped under a group with `RequireAuthorization()`. The manifest records
that established default-policy boundary as a named host policy owned by `Elsa.Workbench`; the group metadata is
inherited by the external minimal-API and SignalR mappers.

## Alternatives rejected

- Adding `AuthorizeAttribute` to management-key routes: would require a new authentication scheme and could change
  direct server-to-server key behavior.
- Adding a Foundation user permission to CShells management: contradicts ADR 0037's host-control credential model.
- Creating a separate host manifest format: would duplicate the existing deterministic route/metadata validator.
