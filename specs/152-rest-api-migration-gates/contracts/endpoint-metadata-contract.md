# Endpoint Metadata Contract

## Purpose

Every enabled first-party REST endpoint must expose framework-neutral metadata sufficient to identify ownership and one primary security disposition from ASP.NET Core's published endpoint surface.

## Contract

- `EndpointOwnershipMetadata` contains one stable, non-empty owner identifier and classifies it as host, module, or dynamic shell; dynamic-shell ownership also records its shell and exact non-negative generation.
- Permission-protected endpoints carry Foundation Identity's permission metadata and canonical authorization policy.
- Intentionally public endpoints carry a typed category/reason disposition plus standard anonymous-access metadata.
- Host-credential endpoints carry a typed host-credential disposition and its owning host surface.
- Named-policy endpoints carry a typed policy name and policy owner plus standard authorization metadata.
- The four disposition forms are mutually exclusive as primary endpoint security.
- Conventions operate on standard `IEndpointConventionBuilder`/route builders and do not introduce a custom endpoint DSL.

## Validation

Runtime inventory rejects missing/conflicting ownership, missing/multiple primary dispositions, empty values, wildcard permission values, and unowned named policies. Diagnostics include normalized route, method, runtime identity, and available owner information.
