import React, { useEffect, useState } from "react";
import type { ElsaStudioModuleApi, StudioActivityPropertyEditorProps } from "@elsa-workflows/studio-sdk";
import { StudioAlert } from "@elsa-workflows/studio-ui";
import { failureMessage, listDecisions, versions, type DecisionAsset, type DecisionVersion } from "./decisionsApi";
export function DecisionVersionEditor({ api, value, disabled, onChange, onValidationChange }: StudioActivityPropertyEditorProps & { api: ElsaStudioModuleApi }) {
  const selected = typeof value === "string" ? value : "", split = selected.lastIndexOf("@"), assetId = split >= 0 ? selected.slice(0, split) : "", selection = split >= 0 ? selected.slice(split + 1) : "";
  const [assets, setAssets] = useState<DecisionAsset[]>([]), [choices, setChoices] = useState<DecisionVersion[]>([]), [loading, setLoading] = useState(true), [loadingVersions, setLoadingVersions] = useState(false), [error, setError] = useState("");
  const unpublished = !loading && !loadingVersions && !!assetId && choices.length === 0;
  useEffect(() => { let active = true; void listDecisions(api).then(items => { if (active) setAssets(items); }, failure => { if (active) setError(failureMessage(failure)); }).finally(() => { if (active) setLoading(false); }); return () => { active = false; }; }, [api]);
  useEffect(() => { let current = true; setChoices([]); setLoadingVersions(!!assetId); if (assetId) void versions(api, assetId).then(items => { if (current) setChoices(items); }, failure => { if (current) setError(failureMessage(failure)); }).finally(() => { if (current) setLoadingVersions(false); }); else setLoadingVersions(false); return () => { current = false; }; }, [api, assetId]);
  useEffect(() => { onValidationChange?.(!assetId || !selection || unpublished); return () => onValidationChange?.(false); }, [assetId, selection, unpublished, onValidationChange]);
  return <div className="decision-version-picker"><label>Decision<select aria-label="Business decision" disabled={disabled || loading} value={assetId} onChange={event => { setError(""); onChange(event.target.value ? `${event.target.value}@latest` : ""); }}><option value="">Select a decision</option>{assetId && !assets.some(item => item.id === assetId) ? <option value={assetId}>Unavailable decision ({assetId})</option> : null}{assets.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
    <label>Version<select aria-label="Business decision version" disabled={disabled || !assetId} value={selection} onChange={event => onChange(`${assetId}@${event.target.value}`)}><option value="latest">Latest when workflow is published</option>{selection && selection !== "latest" && !choices.some(item => item.id === selection) ? <option value={selection}>Unavailable version ({selection})</option> : null}{choices.map(item => <option key={item.id} value={item.id}>Version {item.number}</option>)}</select></label>
    <p className="decision-muted">Workflow publication freezes the selected policy. Later changes do not switch an existing published workflow.</p>
    {unpublished ? <StudioAlert tone="warning">This decision has no published version. Publish it first; Review &amp; publish cannot pin a draft with @latest.</StudioAlert> : null}
    {error ? <p role="alert">{error}</p> : null}</div>;
}
