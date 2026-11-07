import { describe, expect, it } from "vitest";
import { createEndpointContext } from "../sdk";
import { createStudioRegistry } from "../app/registry";
import { filterAccessibleNavigation, getTopLevelNavigationItemsByPlacement } from "../app/navigation/placement";
import { register as registerConnections } from "../../../../../../../Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/module";
import { register as registerSecrets } from "../../../../../../../Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/module";

describe("registered integration navigation", () => {
  it("places Connections before Secrets using actual module contributions", () => {
    const api = createStudioRegistry({ hostVersion: "1", sdkVersion: "1", ...createEndpointContext("https://studio.example") });
    registerSecrets(api);
    registerConnections(api);
    const visible = filterAccessibleNavigation(api.navigation.list());
    expect(getTopLevelNavigationItemsByPlacement(visible, "integrations").map(item => item.label)).toEqual(["Connections", "Secrets"]);
  });
});
