import React, { useEffect, useRef, useState } from "react";
import type { ElsaStudioModuleApi, StudioSettingDescriptor } from "@elsa-workflows/studio-sdk";
import { StudioButton, selectSettingEditor } from "@elsa-workflows/studio-ui";
import {
  formatSettingPreview,
  getFeatureSettingValues,
  getSettingGroups,
  getSettingValue,
  isBooleanSetting,
  stableStringify,
  type DraftFeature
} from "./featureModel";

export function FeatureSettingsSummary({ feature }: { feature: DraftFeature }) {
  const settingGroups = getSettingGroups(feature.settings);

  return (
    <div className="feature-management-settings-summary">
      {settingGroups.map(group => (
        <section key={group.id} className="feature-management-setting-summary-group">
          <h5>{group.label}</h5>
          <div className="feature-management-setting-summary-list">
            {group.settings.map(setting => (
              <div key={setting.name} className="feature-management-setting-summary-row">
                <span className="feature-management-setting-summary-meta">
                  <span>
                    <strong>{setting.displayName || setting.name}</strong>
                    {setting.required ? <em>Required</em> : null}
                    {setting.restartRequired ? <em>Reload</em> : null}
                    {setting.advanced ? <em>Advanced</em> : null}
                  </span>
                  {setting.description ? <small>{setting.description}</small> : null}
                  <code>{setting.name}</code>
                </span>
                <span className="feature-management-setting-summary-value">
                  {formatSettingPreview(setting, getSettingValue(feature, setting))}
                </span>
              </div>
            ))}
          </div>
        </section>
      ))}
    </div>
  );
}

export function FeatureSettingsDialog({
  moduleApi,
  feature,
  disabled,
  onSettingChange,
  onClose
}: {
  moduleApi: ElsaStudioModuleApi;
  feature: DraftFeature;
  disabled: boolean;
  onSettingChange(feature: DraftFeature, setting: StudioSettingDescriptor, value: unknown): void;
  onClose(): void;
}) {
  const [values, setValues] = useState<Record<string, unknown>>(() => getFeatureSettingValues(feature));
  const featureIdRef = useRef(feature.id);
  const settingGroups = getSettingGroups(feature.settings);

  useEffect(() => {
    if (featureIdRef.current === feature.id) return;
    featureIdRef.current = feature.id;
    setValues(getFeatureSettingValues(feature));
  }, [feature]);

  function updateSettingValue(setting: StudioSettingDescriptor, value: unknown) {
    setValues(current => ({
      ...current,
      [setting.name]: value
    }));
  }

  function resetDialogChanges() {
    setValues(getFeatureSettingValues(feature));
  }

  function saveDraft() {
    for (const setting of feature.settings) {
      const value = values[setting.name];
      if (stableStringify(value) !== stableStringify(getSettingValue(feature, setting))) {
        onSettingChange(feature, setting, value);
      }
    }

    onClose();
  }

  return (
    <div className="feature-management-dialog-backdrop" role="presentation">
      <section
        className="feature-management-settings-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="feature-management-settings-dialog-title"
      >
        <div className="feature-management-dialog-heading">
          <div>
            <span>Settings</span>
            <h3 id="feature-management-settings-dialog-title">{feature.displayName || feature.id}</h3>
          </div>
          <StudioButton onClick={onClose}>Close</StudioButton>
        </div>

        <div className="feature-management-dialog-body">
          <div className="feature-management-settings">
            {settingGroups.map(group => (
              <section key={group.id} className="feature-management-setting-group">
                <h5>{group.label}</h5>
                <div>
                  {group.settings.map(setting => {
                    const editor = selectSettingEditor(moduleApi, setting);
                    const Editor = editor.component;

                    return (
                      <SettingField key={setting.name} setting={setting} presentsField={editor.presentsField}>
                        <Editor
                          setting={setting}
                          value={values[setting.name]}
                          disabled={disabled}
                          onChange={value => updateSettingValue(setting, value)}
                        />
                      </SettingField>
                    );
                  })}
                </div>
              </section>
            ))}
          </div>
        </div>

        <div className="feature-management-dialog-actions">
          <span>Save keeps changes local until you review changes and apply the reviewed draft.</span>
          <div>
            <StudioButton onClick={onClose}>Cancel</StudioButton>
            <StudioButton onClick={resetDialogChanges} disabled={disabled}>Reset changes</StudioButton>
            <StudioButton variant="primary" onClick={saveDraft} disabled={disabled}>Save</StudioButton>
          </div>
        </div>
      </section>
    </div>
  );
}

function SettingField({ setting, children, presentsField }: { setting: StudioSettingDescriptor; children: React.ReactNode; presentsField?: boolean }) {
  if (presentsField) return <div className="feature-management-setting outlined">{children}</div>;
  const booleanSetting = isBooleanSetting(setting);

  return (
    <label className={booleanSetting ? "feature-management-setting boolean" : "feature-management-setting"}>
      <span className="feature-management-setting-main">
        <span className="feature-management-setting-title">
          <strong>{setting.displayName || setting.name}</strong>
          {setting.required ? <em>Required</em> : null}
          {setting.restartRequired ? <em>Reload</em> : null}
          {setting.advanced ? <em>Advanced</em> : null}
        </span>
        {booleanSetting ? (
          <span className="feature-management-setting-control">
            {children}
          </span>
        ) : null}
      </span>
      {!booleanSetting ? (
        <span className="feature-management-setting-control">
          {children}
        </span>
      ) : null}
      {setting.description ? <small>{setting.description}</small> : null}
    </label>
  );
}
