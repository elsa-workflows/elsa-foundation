# JSON variables and activity bindings

**Status**: Implemented
**Issue**: #3477
**Program-goal state**: none/free-flow

An author can declare JSON workflow data and bind it through any activity's JSON contract. HTTP Endpoint → variable → Write Http Response is one acceptance journey, not a special case.

- Offer **JSON** in the shared type catalog, backed by `System.Text.Json.JsonElement`, with JSON defaults and durable storage. Keep existing Object defaults unchanged.
- Recognize nullable JSON contracts and accept supported JSON-compatible variables and workflow inputs through the shared pickers and publication/runtime conversion rules.
- Support output capture from any activity into a JSON variable. Preserve JSON kinds, property casing and exact numeric tokens; text remains text, without format sniffing.
- Preserve validation for unrelated typed inputs, collection shapes, transient resources and explicit conversion modes.
- Verify generic activities plus the reported HTTP authoring/runtime journey, with causal red/green regressions and normal delivery gates.

Existing #3167 representation-inference work and #3190 Object default semantics retain their owners. No architecture decision is ratified by this change.
