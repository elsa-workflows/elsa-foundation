import { describe, expect, it } from "vitest";
import { createEndpointContext } from "@elsa-workflows/studio-sdk";
import { register } from "../module";
import { createStudioRegistry } from "../../../../Elsa.Studio.Hosting/Client/src/app/registry";
import { filterAccessibleNavigation, getChildNavigationItems } from "../../../../Elsa.Studio.Hosting/Client/src/app/navigation/placement";

describe("registered workflow navigation", () => {
  it("opens Definitions first using the real host and module registration", () => {
    const api = createStudioRegistry({ hostVersion: "1", sdkVersion: "1", ...createEndpointContext("https://studio.example") });
    register(api);
    const visible = filterAccessibleNavigation(api.navigation.list());
    const workflows = visible.find(item => item.id === "workflows")!;
    expect(workflows.path).toBe("/workflows/definitions");
    expect(getChildNavigationItems(visible, workflows).sort((a, b) => (a.order ?? 1000) - (b.order ?? 1000)).map(item => item.label)).toEqual(["Definitions", "Executables"]);
    expect(visible.find(item => item.id === "workflows-runtime-diagnostics")?.placement).toBe("runtime");
    expect(visible.find(item => item.id === "workflows-activity-availability")?.placement).toBe("authoring");
  });
});
