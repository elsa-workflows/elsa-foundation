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
  // The New decision flow this picker started, the value it showed then, and whether it may still select.
  // Refs survive while the workflow's tab is hidden; only this picker holds the token, so other inspectors
  // never react. The flow belongs to this mounted picker: selecting another activity remounts it and ends
  // the flow by design, because the editor context does not identify the workflow to hand it back to.
  const handoff = useRef<{ token: string; baseline: string; armed: boolean } | null>(null);
  const [created, setCreated] = useState<NewDecisionResult | null>(null);
  const current = useRef({ selected, disabled, onChange });
  current.current = { selected, disabled, onChange };
  // The author chose a value themselves: the flow may no longer select, but its token stays so the next
  // New decision click still addresses the editor tab already open.
  const disarmHandoff = useCallback(() => { if (handoff.current) handoff.current.armed = false; }, []);
  /**
   * Opens the new-decision editor beside the workflow so its unsaved changes stay open. The workflow's
   * hidden tab re-runs its effects when shown again, which reloads this picker's list and takes the
   * decision saved there. A browser-tab fallback is another document, so it only refreshes the list.
   */
  const openNewDecision = useCallback(() => {
    // A flow whose editor has not saved keeps its token, so the same address focuses the editor tab already
    // open (and any work typed there) instead of opening a second, blank one.
    const previous = handoff.current;
    const token = previous && isNewDecisionHandoffPending(previous.token) ? previous.token : beginNewDecisionHandoff();
    if (previous && previous.token !== token) cancelNewDecisionHandoff(previous.token);
    handoff.current = { token, baseline: current.current.selected, armed: true };
    if (openStudioWorkspaceTab(qualifyStudioBackendPath(`/decisions?new=1&${newDecisionHandoffParameter}=${encodeURIComponent(token)}`))) return;
    cancelNewDecisionHandoff(token); handoff.current = null;
    window.open(qualifyStudioBackendPath("/decisions?new=1"), "_blank", "noopener");
  }, []);
  // Selects the decision saved from this picker's flow, unless the author picked something else meanwhile
  // or the inspector became read-only. Either way the flow ends once its editor has saved.
  const claimHandoff = useCallback(() => {
    const pending = handoff.current;
    const result = pending ? takeNewDecisionHandoff(pending.token) : null;
    if (!pending || !result) return;
    handoff.current = null;
    const latest = current.current;
    if (!pending.armed || latest.disabled || latest.selected !== pending.baseline) return;
    setCreated(result); setError("");
    latest.onChange(`${result.id}@latest`);
    // An action's result goes to the host's notifications (module UI guidance); its toast region also
    // announces it, which a region revealed together with the returning tab would not.
    api.notifications?.notify({ level: "success", title: "Decision selected", message: `${result.name} is now used by this Evaluate Decision activity.` });
  }, [api]);
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
  return <div className="decision-version-picker"><label>Decision<select aria-label="Business decision" disabled={disabled || loading} value={assetId} onChange={event => { disarmHandoff(); setError(""); onChange(event.target.value ? `${event.target.value}@latest` : ""); }}><option value="">Select a decision</option>{assetId && !assets.some(item => item.id === assetId) ? <option value={assetId}>{created?.id === assetId ? created.name : `Unavailable decision (${assetId})`}</option> : null}{assets.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
    {can(api, "decisions:manage") ? <StudioButton className="decision-new-shortcut" size="sm" disabled={disabled} title="Opens the decision editor in a new tab" onClick={openNewDecision}><Plus size={14} aria-hidden="true" /> New decision</StudioButton> : null}
    <label>Version<select aria-label="Business decision version" disabled={disabled || !assetId} value={selection} onFocus={() => { if (assetId) loadVersions(assetId, false); }} onChange={event => { disarmHandoff(); onChange(`${assetId}@${event.target.value}`); }}><option value="latest">Latest when workflow is published</option>{selection && selection !== "latest" && !choices.some(item => item.id === selection) ? <option value={selection}>Unavailable version ({selection})</option> : null}{choices.map(item => <option key={item.id} value={item.id}>Version {item.number}</option>)}</select></label>
    <p className="decision-muted">Workflow publication freezes the selected policy. Later changes do not switch an existing published workflow.</p>
    {unpublished ? <StudioAlert tone="warning">This decision has no published version. Publish it first; Review &amp; publish cannot pin a draft with @latest.</StudioAlert> : null}
    {error ? <p role="alert">{error}</p> : null}</div>;
}
