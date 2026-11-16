import { describe, expect, it } from "vitest";
import { createEndpointContext } from "../sdk";
import { createStudioRegistry } from "../app/registry";
import { filterAccessibleNavigation, getTopLevelNavigationItemsByPlacement } from "../app/navigation/placement";
import { register as registerConnections } from "../../../../../../../extensions/Connections/studio/Elsa.Studio.Connections/Client/src/module";
import { register as registerSecrets } from "../../../../../../../extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/module";
import { register as registerExternalAuthentication } from "../../../../../../../extensions/Identity/studio/Elsa.Studio.Identity.ExternalAuthentication/Client/src/module";
import { register as registerUserTasks } from "../../../../../../../extensions/UserTasks/studio/Elsa.Studio.UserTasks/Client/src/module";

import { register as registerDashboard } from "../../../../Elsa.Studio.Dashboard/Client/src/module";

describe("registered integration navigation", () => {
  it("keeps Home first and saved analytics discoverable in Operate", () => {
    const api = createStudioRegistry({ hostVersion: "1", sdkVersion: "1", ...createEndpointContext("https://studio.example") });
    registerDashboard(api);
    const visible = filterAccessibleNavigation(api.navigation.list());
    expect(getTopLevelNavigationItemsByPlacement(visible, "main").map(item => item.path)).toEqual(["/dashboard"]);
    const analytics = getTopLevelNavigationItemsByPlacement(visible, "operate");
    expect(analytics.map(item => ({ label: item.label, path: item.path }))).toEqual([
      { label: "Dashboards", path: "/dashboards" },
      { label: "Questions", path: "/questions" }
    ]);
    for (const item of analytics) expect(item.requiresBackend).toEqual([{ id: "elsa.api.studio-dashboard", contractVersion: "1" }]);
  });

  it("places Connections before Secrets using actual module contributions", () => {
    const api = createStudioRegistry({ hostVersion: "1", sdkVersion: "1", ...createEndpointContext("https://studio.example") });
    registerSecrets(api);
    registerConnections(api);
    const visible = filterAccessibleNavigation(api.navigation.list());
    expect(getTopLevelNavigationItemsByPlacement(visible, "integrations").map(item => item.label)).toEqual(["Connections", "Secrets"]);
  });

  it("keeps the real User Tasks inbox and Forms discoverable in Operate", () => {
    const api = createStudioRegistry({ hostVersion: "1", sdkVersion: "1", ...createEndpointContext("https://studio.example") });
    registerUserTasks(api);
    const access = { authenticated: true, permissions: ["user-tasks:read", "user-tasks:manage", "user-task-forms:read", "user-task-forms:manage"] };
    const visible = filterAccessibleNavigation(api.navigation.list(), access);
    const operate = getTopLevelNavigationItemsByPlacement(visible, "operate");
    expect(operate.map(item => ({ label: item.label, path: item.path }))).toEqual([
      { label: "User Tasks", path: "/user-tasks" },
      { label: "Forms", path: "/user-tasks/forms" }
    ]);
    expect(getTopLevelNavigationItemsByPlacement(filterAccessibleNavigation(api.navigation.list(), { authenticated: true, permissions: [] }), "operate")).toEqual([]);
  });

  it("keeps external sign-in and user access discoverable under their own permissions", () => {
    const api = createStudioRegistry({ hostVersion: "1", sdkVersion: "1", ...createEndpointContext("https://studio.example") });
    registerExternalAuthentication(api);
    const access = { authenticated: true, canReadAdmin: false,
      permissions: ["identity/providers:read", "identity/providers:manage", "identity/users:read"] };
    const visible = filterAccessibleNavigation(api.navigation.list(), access);
    expect(getTopLevelNavigationItemsByPlacement(visible, "integrations").map(item => item.path)).toContain("/security/external-sign-in");
    expect(getTopLevelNavigationItemsByPlacement(visible, "operate").map(item => item.path)).toContain("/security/user-access");
    const usersOnly = filterAccessibleNavigation(api.navigation.list(), { ...access, permissions: ["identity/users:read"] });
    expect(usersOnly.map(item => item.path)).toContain("/security/user-access");
    expect(usersOnly.map(item => item.path)).not.toContain("/security/external-sign-in");
    expect(filterAccessibleNavigation(api.navigation.list(), { ...access, permissions: [] })).toEqual([]);
    for (const item of visible) expect(item.requiresBackend).toEqual([{ id: "elsa.api.identity.external-authentication", contractVersion: "1" }]);
  });
});
