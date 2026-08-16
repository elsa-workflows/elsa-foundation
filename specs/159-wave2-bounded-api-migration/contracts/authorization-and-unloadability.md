# Authorization and Unloadability Contract

Every route has exactly one module owner, `EndpointAuthoringModels.MinimalApi`, and one Foundation permission disposition. Tests exercise anonymous 401, unrelated 403, exact action, implied manage, wildcard evaluator grant, normalized identity, and cross-tenant/user isolation. Mappers never inspect permission claims or implement tenant checks outside existing service boundaries.

Each of the four owners is exercised in repeated collectible-context cycles. The cycle releases endpoint routes, delegates, DI scopes/providers, serializer/OpenAPI documents, and disposal references before observing a weak reference. Any retained owner must be investigated with real route/DI/serializer/disposal evidence; blanket waivers are prohibited.
