# Extension points

- `DecisionsWorkflowsApiFeature` composes exact-material authorization through existing Foundation `IPermissionResourceHandler`, workflow closure resource authorization and workflow library visibility policy.
- Hosts may supply `IWorkflowLibraryVisibilityPolicy`; registration preserves an existing policy.
- The owning Publishing API checks the exact produced closure before invoking any export target; the Decisions contribution never replaces host targets.
