# Activity Availability Uses A Layered Policy Stack

Activity availability controls whether an activity type may be newly selected in the workflow definition editor; it does not decide whether existing workflow nodes render, whether workflow definitions are valid, or whether runtime execution is allowed. We will model availability as a layered policy stack: persisted catalog rows define the factual activity universe, optional host configuration sets the maximum baseline, persisted management settings may narrow that baseline, and future user-context policy such as RBAC may narrow it further.

Host configuration is optional. When omitted, every non-removed catalog activity is baseline-eligible. Hosts may define explicit activity sets and optional include/exclude rules; missing or empty include rules mean all catalog activities are candidates, and exclude rules always remove matches. Management settings are stored separately from the catalog, use one mode such as all-except or only with explicit activity keys and set names, and default to allowing all baseline-eligible activities when no settings exist.

Unknown activity keys and unknown activity set names are retained as unresolved references with diagnostics. They do not fail startup, are not silently discarded, and only affect policy evaluation when they can be resolved to concrete catalog activity keys.

The picker receives only the final addable list, while management surfaces may show diagnostic availability states such as blocked by host baseline, hidden by management settings, removed from catalog, or unresolved reference. Existing workflow definitions still display all authored activity nodes even when those activity types are no longer addable; the designer may show a non-blocking warning for such nodes.

Core policy contracts and evaluation belong with Activities Design because the policy is about design-time activity addability and depends on activity catalog identity. Persisted management settings and their API surface belong outside the catalog store so the catalog remains a source of activity facts rather than user or management policy.

Host baseline changes are deployment configuration changes and take effect at startup unless a host explicitly wires configuration reload. Management settings are runtime policy data and take effect immediately after save.
