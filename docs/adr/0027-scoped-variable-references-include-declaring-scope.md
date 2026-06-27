# Scoped Variable References Include Declaring Scope

Status: proposed

Authored variable expressions that target a container-scoped variable store both the variable reference key and the declaring scope identity, where the declaring scope identity is the authored container activity node identity (`ActivityNode.NodeId`), while workflow-scoped references may use an explicit workflow scope sentinel or omit the scope where the contract defines that default. This chooses stable references over implicit rebinding through the current visible scope chain, because moving activities between containers should not silently retarget a saved expression to a different variable with the same reference key, and runtime activity execution identifiers are per invocation rather than authored identity.

Container-scoped variable declarations are authored once on the declaring container node, but their runtime values are scoped to one concrete container activity execution. This keeps repeated, retried, or parallel container executions from overwriting each other's values while preserving a stable authored declaration identity.

Sibling branches inside the same container activity execution share the same container-scoped variable values. Concurrent writes must therefore be governed by runtime scheduling and checkpoint policy rather than by silently copying variables per branch.

Descendant activities may read and assign visible ancestor container-scoped variables by explicit scoped reference. They may not target variables declared by sibling or otherwise unrelated container scopes.

When an activity is moved out of the scope that declares a referenced variable, the authored reference is preserved and validation marks it invalid rather than retargeting it to a same-named variable in the new scope.

Workflow-scoped and container-scoped variables should reuse the same variable declaration value object where their intrinsic facts are the same; scope is supplied by the owning workflow or container location rather than by duplicating declaration types.

Variable names and reference keys are unique within one declaring scope, not across all visible scopes. Nested scopes may shadow outer variables; validation allows this, while authoring surfaces may warn when shadowing could be confusing.

Variable default values initialize runtime state once when the declaring scope starts: workflow-scoped variables at workflow execution start, and container-scoped variables at each concrete container activity execution start. Defaults are not re-evaluated on every read.

Variable value changes are made in the active runtime execution state and become durable through normal runtime checkpoint boundaries. Assignments do not create a separate immediate-persistence path.

When execution resumes inside a container scope, the runtime recovers the variable values for the original concrete container activity execution rather than reinitializing that container's defaults.

After a container scope completes, its variable values are no longer available to later runtime expressions, but they may be retained as runtime inspection or history evidence according to the workflow execution retention and redaction policy.

This decision covers authored workflow-scoped and container-scoped variables only. User-authored activity-local variables are out of scope; activity-local execution state remains an internal runtime or activity concern unless a later decision introduces it explicitly.

Structured variable expression bindings persist explicit scoped references. Freehand JavaScript may continue to expose name-based helper functions and a visible-scope variable container as a convenience, with names resolved through the same nearest-scope lookup rules.

Authoring pickers show variables visible from the selected activity's scope by default. Repair and diagnostic surfaces may show invalid existing references so users can understand and retarget broken scoped references deliberately.

Copying or importing a container subtree preserves variable reference keys inside that subtree, assigns new authored node identities to copied activity nodes, and remaps internal scoped references to the new declaring scope node identities. References to variables outside the copied subtree remain external only when still visible from the copy target; otherwise validation marks them invalid.
