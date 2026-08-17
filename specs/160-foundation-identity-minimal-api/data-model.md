# Data Model: Foundation Identity Minimal API Migration

This work changes endpoint authoring and does not introduce persistent entities.

## Endpoint contract

- Route and HTTP method
- Request binding and media types
- Success and failure status/body/header/cookie/redirect/challenge observations
- Stable endpoint name, tag, and consumed OpenAPI operation/schema projection
- Owner, authoring model, and public-or-policy security disposition

## Authentication disposition

- Public operation, configured interactive authentication, or one Foundation policy
- Accepted interactive scheme set
- Normalized principal and permission implications evaluated by the shared evaluator

## Compatibility case

- Immutable before observation
- Actual migrated observation
- Optional exact approved difference with rationale
- Consumption status so unused or overly broad approvals fail

## Collectibility cycle

- Owner assembly/load context weak reference
- Mapped route delegates and metadata
- Authentication schemes/provider delegates
- Service-provider and serializer materialization
- Disposal/release completion and collection result
