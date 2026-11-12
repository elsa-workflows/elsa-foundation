import { describe, expect, it } from "vitest";
import { createEndpointContext } from "../sdk";
import { createStudioRegistry } from "../app/registry";
import { filterAccessibleNavigation, getTopLevelNavigationItemsByPlacement } from "../app/navigation/placement";
import { register as registerConnections } from "../../../../../../../Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/module";
import { register as registerSecrets } from "../../../../../../../Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/module";
import { register as registerUserTasks } from "../../../../../../../Extensions/UserTasks/studio/Elsa.Studio.UserTasks/Client/src/module";

describe("registered integration navigation", () => {
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
});
