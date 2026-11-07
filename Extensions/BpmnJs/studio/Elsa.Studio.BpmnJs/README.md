# Optional bpmn-js Studio canvas

This module contributes a controlled canvas to the `Elsa.Studio.Workflows` canvas-editor slot. Compose the `Elsa.Studio.BpmnJs` RCL and enable `BpmnJsStudio` alongside `WorkflowsStudio`. The in-box Web distribution makes that choice explicitly; the Workflows package does not depend on this module.

For an npm host, add `@elsa-workflows/studio-bpmn-js`, provide the SDK canvas registry, and register the optional module before rendering a Workflows route:

```tsx
import { register as registerBpmnJs } from "@elsa-workflows/studio-bpmn-js";
import "@elsa-workflows/studio-bpmn-js/style.css";

import { createContributionRegistry, studioSlots, type StudioWorkflowDesignerCanvasContribution } from "@elsa-workflows/studio-sdk";

const api = {
  ...hostApi,
  workflowDesigner: {
    ...hostApi.workflowDesigner,
    canvasEditors: createContributionRegistry<StudioWorkflowDesignerCanvasContribution>({
      slot: studioSlots.workflowDesignerCanvasEditors
    })
  }
};
registerBpmnJs(api);
```

Create the registry as part of the host API construction (the SDK exposes it as readonly). Module-mode assets defer toolkit JavaScript, CSS, and fonts until the canvas loads. The npm library emits its own public `style.css`; upstream diagram/font CSS remains imported by the deferred canvas for the consumer bundler to process. An npm host controls when it imports the public stylesheet. Omitting the package and registration keeps toolkit code and assets out of the consumer build.

The current prototype advertises controlled-canvas protocol 1 and `elsa.bpmn.structure` schema `1.0.0` for workflow definitions and Activity Definition graphs. It does not advertise schema 2. The host owns the complete canonical document, shared inspector, history, selection, and revision-sensitive lifecycle commands. A failed lazy load offers a local retry or Elsa fallback. Editor preference uses the governed user/tenant/host/backend preference scope and is never stored in process metadata.

`bpmn-js` is pinned to `18.31.1`. Its bpmn.io license requires the upstream logo source to remain unchanged and the watermark to remain visible and unobstructed. Distributed assets include `THIRD_PARTY_NOTICES.txt` with the exact pinned dependency licenses. Refresh it using `pnpm notices` only after a reviewed dependency upgrade; keep the package and asset budgets passing.
