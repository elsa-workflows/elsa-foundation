# Data model

No persistent entity, schema or ownership change.

- **Configuration snapshot**: `WorkflowsDesignApi.WorkflowOrganizationEnabled`, Boolean, default true, immutable for a composed shell generation. The existing settings store owns source precedence, review, revision checks and activation.
- **Retained organization**: Existing store contracts/entities from [spec 1173](../1173-workflow-library/spec.md); off/on never rewrites identities, revisions, queries, columns, owners or assignments.
- **Availability facts**: Existing advertised operation relations, independently resolved for the selected immutable backend session. Paging remains supported independently of organization.
- **Browse recovery**: Existing URL-backed location; unsupported organization constraints remain visible until explicit recovery. Recovery preserves unrelated location values and saved column choices.

Transitions: enabled → reviewed disabled → refreshed unavailable UI → restart retaining data → reviewed enabled → refreshed restored UI. Permission and tenant/resource visibility stay constant through all transitions.
