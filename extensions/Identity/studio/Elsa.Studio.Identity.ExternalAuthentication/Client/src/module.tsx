import React from "react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { ExternalAuthenticationPage } from "./ExternalAuthenticationPage";
import { UserAccessPage } from "./UserAccessPage";
import "./styles.css";

export function register(api: ElsaStudioModuleApi) {
  const requirements = [{ id: "elsa.api.identity.external-authentication", contractVersion: "1" }];
  api.featureAreas.add({ id: "external-authentication", title: "External sign-in", description: "Connect identity providers and preview Elsa access.",
    navGroup: "Security", ownedPaths: ["/security/external-sign-in"], required: false, defaultEnabled: true, order: 47,
    nav: { title: "External sign-in", path: "/security/external-sign-in", placement: "more", order: 32,
      requiresBackend: requirements, requiresPermission: ["identity/providers:read", "identity/providers:manage"] },
    routes: [{ id: "external-authentication", path: "/security/external-sign-in", label: "External sign-in", requiresBackend: requirements,
      workspace: { resourceKey: () => "identity:external-sign-in" },
      component: () => <ExternalAuthenticationPage context={api.backend} dialogs={api.dialogs} /> }]
  });
  api.featureAreas.add({ id: "identity-user-access", title: "User access", description: "Inspect identity links and manage Elsa sessions.",
    navGroup: "Security", ownedPaths: ["/security/user-access"], required: false, defaultEnabled: true, order: 48,
    nav: { title: "User access", path: "/security/user-access", placement: "more", order: 33, requiresBackend: requirements,
      requiresPermission: "identity/users:read" },
    routes: [{ id: "identity-user-access", path: "/security/user-access", label: "User access", requiresBackend: requirements,
      workspace: { resourceKey: () => "identity:user-access" }, component: () => <UserAccessPage context={api.backend} dialogs={api.dialogs} /> }]
  });
}
