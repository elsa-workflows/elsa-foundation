import { describe, expect, it } from "vitest";
import type { StudioNavigationContribution } from "../sdk";
import {
  getSettingsSectionGroups,
  isSettingsSurfacePath,
  resolveSettingsPane,
  resolveSettingsSection,
  settingsAccessDeniedMessage,
  settingsPathFor
} from "../app/settings/settingsSections";

const appearance: StudioNavigationContribution = {
  id: "appearance",
  label: "Appearance",
  path: "/settings/appearance",
  activePathPrefix: "/settings/appearance",
  placement: "settings",
  order: 10
};
const modules: StudioNavigationContribution = {
  id: "modules",
  label: "Modules",
  path: "/modules",
  activePathPrefix: "/modules",
  placement: "admin",
  order: 10
};
const home: StudioNavigationContribution = { id: "home", label: "Home", path: "/dashboard", placement: "main" };
const navigation = [home, appearance, modules];

describe("settings sections", () => {
  it("groups settings placement under Studio and admin under Administration", () => {
    const groups = getSettingsSectionGroups(navigation, { canReadAdmin: true });
    expect(groups.map(group => [group.id, group.sections.map(section => section.id)])).toEqual([
      ["studio", ["appearance"]],
      ["administration", ["modules"]]
    ]);
  });

  it("keeps Appearance without host-control read and hides admin; anonymous/demo still see admin", () => {
    expect(getSettingsSectionGroups(navigation, { canReadAdmin: false }).map(group => [group.id, group.sections.map(section => section.id)]))
      .toEqual([["studio", ["appearance"]]]);
    expect(getSettingsSectionGroups(navigation, { authenticated: false, canReadAdmin: true }).flatMap(group => group.sections).map(section => section.id))
      .toEqual(["appearance", "modules"]);
  });

  it("resolves deep links, original admin paths, and sub-paths via activePathPrefix", () => {
    const groups = getSettingsSectionGroups(navigation, { canReadAdmin: true });
    expect(settingsPathFor(appearance)).toBe("/settings/appearance");
    expect(resolveSettingsSection("/settings", groups)?.id).toBe("appearance");
    expect(resolveSettingsSection("/settings/appearance", groups)?.id).toBe("appearance");
    expect(resolveSettingsSection("/settings/modules", groups)?.id).toBe("modules");
    expect(resolveSettingsSection("/modules", groups)?.id).toBe("modules");
    expect(resolveSettingsSection("/modules/logs", groups)?.id).toBe("modules");
    expect(isSettingsSurfacePath("/settings/appearance", navigation)).toBe(true);
    expect(isSettingsSurfacePath("/modules", navigation)).toBe(true);
    expect(isSettingsSurfacePath("/theme-builder/preview", [appearance, { ...modules, id: "theme-builder", path: "/theme-builder", activePathPrefix: "/theme-builder" }])).toBe(true);
    expect(isSettingsSurfacePath("/dashboard", navigation)).toBe(false);
  });

  it("treats legacy admin URLs as the settings surface even when the user cannot see those sections", () => {
    expect(isSettingsSurfacePath("/modules", navigation)).toBe(true);
    expect(isSettingsSurfacePath("/package-feeds", [...navigation, { id: "package-feeds", label: "Package feeds", path: "/package-feeds", activePathPrefix: "/package-feeds", placement: "admin" }])).toBe(true);
    const visible = getSettingsSectionGroups(navigation, { canReadAdmin: false });
    expect(resolveSettingsSection("/modules", visible)).toBeUndefined();
    expect(resolveSettingsPane("/modules", visible, navigation)).toEqual({ status: "forbidden", section: modules });
    expect(resolveSettingsPane("/settings/modules", visible, navigation).status).toBe("forbidden");
    expect(settingsAccessDeniedMessage).toBe("You don't have access to this section.");
  });
});
