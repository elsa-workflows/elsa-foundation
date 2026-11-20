import React from "react";
import { Hammer } from "lucide-react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { ExtensionBuilderPage } from "./extension-builder";
import "./styles.css";

export const extensionBuilderPath = "/extension-builder";

export function register(api: ElsaStudioModuleApi) {
  // Extension Builder is an operator surface, so it sits in the gated Administration group next to module
  // management. The host honours the declared placement and icon instead of a special case (#535).
  api.navigation.add({
    id: "extension-builder",
    label: "Extension Builder",
    path: extensionBuilderPath,
    order: 50,
    iconColor: "#ec4899",
    placement: "admin",
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
