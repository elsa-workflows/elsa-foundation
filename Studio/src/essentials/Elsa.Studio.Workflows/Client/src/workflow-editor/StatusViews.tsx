import React from "react";
import { AlertCircle, Boxes } from "lucide-react";
import { SkeletonRows, StudioResultState } from "@elsa-workflows/studio-ui";

export function WfListSkeleton({ rows = 5 }: { rows?: number }) {
  return <SkeletonRows rows={rows} />;
}

export function WfEmptyState({ icon, title, description, action }: { icon?: React.ReactNode; title: string; description?: string; action?: React.ReactNode }) {
  return <StudioResultState icon={icon ?? <Boxes size={22} />} title={title} description={description} actions={action} />;
}

export function WfErrorCard({ message, title = "Something went wrong", action }: { message?: string; title?: string; action?: React.ReactNode }) {
  return <StudioResultState tone="danger" icon={<AlertCircle size={18} />} title={title} description={message || "Please try again, or check that the Elsa server is reachable."} actions={action} />;
}
