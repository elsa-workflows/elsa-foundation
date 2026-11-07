import type { ReactNode } from "react";
import { StudioPageHeader } from "@elsa-workflows/studio-ui";

export function WorkflowsPageFrame({ title, context, workbench = false, definitions = false, children }: {
  title?: string;
  context?: ReactNode;
  workbench?: boolean;
  definitions?: boolean;
  children: ReactNode;
}) {
  return (
    <section className={`studio-page wf-page${workbench ? " wf-page--run-workbench" : ""}${definitions ? " wf-page--definitions" : ""}`}>
      {title ? <StudioPageHeader title={title} headingLevel={2} actions={context} /> : null}
      {children}
    </section>
  );
}
