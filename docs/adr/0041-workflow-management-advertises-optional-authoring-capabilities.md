# Workflow Management Advertises Optional Authoring Capabilities

Status: superseded (2026-07-13)

## Supersession

This decision was superseded by the domain-owned management API design in
[`specs/091-domain-owned-apis`](../../specs/091-domain-owned-apis/spec.md). The host-owned workflow-management
facade and its boolean capability response were removed before release.

The replacement is the authenticated, shell-scoped `GET /capabilities` contract owned by
`Elsa.Api.Capabilities`. Active domain API features explicitly declare stable capability IDs, major contract
versions, and canonical relative links; operationally conditional links are contributed through typed Sources.
The document remains permission-neutral, and each domain endpoint enforces its own action-scoped permission.

The original context, decision, and consequences below are retained as the historical record of the short-lived
approach that motivated the global capability contract.

## Context

Studio needs to know whether an activated Foundation shell supports scoped-variable analysis before
it sends an analysis request. Treating a failed analysis request as feature discovery creates noisy
404s and cannot distinguish an unsupported backend from a transient analysis failure.

Foundation has no global backend manifest or API-version contract suitable for this feature. Its
existing capability contracts are small, area-owned boolean responses, and scoped-variable analysis
belongs to workflow management rather than to the storage or variable-type descriptor catalogs.

## Decision

Workflow management exposes `GET /_elsa/workflow-management/capabilities`. The additive response
currently contains `scopedVariableAnalysis`, which is `true` only when the activated shell can resolve
the scoped-variable authoring contract. Missing, false, malformed, or failed capability discovery is
treated by Studio as unsupported, and Studio does not send the analysis POST in those cases.

The capability response and the gated operation follow the same workflow-management authorization
convention. New optional workflow-management features may add boolean fields to this response. A
contract version will be introduced only if a breaking envelope change becomes necessary.

## Consequences

Matched Foundation and Studio versions discover support without intentionally issuing a request to a
missing feature endpoint. Foundation shells that omit the authoring service still return HTTP 200
with `scopedVariableAnalysis: false`.

Foundation versions released before this capability route may return 404 for the discovery GET.
Studio fails closed after that single discovery failure and never probes the analysis route. Avoiding
even that compatibility 404 would require coupling feature discovery to an unrelated older response;
that tradeoff was rejected in favor of a cohesive workflow-management capability contract.

The removed storage-driver descriptor URLs are not capabilities and are not restored. Foundation has
no live storage-driver catalog contract, so Studio retains the compatibility field as free text.
