# Worker profile data model

Reuse [Spec174 selection models](../174-profile-selection-planner/data-model.md) and [Spec176 file bridge](../176-composition-file-bridge/data-model.md); no new production wire shape.

- **SelectionCatalog v3**: Existing schema/publisher, new catalog digest; unchanged Embedded definition/group plus one Worker definition.
- **Worker selection definition**: `kind=profile`, `id=worker-http`, `version=1`, digest over owned metadata/membership/explanations. Exact members in [contract](contracts/worker-profile.md). No deployment values.
- **Authored and accepted selection**: Same catalog/profile refs and exact19 baseline; the secondary uses explicit `remove=[ActivitiesControlFlow]` and exact18. Existing accepted lock/findings rules remain unchanged.
- **Candidate fixture state**: Isolated source/accepted/candidate directories, selected shell/environment, expected file hashes, exact accepted IDs, source preservation hashes. Owned by existing async fixture; cleanup includes failures.
- **Child startup/receipt**: Private stdin startup adds candidate directory/shell/environment; safe receipt exposes actual enabled IDs and independently computed consumed-file hashes. Host scope stays a separate ordinary input. No second feature list is transmitted.
- **Runtime/IAM state**: Existing separate files/contexts; actual persisted rules can grant execute, stimulus or capabilities-read. Parent issuer/key/token survives restart; each child has distinct PID and same built artifact/candidate.

Transitions: authored→planned candidate→interactive accepted→fresh generated files→actual child activation→stop/await exit→distinct child reopen. Selection metadata does not skip activation or database gates.
