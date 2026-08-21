---
status: proposed
date: 2026-08-21
decision_context: First-party API structure restructure, decided by Sipke Schoorstra; supersedes ADR 0069
---

# REST API contracts ship in one assembly per domain

## Context

[ADR 0069](0069-openapi-contract-types-use-stable-api-core.md) split each dynamically replaceable
REST module in two: a collectible `*.Api` implementation assembly and a stable `*.Api.Core`
assembly holding every type reachable from API Explorer. The split existed for one reason. ASP.NET
Core's API Explorer and OpenAPI document service retain an endpoint's request and response `Type`
for the host service-provider lifetime, so a contract type in a collectible assembly pins that
assembly forever.

Four modules adopted the pattern: Workflows Design, Workflows Publishing, Activities Design, and
Workflows Runtime. Each carried four coupled mechanisms — a `<Compile Remove>` list in the
implementation project, a linked-compile `<Compile Include>` list in a `Core` project that owned
almost no files of its own, a project reference, and 335 `TypeForwardedTo` attributes across three
assemblies. Deleting the seemingly empty `Core` directory produced 49 CS0729 errors.

Three facts reframed the trade.

First, **no production code creates a collectible `AssemblyLoadContext`.** Neither `src/` nor the
CShells packages construct one. Every collectible context in the repository is built by a test
fixture, so assembly unloading was a property proven in tests and never exercised by a host.

Second, **the split does not buy feature-disable.** Retiring a disabled feature's endpoints from
routing and from the OpenAPI document needs only `AddDynamicEndpointApiExplorerRefresh()`. That was
demonstrated with no collectible context and no `Api.Core` present, and bite-proofed by removing the
bridge and watching both tests fail.

Third, **the pattern cannot serve third parties**, which is the product's actual dynamic-loading
target. ADR 0069 says so directly: third-party unloadable contract publication is unresolved. An
independently authored module either has its contract assembly permanently loaded by the host — with
unbounded growth and version conflicts — or keeps its contracts collectible and unloads nothing.

The split therefore protected a capability that no host used, could not be extended to the case that
motivated it, and stood directly in the way of grouping each endpoint's code together, because its
compile globs are shaped by layer (`Models/`, `Requests/`, `Commands/`).

## Decision

First-party REST modules ship their wire contracts in the same assembly as their implementation.
There is one `*.Api` project per domain.

The `*.Api.Core` projects and the `TypeForwardedTo` lists that supported them are removed. Public
CLR namespaces and JSON contracts are unchanged; only the assembly that exports a contract type
changes, and the repository is pre-release, so no binary-compatibility obligation applies.

First-party REST modules are consequently **not unloadable**. A host that replaces a module
generation retains that generation's contract types through API Explorer. Feature enable and disable
continue to add and retire routes and OpenAPI operations through the endpoint change-token bridge,
which is unaffected.

`RequireStableOpenApi()` stays on every mapper that already calls it. Its checks on metadata
objects, members, delegates, transformers, and serializer artifacts remain meaningful — those
artifacts pin an owner regardless of where contract types live. Its collectible-contract-type check
is now unsatisfiable for these modules by construction rather than by accident, and that is the
intended reading.

### The classification the assembly boundary used to carry

Splitting the assembly also recorded, structurally, which public types in `Api.Models`,
`Api.Requests`, and `Api.Commands` were part of the HTTP contract: contract types were compiled into
`Api.Core`, and interfaces, exceptions, mapping helpers, and other public support types stayed
behind. Merging the assemblies removes that carrier.

Each module's contract tests now hold two explicit lists — its wire contracts and its
implementation-only public types — and assert that together they account for exactly the public
types exported from the contract namespaces. A new or deleted public type must be classified in one
of the two lists, so the decision stays deliberate. The public-shape hashes are retained unchanged
in purpose; their values moved once, because a constructed generic type's `FullName` embeds its
arguments' assembly-qualified names.

## Consequences

- Each domain has one API project directory and one API assembly, so an endpoint's route, handler,
  request, and response can live together.
- Four coupled build mechanisms per module collapse to none, and 335 type forwards are deleted.
- First-party modules lose their unloadability proofs. The collectibility cycles for Workflows
  Design, Publishing, Activities Design, and Runtime are removed rather than left asserting a
  guarantee the code no longer makes.
- A long-running host that repeatedly replaces a module generation retains each generation's
  contract types. No host does this today; a host that intends to must resolve third-party
  contract publication first.
- Third-party unloadable contract publication remains unresolved and is now the only path to
  dynamic module replacement. It is tracked by
  [#1414](https://github.com/elsa-workflows/elsa-foundation/issues/1414).

## Evidence

- ADR 0069's mechanism and its stated third-party limitation:
  [`0069-openapi-contract-types-use-stable-api-core.md`](0069-openapi-contract-types-use-stable-api-core.md)
- Endpoint authoring and shared-layer bounds:
  [`0068-first-party-rest-apis-use-aspnet-core-minimal-apis.md`](0068-first-party-rest-apis-use-aspnet-core-minimal-apis.md)
