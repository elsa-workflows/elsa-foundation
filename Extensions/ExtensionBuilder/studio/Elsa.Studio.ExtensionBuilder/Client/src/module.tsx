import React from "react";
import { Hammer } from "lucide-react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { ExtensionBuilderPage } from "./extension-builder";
import "./styles.css";

export const extensionBuilderPath = "/extension-builder";

export function register(api: ElsaStudioModuleApi) {
  // Extension Builder is an operator surface, so it sits in the Settings section next to module management; the host
  // honours the declared section and icon instead of carrying a special case for the module (#535).
  api.navigation.add({
    id: "extension-builder",
    label: "Extension Builder",
    path: extensionBuilderPath,
    order: 40,
    iconColor: "#ec4899",
    section: "settings",
    icon: Hammer
  });
  api.routes.add({
    id: "extension-builder",
    label: "Extension Builder",
    path: extensionBuilderPath,
    component: () => <ExtensionBuilderPage api={api} />
  });
}
