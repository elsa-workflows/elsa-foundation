# Weaver direct-apply risk classification is Elsa-owned

Weaver provider output may include risk hints, but Elsa owns provider-neutral risk classification for direct workflow graph edits. Backend policy classifies generated operation batches before returning them, Studio rechecks against live designer working state before applying them, and any disagreement or uncertainty fails closed into clarification or proposal instead of direct apply.
