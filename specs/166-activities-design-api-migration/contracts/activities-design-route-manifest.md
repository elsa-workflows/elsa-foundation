# Activities Design Route Manifest Contract

The owner prefix is `/design/activities`. The frozen baseline owns the final wire templates and metadata; this
reviewed logical inventory is fixed at 38 and must remain one-to-one through migration. `R` means
`activity-design.read`; `M` means `activity-design.manage`.

| Identity | Method | Relative route | Action | Success |
|---|---|---|---|---|
| Availability.GetSettings | GET | `/availability/settings` | R | 200 |
| Availability.ListDiagnostics | GET | `/availability/diagnostics` | R | 200 |
| Availability.SaveSettings | PUT | `/availability/settings` | M | 200 |
| AuthoringCapabilities.Get | GET | `/authoring-capabilities` | R | 200 |
| Catalog.List | GET | `/catalog` | R | 200 |
| Definitions.Add | POST | `/definitions` | M | 201 + Location |
| Definitions.PreviewFork | POST | `/definitions/{definitionId}/fork-previews` | M | 200 |
| Definitions.List | GET | `/definitions` | R | 200 |
| Definitions.Get | GET | `/definitions/{definitionId}` | R | 200 |
| Definitions.Update | PATCH | `/definitions/{definitionId}` | M | 200 |
| Definitions.Recommendation | PUT | `/definitions/{definitionId}/recommendation` | M | 200 |
| Definitions.Picker | GET | `/definitions/picker` | R | 200 |
| Definitions.ListDrafts | GET | `/definitions/{definitionId}/drafts` | R | 200 |
| Definitions.AddDraft | POST | `/definitions/{definitionId}/drafts` | M | 201 + Location |
| Definitions.ListVersions | GET | `/definitions/{definitionId}/versions` | R | 200 |
| Drafts.Get | GET | `/drafts/{draftId}` | R | 200 |
| Drafts.Replace | PUT | `/drafts/{draftId}` | M | 200 |
| Drafts.UpdatePresentation | PATCH | `/drafts/{draftId}/presentation` | M | 200 |
| Drafts.ConflictCopy | POST | `/drafts/{draftId}/conflict-copies` | M | 201 + Location |
| Drafts.Validate | POST | `/drafts/{draftId}/validate` | M | 200 |
| Drafts.MigrateProvider | POST | `/drafts/{draftId}/migrate-provider` | M | 201 + Location |
| Drafts.ProposeContract | POST | `/drafts/{draftId}/contract-proposals` | M | 200 |
| Drafts.ApplyContractProposal | POST | `/drafts/{draftId}/contract-proposals/apply` | M | 200 |
| Drafts.Discard | DELETE | `/drafts/{draftId}` | M | 204 |
| Drafts.Diff | POST | `/drafts/{draftId}/diff` | R | 200 |
| Forks.Apply | POST | `/fork-candidates/{candidateId}/apply` | M | 201 + Location |
| Forks.GetStatus | GET | `/forks/{idempotencyKey}` | R | 200 |
| Versions.Dependencies | GET | `/versions/{versionId}/dependencies` | R | 200 |
| Versions.Diff | GET | `/versions/{fromVersionId}/diff/{toVersionId}` | R | 200 |
| Versions.Get | GET | `/versions/{versionId}` | R | 200 |
| Versions.Retire | POST | `/versions/{versionId}/retire` | M | 200 |
| Versions.Restore | POST | `/versions/{versionId}/restore` | M | 200 |
| Versions.Revoke | POST | `/versions/{versionId}/revoke` | M | 200 |
| UpgradePlans.Create | POST | `/upgrade-plans` | M | 201 + Location |
| UpgradePlans.Get | GET | `/upgrade-plans/{planId}` | R | 200 |
| UpgradePlans.Apply | POST | `/upgrade-plans/{planId}/apply` | M | 200 |
| UpgradePlans.GetReceipt | GET | `/upgrade-plans/{planId}/receipts/{receiptId}` | R | 200 |
| UpgradePlans.Refresh | POST | `/upgrade-plans/{planId}/refresh` | M | 201 + Location |

## Manifest invariants

- Exactly 38 unique logical registrations, method/template pairs, stable operation IDs, and owner metadata entries.
- Same-method equivalent-template or multi-method overlaps fail the manifest gate.
- Each protected registration names exactly one catalog action; provider-payload/authoring resource actions remain
  explicit secondary service decisions rather than route wildcard metadata.
- Transition completion removes these exact 38 first-party FastEndpoints registrations and no others.
- Route values override body identifiers for every mutating request shape and remain absent from JSON schemas.
- `/definitions/picker` remains unambiguous beside `/definitions/{definitionId}`.
