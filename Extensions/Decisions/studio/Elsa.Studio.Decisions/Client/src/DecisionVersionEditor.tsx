import { Plus } from "lucide-react";
import React, { useCallback, useEffect, useRef, useState } from "react";
import { openStudioWorkspaceTab, qualifyStudioBackendPath, type ElsaStudioModuleApi, type StudioActivityPropertyEditorProps } from "@elsa-workflows/studio-sdk";
import { StudioAlert, StudioButton } from "@elsa-workflows/studio-ui";
import { can, failureMessage, listDecisions, versions, type DecisionAsset, type DecisionVersion } from "./decisionsApi";
import { useForegroundGeneration } from "./decisionFreshness";
/**
 * Opens the new-decision editor beside the workflow so its unsaved changes stay open. The workflow's
 * hidden tab re-runs its effects when shown again, which reloads this picker's list.
 */
function openNewDecision() {
  const target = qualifyStudioBackendPath("/decisions?new=1");
  if (!openStudioWorkspaceTab(target)) window.open(target, "_blank", "noopener");
}
export function DecisionVersionEditor({ api, value, disabled, onChange, onValidationChange }: StudioActivityPropertyEditorProps & { api: ElsaStudioModuleApi }) {
  const selected = typeof value === "string" ? value : "", split = selected.lastIndexOf("@"), assetId = split >= 0 ? selected.slice(0, split) : "", selection = split >= 0 ? selected.slice(split + 1) : "";
  const [assets, setAssets] = useState<DecisionAsset[]>([]), [choices, setChoices] = useState<DecisionVersion[]>([]), [loading, setLoading] = useState(true), [loadingVersions, setLoadingVersions] = useState(false), [error, setError] = useState("");
  const freshness = useForegroundGeneration();
  const versionsGeneration = useRef(0);
  const observedAssetId = useRef<string | null>(null);
  const unpublished = !loading && !loadingVersions && !error && !!assetId && choices.length === 0;
  const loadVersions = useCallback((id: string, indicate: boolean) => {
    const generation = ++versionsGeneration.current;
    if (indicate) setLoadingVersions(true);
    void versions(api, id).then(items => {
      if (generation !== versionsGeneration.current) return;
      setChoices(items);
      setError("");
    }, failure => {
      if (generation !== versionsGeneration.current) return;
      setError(failureMessage(failure));
    }).finally(() => {
      if (generation !== versionsGeneration.current) return;
      setLoadingVersions(false);
    });
  }, [api]);
  useEffect(() => { let active = true; void listDecisions(api).then(items => { if (active) setAssets(items); }, failure => { if (active) setError(failureMessage(failure)); }).finally(() => { if (active) setLoading(false); }); return () => { active = false; }; }, [api, freshness]);
  useEffect(() => {
    if (!assetId) {
      versionsGeneration.current += 1;
      observedAssetId.current = "";
      setChoices([]);
      setLoadingVersions(false);
      return () => { versionsGeneration.current += 1; };
    }
    const switched = observedAssetId.current !== null && observedAssetId.current !== assetId;
    const indicate = observedAssetId.current === null || switched;
    observedAssetId.current = assetId;
    if (switched) setChoices([]);
    loadVersions(assetId, indicate);
    return () => { versionsGeneration.current += 1; };
  }, [api, assetId, freshness, loadVersions]);
  useEffect(() => { onValidationChange?.(!assetId || !selection || unpublished || !!error); return () => onValidationChange?.(false); }, [assetId, selection, unpublished, error, onValidationChange]);
  return <div className="decision-version-picker"><label>Decision<select aria-label="Business decision" disabled={disabled || loading} value={assetId} onChange={event => { setError(""); onChange(event.target.value ? `${event.target.value}@latest` : ""); }}><option value="">Select a decision</option>{assetId && !assets.some(item => item.id === assetId) ? <option value={assetId}>Unavailable decision ({assetId})</option> : null}{assets.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
    {can(api, "decisions:manage") ? <StudioButton className="decision-new-shortcut" size="sm" disabled={disabled} title="Opens the decision editor in a new tab" onClick={openNewDecision}><Plus size={14} aria-hidden="true" /> New decision</StudioButton> : null}
    <label>Version<select aria-label="Business decision version" disabled={disabled || !assetId} value={selection} onFocus={() => { if (assetId) loadVersions(assetId, false); }} onChange={event => onChange(`${assetId}@${event.target.value}`)}><option value="latest">Latest when workflow is published</option>{selection && selection !== "latest" && !choices.some(item => item.id === selection) ? <option value={selection}>Unavailable version ({selection})</option> : null}{choices.map(item => <option key={item.id} value={item.id}>Version {item.number}</option>)}</select></label>
    <p className="decision-muted">Workflow publication freezes the selected policy. Later changes do not switch an existing published workflow.</p>
    {unpublished ? <StudioAlert tone="warning">This decision has no published version. Publish it first; Review &amp; publish cannot pin a draft with @latest.</StudioAlert> : null}
    {error ? <p role="alert">{error}</p> : null}</div>;
}
