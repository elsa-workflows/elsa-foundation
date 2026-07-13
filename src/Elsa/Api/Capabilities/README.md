# API Capabilities

`Elsa.Api.Capabilities` provides the supported management-client bootstrap for an active Elsa shell.
An authenticated `GET /capabilities` returns one caller-neutral document containing only the stable
contracts explicitly declared by the shell's active domain API features.

The distinction between an internal composition feature and a client-visible API capability is defined in
the [feature specification](../../../../specs/092-domain-owned-apis/spec.md). Capability IDs are stable public
promises; they are never inferred from CLR types, feature names, service registrations, or route discovery.

## Composition

Enable the `ApiCapabilities` shell feature. Supported domain API features depend on it and register their
static declarations while their shell services are composed. An omitted domain feature therefore contributes
neither endpoints nor a capability.

The endpoint is secured with `api-capabilities.read`. Its response intentionally contains no caller identity,
effective permissions, arbitrary module state, or rich editor bootstrap data. Clients must still satisfy each
domain endpoint's action-scoped authorization policy.

All advertised `href` values are relative to the active shell route base. A client resolves them against the
URL from which it loaded `/capabilities`; no declaration may assume a shell named `default`.

## Declaration rules

A declaration contains a stable capability ID, a positive major contract version, canonical link relations,
and a source feature ID used only for diagnostics. Link relations are unique within a declaration and links
must be shell-relative.

Equivalent duplicate declarations collapse deterministically. Incompatible declarations for the same
capability fail static shell service configuration, or fail dynamic document assembly if introduced by an
operational source. Capabilities and links are returned in ordinal order.

Operationally conditional links or capabilities belong in typed `IApiCapabilitySource` implementations. They
are evaluated in the active shell scope for each document, so changing operational availability does not
mutate static feature metadata. See [the extension-point catalog](EXTENSION_POINTS.md) for registration and
compatibility obligations.

The normative wire shape and multi-shell examples live in the
[management API contract](../../../../specs/092-domain-owned-apis/contracts/management-api.openapi.yaml) and
[quickstart](../../../../specs/092-domain-owned-apis/quickstart.md).
