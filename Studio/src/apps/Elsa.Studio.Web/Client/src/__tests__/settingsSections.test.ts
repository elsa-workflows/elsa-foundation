import { describe, expect, it } from "vitest";
import type { StudioNavigationContribution } from "../sdk";
import {
  getSettingsSectionGroups,
  isSettingsSurfacePath,
  resolveSettingsSection,
  settingsPathFor
} from "../app/settings/settingsSections";

const appearance: StudioNavigationContribution = {
  id: "appearance",
  label: "Appearance",
  path: "/settings/appearance",
  placement: "settings",
  order: 10
};
const modules: StudioNavigationContribution = { id: "modules", label: "Modules", path: "/modules", placement: "admin", order: 10 };
const home: StudioNavigationContribution = { id: "home", label: "Home", path: "/dashboard", placement: "main" };

describe("settings sections", () => {
  it("groups settings placement under Studio and admin under Administration", () => {
    const groups = getSettingsSectionGroups([home, appearance, modules], { canReadAdmin: true });
    expect(groups.map(group => [group.id, group.sections.map(section => section.id)])).toEqual([
      ["studio", ["appearance"]],
      ["administration", ["modules"]]
    ]);
  });

  it("hides settings and admin sections without host-control read, and keeps them for anonymous/demo", () => {
    expect(getSettingsSectionGroups([appearance, modules], { canReadAdmin: false })).toEqual([]);
    expect(getSettingsSectionGroups([appearance, modules], { authenticated: false, canReadAdmin: true }).flatMap(group => group.sections).map(section => section.id))
      .toEqual(["appearance", "modules"]);
  });

  it("resolves deep links and original admin paths", () => {
    const groups = getSettingsSectionGroups([appearance, modules], { canReadAdmin: true });
    expect(settingsPathFor(appearance)).toBe("/settings/appearance");
    expect(resolveSettingsSection("/settings", groups)?.id).toBe("appearance");
    expect(resolveSettingsSection("/settings/appearance", groups)?.id).toBe("appearance");
    expect(resolveSettingsSection("/settings/modules", groups)?.id).toBe("modules");
    expect(resolveSettingsSection("/modules", groups)?.id).toBe("modules");
    expect(isSettingsSurfacePath("/settings/appearance", groups)).toBe(true);
    expect(isSettingsSurfacePath("/modules", groups)).toBe(true);
    expect(isSettingsSurfacePath("/dashboard", groups)).toBe(false);
  });
});
