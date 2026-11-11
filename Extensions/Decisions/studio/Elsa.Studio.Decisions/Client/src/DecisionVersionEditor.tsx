import { Plus } from "lucide-react";
import React, { useCallback, useEffect, useRef, useState } from "react";
import { openStudioWorkspaceTab, qualifyStudioBackendPath, type ElsaStudioModuleApi, type StudioActivityPropertyEditorProps } from "@elsa-workflows/studio-sdk";
import { StudioAlert, StudioButton } from "@elsa-workflows/studio-ui";
import { can, failureMessage, listDecisions, versions, type DecisionAsset, type DecisionVersion } from "./decisionsApi";
import { useForegroundGeneration } from "./decisionFreshness";
import { beginNewDecisionHandoff, cancelNewDecisionHandoff, isNewDecisionHandoffPending, newDecisionHandoffParameter, subscribeNewDecisionHandoffs, takeNewDecisionHandoff, type NewDecisionResult } from "./newDecisionHandoff";
export function DecisionVersionEditor({ api, value, disabled, onChange, onValidationChange }: StudioActivityPropertyEditorProps & { api: ElsaStudioModuleApi }) {
  const selected = typeof value === "string" ? value : "", split = selected.lastIndexOf("@"), assetId = split >= 0 ? selected.slice(0, split) : "", selection = split >= 0 ? selected.slice(split + 1) : "";
  const [assets, setAssets] = useState<DecisionAsset[]>([]), [choices, setChoices] = useState<DecisionVersion[]>([]), [loading, setLoading] = useState(true), [loadingVersions, setLoadingVersions] = useState(false), [error, setError] = useState("");
  const freshness = useForegroundGeneration();
  const versionsGeneration = useRef(0);
  const observedAssetId = useRef<string | null>(null);
  // The New decision flow this picker started, and the value it showed then. Refs survive while the
  // workflow's tab is hidden; only this picker holds the token, so other inspectors never react. The flow
  // belongs to this mounted picker: selecting another activity remounts it and ends the flow by design.
  const handoff = useRef<{ token: string; baseline: string } | null>(null);
  const [created, setCreated] = useState<NewDecisionResult | null>(null), [announcement, setAnnouncement] = useState("");
  const current = useRef({ selected, disabled, onChange });
  current.current = { selected, disabled, onChange };
  const dropHandoff = useCallback(() => { if (handoff.current) cancelNewDecisionHandoff(handoff.current.token); handoff.current = null; }, []);
  /**
   * Opens the new-decision editor beside the workflow so its unsaved changes stay open. The workflow's
   * hidden tab re-runs its effects when shown again, which reloads this picker's list and takes the
   * decision saved there. A browser-tab fallback is another document, so it only refreshes the list.
   */
  const openNewDecision = useCallback(() => {
    // A flow still waiting keeps its token, so the same address focuses the editor tab already open
    // (and any work typed there) instead of opening a second, blank one.
    const waiting = handoff.current && isNewDecisionHandoffPending(handoff.current.token) ? handoff.current.token : null;
    if (!waiting) dropHandoff();
    const token = waiting ?? beginNewDecisionHandoff();
    handoff.current = { token, baseline: current.current.selected };
    if (openStudioWorkspaceTab(qualifyStudioBackendPath(`/decisions?new=1&${newDecisionHandoffParameter}=${encodeURIComponent(token)}`))) return;
    dropHandoff();
    window.open(qualifyStudioBackendPath("/decisions?new=1"), "_blank", "noopener");
  }, [dropHandoff]);
  // Selects the decision saved from this picker's flow, unless the author picked something else meanwhile
  // or the inspector became read-only. Either way the flow ends here.
  const claimHandoff = useCallback(() => {
    const pending = handoff.current;
    const result = pending ? takeNewDecisionHandoff(pending.token) : null;
    if (!pending || !result) return;
    handoff.current = null;
    const latest = current.current;
    if (latest.disabled || latest.selected !== pending.baseline) return;
    setCreated(result); setError(""); setAnnouncement(`Selected ${result.name}, the decision you just created.`);
    latest.onChange(`${result.id}@latest`);
  }, []);
  useEffect(() => { claimHandoff(); return subscribeNewDecisionHandoffs(claimHandoff); }, [claimHandoff, freshness]);
  // The handed-off name only bridges the gap until the reloaded list lists the decision; after that, a
  // decision that disappears again reads as unavailable like any other.
  useEffect(() => { if (created && assets.some(item => item.id === created.id)) setCreated(null); }, [assets, created]);
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
  return <div className="decision-version-picker"><label>Decision<select aria-label="Business decision" disabled={disabled || loading} value={assetId} onChange={event => { dropHandoff(); setError(""); setAnnouncement(""); onChange(event.target.value ? `${event.target.value}@latest` : ""); }}><option value="">Select a decision</option>{assetId && !assets.some(item => item.id === assetId) ? <option value={assetId}>{created?.id === assetId ? created.name : `Unavailable decision (${assetId})`}</option> : null}{assets.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
    {can(api, "decisions:manage") ? <StudioButton className="decision-new-shortcut" size="sm" disabled={disabled} title="Opens the decision editor in a new tab" onClick={openNewDecision}><Plus size={14} aria-hidden="true" /> New decision</StudioButton> : null}
    <label>Version<select aria-label="Business decision version" disabled={disabled || !assetId} value={selection} onFocus={() => { if (assetId) loadVersions(assetId, false); }} onChange={event => { dropHandoff(); setAnnouncement(""); onChange(`${assetId}@${event.target.value}`); }}><option value="latest">Latest when workflow is published</option>{selection && selection !== "latest" && !choices.some(item => item.id === selection) ? <option value={selection}>Unavailable version ({selection})</option> : null}{choices.map(item => <option key={item.id} value={item.id}>Version {item.number}</option>)}</select></label>
    <p role="status" className="decision-visually-hidden">{announcement}</p>
    <p className="decision-muted">Workflow publication freezes the selected policy. Later changes do not switch an existing published workflow.</p>
    {unpublished ? <StudioAlert tone="warning">This decision has no published version. Publish it first; Review &amp; publish cannot pin a draft with @latest.</StudioAlert> : null}
    {error ? <p role="alert">{error}</p> : null}</div>;
}
