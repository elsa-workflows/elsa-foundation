import React from "react";
import * as DropdownMenu from "@radix-ui/react-dropdown-menu";
import { Check, Contrast, Moon, Palette, PanelLeft, PanelTop, Sun, SunDim, SwatchBook, type LucideIcon } from "lucide-react";
import { useTheme } from "./ThemeProvider";
import { allThemeModes, getSupportedThemeModes, getThemeModeDefinition, isThemeMode, themeModeLabels, type ThemeMode } from "../themes/presets";
import { isNavModePreference, navModePreferenceLabels, navModePreferences, type NavModePreference } from "../themes/navMode";
import { isWorkspaceStyle, workspaceStyles } from "../themes/workspaceStyles";
import { isSidebarColorPreference, sidebarColorPreferenceLabels, sidebarColorPreferences, type SidebarColorPreference } from "../themes/sidebarColor";
import "./ThemeSwitcher.css";

const modeIcons: Record<ThemeMode, LucideIcon> = {
  light: Sun,
  dark: Moon,
  dim: SunDim,
  "high-contrast": Contrast
};

// High contrast is an accessibility mode, so it stays in the menu rather than the quick control.
const quickModes: readonly ThemeMode[] = ["light", "dark", "dim"];

/** The radio-group arrow keys wrap around; Home/End jump to the ends. Undefined for any other key. */
function keyTarget(key: string, index: number, count: number): number | undefined {
  switch (key) {
    case "ArrowRight":
    case "ArrowDown": return (index + 1) % count;
    case "ArrowLeft":
    case "ArrowUp": return (index - 1 + count) % count;
    case "Home": return 0;
    case "End": return count - 1;
    default: return undefined;
  }
}

/** Compact radiogroup for the base modes; offers only the ones the current theme defines (always at least Light and Dark). */
function QuickModeToggle() {
  const { mode, setMode, supportedModes } = useTheme();
  const options = quickModes.filter(themeMode => supportedModes.includes(themeMode));
  const optionRefs = React.useRef<Record<string, HTMLButtonElement | null>>({});

  // High contrast is not a quick option, so nothing is checked then; keep the group reachable by Tab.
  const tabStop = options.includes(mode) ? mode : options[0];

  const handleKeyDown = (event: React.KeyboardEvent) => {
    const index = options.findIndex(themeMode => optionRefs.current[themeMode] === event.target);
    const target = index < 0 ? undefined : keyTarget(event.key, index, options.length);
    if (target === undefined) return;
    event.preventDefault();
    setMode(options[target]);
    optionRefs.current[options[target]]?.focus();
  };

  return (
    <div className="theme-mode-toggle" role="radiogroup" aria-label="Colour mode" onKeyDown={handleKeyDown}>
      {options.map(themeMode => {
        const Icon = modeIcons[themeMode];
        return (
          <button
            key={themeMode}
            ref={element => { optionRefs.current[themeMode] = element; }}
            type="button"
            role="radio"
            className="theme-mode-toggle-option"
            data-mode={themeMode}
            aria-checked={mode === themeMode}
            aria-label={themeModeLabels[themeMode]}
            title={`${themeModeLabels[themeMode]} mode`}
            tabIndex={themeMode === tabStop ? 0 : -1}
            onClick={() => setMode(themeMode)}
          >
            <Icon size={16} aria-hidden="true" />
          </button>
        );
      })}
    </div>
  );
}

const navModeIcons: Record<NavModePreference, LucideIcon> = {
  theme: SwatchBook,
  left: PanelLeft,
  top: PanelTop
};

const sidebarColorIcons: Record<SidebarColorPreference, LucideIcon> = {
  theme: SwatchBook,
  light: Sun,
  dark: Moon
};

export function ThemeSwitcher() {
  const {
    currentTheme, currentColorScheme, workspaceStyle, setWorkspaceStyle, setColorScheme, mode, setMode, availableThemes, supportedModes,
    navMode, navModePreference, themeNavMode, setNavModePreference, sidebarColorPreference, setSidebarColorPreference
  } = useTheme();
  // The setting paints whichever surface holds the navigation: the sidebar, or the top bar.
  const sidebarColorTitle = navMode === "top" ? "Navigation bar color" : "Sidebar color";

  return (
    <div className="theme-switcher-container">
      <QuickModeToggle />

      {/* Theme Selector Dropdown (Radix: keyboard nav, Esc, focus trap/restore, ARIA menu) */}
      <DropdownMenu.Root>
        <DropdownMenu.Trigger asChild>
          <button
            type="button"
            className="theme-selector-button"
            aria-label="Select theme"
            title={`${workspaceStyles.find(style => style.id === workspaceStyle)?.name ?? currentTheme.name} · ${currentColorScheme.name} · ${themeModeLabels[mode]}`}
          >
            <Palette size={18} />
            <span className="theme-selector-swatch" aria-hidden="true">
              {supportedModes.map(themeMode => (
                <span
                  key={themeMode}
                  className="theme-selector-swatch-dot"
                  style={{ backgroundColor: getThemeModeDefinition(currentColorScheme, themeMode)?.primary }}
                />
              ))}
            </span>
          </button>
        </DropdownMenu.Trigger>

        <DropdownMenu.Portal>
          <DropdownMenu.Content className="theme-dropdown" align="end" sideOffset={8}>
            <DropdownMenu.Label className="theme-dropdown-header">
              <span className="theme-dropdown-title">Workspace style</span>
            </DropdownMenu.Label>
            <DropdownMenu.RadioGroup
              className="theme-style-group"
              aria-label="Workspace style"
              value={workspaceStyle ?? "theme"}
              onValueChange={value => setWorkspaceStyle(isWorkspaceStyle(value) ? value : null)}
            >
              {[...workspaceStyles.map(style => ({ id: style.id, name: style.name })), { id: "theme", name: "Theme default" }].map(style => (
                <DropdownMenu.RadioItem key={style.id} value={style.id} className="theme-mode-item" onSelect={event => event.preventDefault()}>
                  <span>{style.name}</span>
                </DropdownMenu.RadioItem>
              ))}
            </DropdownMenu.RadioGroup>
            <DropdownMenu.Label className="theme-dropdown-header">
              <span className="theme-dropdown-title">Appearance</span>
            </DropdownMenu.Label>
            <DropdownMenu.RadioGroup
              className="theme-mode-group"
              aria-label="Appearance"
              value={mode}
              onValueChange={value => isThemeMode(value) && setMode(value)}
            >
              {allThemeModes.map(themeMode => {
                const Icon = modeIcons[themeMode];
                const available = supportedModes.includes(themeMode);
                return (
                  <DropdownMenu.RadioItem
                    key={themeMode}
                    value={themeMode}
                    className="theme-mode-item"
                    disabled={!available}
                    title={available ? themeModeLabels[themeMode] : `${currentColorScheme.name} has no ${themeModeLabels[themeMode].toLowerCase()} mode`}
                    // Keep the menu open so modes can be compared in place.
                    onSelect={event => event.preventDefault()}
                  >
                    <Icon size={16} aria-hidden="true" />
                    <span>{themeModeLabels[themeMode]}</span>
                  </DropdownMenu.RadioItem>
                );
              })}
            </DropdownMenu.RadioGroup>
            <DropdownMenu.Item className="theme-follow-system" onSelect={event => { event.preventDefault(); setMode("system"); }}>
              Follow system appearance
            </DropdownMenu.Item>

            <DropdownMenu.Label className="theme-dropdown-header">
              <span className="theme-dropdown-title">Navigation</span>
            </DropdownMenu.Label>
            <DropdownMenu.RadioGroup
              className="theme-nav-group"
              aria-label="Navigation"
              value={navModePreference}
              onValueChange={value => isNavModePreference(value) && setNavModePreference(value)}
            >
              {navModePreferences.map(preference => {
                const Icon = navModeIcons[preference];
                return (
                  <DropdownMenu.RadioItem
                    key={preference}
                    value={preference}
                    className="theme-mode-item"
                    title={preference === "theme"
                      ? `Follow ${workspaceStyles.find(style => style.id === workspaceStyle)?.name ?? currentTheme.name}: ${navModePreferenceLabels[themeNavMode].toLowerCase()} navigation`
                      : `${navModePreferenceLabels[preference]} navigation`}
                    onSelect={event => event.preventDefault()}
                  >
                    <Icon size={16} aria-hidden="true" />
                    <span>{navModePreferenceLabels[preference]}</span>
                  </DropdownMenu.RadioItem>
                );
              })}
            </DropdownMenu.RadioGroup>

            <DropdownMenu.Label className="theme-dropdown-header">
              <span className="theme-dropdown-title">{sidebarColorTitle}</span>
            </DropdownMenu.Label>
            <DropdownMenu.RadioGroup
              className="theme-sidebar-color-group"
              aria-label={sidebarColorTitle}
              value={sidebarColorPreference}
              onValueChange={value => isSidebarColorPreference(value) && setSidebarColorPreference(value)}
            >
              {sidebarColorPreferences.map(preference => {
                const Icon = sidebarColorIcons[preference];
                const highContrast = mode === "high-contrast";
                return (
                  <DropdownMenu.RadioItem
                    key={preference}
                    value={preference}
                    className="theme-mode-item"
                    // High contrast keeps its own accessible navigation, so a pinned tone does not apply there.
                    disabled={highContrast}
                    title={highContrast
                      ? "High contrast keeps its own navigation colors"
                      : preference === "theme" ? "Follow the workspace appearance" : `${sidebarColorPreferenceLabels[preference]} navigation in every appearance`}
                    onSelect={event => event.preventDefault()}
                  >
                    <Icon size={16} aria-hidden="true" />
                    <span>{sidebarColorPreferenceLabels[preference]}</span>
                  </DropdownMenu.RadioItem>
                );
              })}
            </DropdownMenu.RadioGroup>

            <DropdownMenu.Label className="theme-dropdown-header">
              <span className="theme-dropdown-title">Color scheme</span>
            </DropdownMenu.Label>
            <DropdownMenu.RadioGroup className="theme-list" value={currentColorScheme.id} onValueChange={setColorScheme} aria-label="Color scheme">
              {availableThemes.map(theme => (
                <DropdownMenu.RadioItem
                  key={theme.id}
                  value={theme.id}
                  className={`theme-item ${currentColorScheme.id === theme.id ? "active" : ""}`}
                  aria-label={theme.name}
                  onSelect={event => event.preventDefault()}
                  title={`${theme.name} colors; keeps the current workspace`}
                >
                  <span className="theme-item-preview" aria-hidden="true">
                    {getSupportedThemeModes(theme).map(themeMode => (
                      <span
                        key={themeMode}
                        className={`theme-color-dot ${themeMode}`}
                        style={{ backgroundColor: getThemeModeDefinition(theme, themeMode)?.primary }}
                      />
                    ))}
                  </span>
                  <span className="theme-item-name">{theme.name}</span>
                  {currentColorScheme.id === theme.id && (
                    <Check size={16} className="theme-item-checkmark" aria-hidden="true" />
                  )}
                </DropdownMenu.RadioItem>
              ))}
            </DropdownMenu.RadioGroup>
          </DropdownMenu.Content>
        </DropdownMenu.Portal>
      </DropdownMenu.Root>
    </div>
  );
}
