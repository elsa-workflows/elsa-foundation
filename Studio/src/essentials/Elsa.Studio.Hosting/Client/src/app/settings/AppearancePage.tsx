import React from "react";
import { StudioButton } from "../ui/forms/Button";
import { useTheme } from "../components/ThemeProvider";
import {
  appearanceModePreferences,
  defaultAppearancePreferences,
  themeModePreferenceLabels,
  type ThemeModePreference
} from "../themes/appearance";
import { getSupportedThemeModes, getThemeModeDefinition, isThemeMode } from "../themes/presets";
import { isNavModePreference, navModePreferenceLabels, navModePreferences } from "../themes/navMode";
import { isWorkspaceStyle, workspaceStyles } from "../themes/workspaceStyles";

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

export function AppearancePage({
  themeBuilderHref,
  onNavigate
}: {
  themeBuilderHref?: string;
  onNavigate: (path: string) => void;
}) {
  const {
    currentTheme,
    currentColorScheme,
    workspaceStyle,
    setWorkspaceStyle,
    setColorScheme,
    availableThemes,
    store,
    modePreference,
    setModePreference,
    setTheme,
    resetAppearance,
    supportedModes,
    navModePreference,
    setNavModePreference
  } = useTheme();
  const defaultThemeId = store.defaultThemeId || availableThemes[0]?.id;
  const modeOptions = appearanceModePreferences.filter(option => option === "system" || supportedModes.includes(option));

  return (
    <section className="settings-page" aria-labelledby="settings-appearance-title">
      <header className="settings-page-header">
        <h2 id="settings-appearance-title">Appearance</h2>
        <p>Choose your workspace, colors and mode independently.</p>
        <span className="settings-scope">This browser</span>
      </header>

      <AppearanceCard title="Workspace style" description="Arrange your tools without changing your color scheme.">
        <RadioCardGroup
          ariaLabel="Workspace style"
          workspacePreviews
          value={workspaceStyle ?? "theme"}
          options={[
            ...workspaceStyles.map(style => ({ value: style.id, label: style.name, description: style.description })),
            { value: "theme", label: "Theme default", description: `Keep ${currentTheme.name}'s original arrangement.`, defaultValue: true }
          ]}
          onChange={value => setWorkspaceStyle(isWorkspaceStyle(value) ? value : null)}
        />
      </AppearanceCard>

      <AppearanceCard title="Color scheme" description="A coordinated palette for surfaces, text and accents. Your workspace stays put.">
        {availableThemes.length === 0 ? <p>No themes are available for selection.</p> : <RadioCardGroup
          ariaLabel="Color scheme"
          value={currentColorScheme.id}
          options={availableThemes.map(theme => ({
            value: theme.id,
            label: theme.name,
            swatches: getSupportedThemeModes(theme).map(mode => getThemeModeDefinition(theme, mode)?.primary).filter((color): color is string => Boolean(color))
          }))}
          onChange={setColorScheme}
        />}
      </AppearanceCard>

      <AppearanceCard
        title="Mode"
        description="Light and Dark stay put. System follows the OS colour scheme and contrast, live."
      >
        <RadioCardGroup
          ariaLabel="Colour mode"
          value={modePreference}
          options={modeOptions.map(option => ({
            value: option,
            label: themeModePreferenceLabels[option],
            defaultValue: option === defaultAppearancePreferences.mode
          }))}
          onChange={value => isThemeModePreferenceValue(value) && setModePreference(value)}
        />
      </AppearanceCard>

      <details className="settings-theme-presets">
        <summary>Theme presets</summary>
        <AppearanceCard
          title="Theme preset"
          description="Apply an original theme's complete combination, clearing workspace and color overrides."
        >
          {availableThemes.length === 0 ? <p>No themes are available for selection.</p> : <RadioCardGroup
            ariaLabel="Theme"
            value={currentTheme.id}
            options={availableThemes.map(theme => ({
              value: theme.id,
              label: theme.name,
              defaultValue: theme.id === defaultThemeId,
              swatches: getSupportedThemeModes(theme).map(mode => getThemeModeDefinition(theme, mode)?.primary).filter((color): color is string => Boolean(color))
            }))}
            onChange={setTheme}
          />}
        </AppearanceCard>
      </details>

      <AppearanceCard
        title="Navigation"
        description="Follow the workspace, or pin navigation to the left or top."
      >
        <RadioCardGroup
          ariaLabel="Navigation"
          value={navModePreference}
          options={navModePreferences.map(preference => ({
            value: preference,
            label: navModePreferenceLabels[preference],
            defaultValue: preference === defaultAppearancePreferences.navigation
          }))}
          onChange={value => isNavModePreference(value) && setNavModePreference(value)}
        />
      </AppearanceCard>

      <footer className="settings-page-footer">
        <StudioButton variant="ghost" onClick={resetAppearance}>
          Reset appearance
        </StudioButton>
        {themeBuilderHref ? (
          <a
            className="settings-builder-link"
            href={themeBuilderHref}
            onClick={event => {
              event.preventDefault();
              onNavigate(themeBuilderHref);
            }}
          >
            Open Theme Builder
          </a>
        ) : null}
      </footer>
    </section>
  );
}

function AppearanceCard({
  title,
  description,
  children
}: {
  title: string;
  description: string;
  children: React.ReactNode;
}) {
  return (
    <section className="settings-card">
      <header className="settings-card-header">
        <h3>{title}</h3>
        <p>{description}</p>
      </header>
      {children}
    </section>
  );
}

function RadioCardGroup({
  ariaLabel,
  workspacePreviews = false,
  value,
  options,
  onChange
}: {
  ariaLabel: string;
  workspacePreviews?: boolean;
  value: string;
  options: Array<{ value: string; label: string; description?: string; defaultValue?: boolean; swatches?: string[]; disabled?: boolean }>;
  onChange: (value: string) => void;
}) {
  const optionRefs = React.useRef<Record<string, HTMLButtonElement | null>>({});
  const enabled = options.filter(option => !option.disabled);
  const tabStop = enabled.some(option => option.value === value) ? value : enabled[0]?.value;

  const handleKeyDown = (event: React.KeyboardEvent) => {
    const index = enabled.findIndex(option => optionRefs.current[option.value] === event.target);
    const target = index < 0 ? undefined : keyTarget(event.key, index, enabled.length);
    if (target === undefined) return;
    event.preventDefault();
    onChange(enabled[target].value);
    optionRefs.current[enabled[target].value]?.focus();
  };

  return (
    <div className="settings-card-grid" role="radiogroup" aria-label={ariaLabel} onKeyDown={handleKeyDown}>
      {options.map(option => (
        <button
          key={option.value}
          ref={element => { optionRefs.current[option.value] = element; }}
          type="button"
          role="radio"
          className="settings-radio-card"
          aria-checked={value === option.value}
          aria-label={option.defaultValue ? `${option.label} (default)` : option.label}
          tabIndex={option.value === tabStop ? 0 : -1}
          disabled={option.disabled}
          onClick={() => onChange(option.value)}
        >
          {workspacePreviews && option.value !== "theme" ? (
            <span className="workspace-style-preview" data-style={option.value} aria-hidden="true"><i /><i /><i /></span>
          ) : null}
          {option.swatches && option.swatches.length > 0 ? (
            <span className="settings-swatches" aria-hidden="true">
              {option.swatches.map((color, index) => (
                <span key={`${option.value}-${index}`} className="settings-swatch" style={{ backgroundColor: color }} />
              ))}
            </span>
          ) : null}
          <span className="settings-radio-card-title">
            {option.label}
            {option.defaultValue ? <span className="settings-default-badge">Default</span> : null}
          </span>
          {option.description ? <small className="settings-radio-description">{option.description}</small> : null}
        </button>
      ))}
    </div>
  );
}

function isThemeModePreferenceValue(value: string): value is ThemeModePreference {
  return value === "system" || isThemeMode(value);
}
