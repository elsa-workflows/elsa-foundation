import { createContext, type RefCallback } from "react";

export interface ActivityDefinitionAuthoringTabsHost {
  activeView: string;
  designerPanelId: string;
  designerTabId: string;
  setTablistHost: RefCallback<HTMLDivElement>;
  setCenterViewHost: RefCallback<HTMLDivElement>;
}

export const ActivityDefinitionAuthoringTabsHostContext = createContext<ActivityDefinitionAuthoringTabsHost | null>(null);