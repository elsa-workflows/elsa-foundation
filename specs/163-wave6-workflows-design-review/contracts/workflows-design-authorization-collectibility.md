# Workflows Design Authorization and Collectibility Contract

Each endpoint exposes one owner-local catalog action through standard authorization metadata. The
shared evaluator, not route metadata, accepts implied and wildcard grants and enforces normalized
external claims plus tenant/resource constraints. A retained FastEndpoints canary must produce the
same decisions.

Three real load/map/invoke/OpenAPI/serialize/dispose/unload cycles must leave no owner delegate,
metadata, authentication, DI, store/provider, serializer, or OpenAPI state strongly reachable.
