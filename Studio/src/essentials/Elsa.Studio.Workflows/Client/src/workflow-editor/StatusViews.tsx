import React from "react";
import { Boxes } from "lucide-react";
import { StudioResultState } from "@elsa-workflows/studio-ui";

export function WfEmptyState({ icon, title, description, action, headingLevel }: { icon?: React.ReactNode; title: string; description?: string; action?: React.ReactNode; headingLevel?: 2 | 3 }) {
  return <StudioResultState headingLevel={headingLevel} icon={icon ?? <Boxes size={22} />} title={title} description={description} actions={action} />;
}
