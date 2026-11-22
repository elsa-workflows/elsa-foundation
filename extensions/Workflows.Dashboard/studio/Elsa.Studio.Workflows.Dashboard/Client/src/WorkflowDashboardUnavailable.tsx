import React from "react";
import { StudioResultState, TechnicalDetails } from "@elsa-workflows/studio-ui";

export function WorkflowDashboardUnavailable({ title, errorCode }: { title: string; errorCode?: string }) {
  return <StudioResultState headingLevel={3} tone="danger" title={title} description="Try refreshing the widget.">
    {errorCode ? <TechnicalDetails label="Technical details"><code>{errorCode}</code></TechnicalDetails> : null}
  </StudioResultState>;
}
