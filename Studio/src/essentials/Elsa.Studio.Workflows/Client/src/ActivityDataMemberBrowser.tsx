import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { ChevronRight } from "lucide-react";
import { SkeletonRows, StudioField } from "@elsa-workflows/studio-ui";
import { matchesMemberOffer, type ActivityDataBinding, type ActivityDataContextResult, type ActivityDataSourceFacts, type ActivityDataTarget } from "./api/activityDataSelection";
import type { ActivityDataSelectionHost } from "./activityDataSelection";
import { activityDataBreadcrumbs, activityDataMemberBrowser, activityDataTypeLabel, appendActivityDataMember,
  describeActivityDataMemberFinding, groupActivityDataSources } from "./activityDataPickerModel";

interface Selection {
  source: ActivityDataBinding;
  sourceId?: string;
  label: string;
  path: string[];
  choose: boolean;
  documentRevision: string;
}
type Packet = { selection: Selection; result: ActivityDataContextResult; source: ActivityDataSourceFacts };

/** The member-enabled branch of the existing popover. Navigation is transient; only a fresh server offer commits. */
export function ActivityDataMemberBrowser({ host, target, result, current, refreshing, onSelect, onRefusal, onRefresh }: {
  host: ActivityDataSelectionHost; target: ActivityDataTarget; result: ActivityDataContextResult; current: boolean;
  refreshing: boolean; onSelect(binding: ActivityDataBinding): void;
  onRefusal(result: ActivityDataContextResult): void; onRefresh(): void;
}) {
  const [selection, setSelection] = useState<Selection | null>(null);
  const [packet, setPacket] = useState<Packet | null>(null);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [literal, setLiteral] = useState("");
  const rootRef = useRef<HTMLDivElement>(null);
  const latest = useRef({ host, target, current, onSelect, onRefusal, onRefresh });
  latest.current = { host, target, current, onSelect, onRefusal, onRefresh };
  const labels = { describeActivity: host.describeActivity, describeScope: (scope: string) => host.describeActivity(scope)?.label ?? null };
  const families = groupActivityDataSources(result, labels);

  const { documentRevision, baseRevision, requestSources } = host;
  const { nodeId, inputKey } = target;
  useEffect(() => {
    if (!selection) { setPacket(null); setError(null); setPending(false); return; }
    const controller = new AbortController();
    const revision = documentRevision;
    const base = baseRevision;
    setPending(true);
    setError(null);
    requestSources({ nodeId, inputKey }, controller.signal, 0, { source: selection.source, memberPath: selection.path, includeMemberShapes: true }).then(answer => {
      if (controller.signal.aborted) return;
      const now = latest.current;
      if (now.host.documentRevision !== revision || now.host.baseRevision !== base || now.target.nodeId !== nodeId || now.target.inputKey !== inputKey) return;
      setPending(false);
      if (answer.state !== "supported" && answer.state !== "empty") {
        setPacket(null);
        if (answer.state !== "invalid") { now.onRefusal(answer); return; }
        setError(answer.findings?.map(describeActivityDataMemberFinding).join(" ") || "This source cannot be used here.");
        return;
      }
      if (answer.memberReadsEnabled !== true) { setPacket(null); now.onRefresh(); return; }
      const offered = answer.sources.length === 1 ? answer.sources[0] : undefined;
      if (!offered || answer.documentRevision !== revision || answer.baseRevision !== base ||
        selection.sourceId && offered.id !== selection.sourceId || offered.binding &&
        !(selection.path.length ? matchesMemberOffer(selection.source, selection.path, offered.binding) : sameWholeSource(selection.source, offered.binding))) {
        setPacket(null); setError("The source changed while loading. Go back and choose it again."); return;
      }
      const next = { selection, result: answer, source: offered };
      setPacket(next);
      const model = activityDataMemberBrowser(answer, offered.id, selection.path, { describeActivity: now.host.describeActivity });
      if (selection.choose && selection.documentRevision === revision && now.current && offered.binding && !model.reason)
        now.onSelect(offered.binding);
    }, () => {
      if (!controller.signal.aborted) { setPending(false); setPacket(null); setError("Studio couldn't load the members. Try again."); }
    });
    return () => controller.abort();
  }, [selection, documentRevision, baseRevision, requestSources, nodeId, inputKey, result]);

  const focusedPath = useRef("");
  useLayoutEffect(() => {
    if (pending || selection && !packet) return;
    const frame = requestAnimationFrame(() => {
      const panel = rootRef.current;
      if (!panel) return;
      const pathKey = packet ? JSON.stringify([packet.source.id, packet.selection.path]) : "sources";
      if (focusedPath.current === pathKey && panel.contains(panel.ownerDocument.activeElement)) return;
      focusedPath.current = pathKey;
      (panel.querySelector<HTMLElement>("[role='option']:not([aria-disabled='true'])") ?? panel.querySelector<HTMLElement>("input:not(:disabled)") ?? panel.querySelector<HTMLElement>("button:not(:disabled)"))?.focus();
    });
    return () => cancelAnimationFrame(frame);
  }, [packet, pending, selection]);

  const open = (source: ActivityDataBinding, sourceId: string | undefined, label: string, path: string[], choose = false) => {
    setLiteral("");
    setSelection({ source, sourceId, label, path, choose, documentRevision: host.documentRevision });
  };
  const ready = packet && !pending && current && !refreshing && packet.result.documentRevision === host.documentRevision;
  const shown = packet?.selection ?? selection;
  const model = packet ? activityDataMemberBrowser(packet.result, packet.source.id, packet.selection.path, labels) : null;
  const canonicalRoot = packet?.selection.path.length === 0 && packet.source.binding ? packet.source.binding : selection?.source;
  const selectedId = packet?.source.id ?? selection?.sourceId;
  const changePath = (path: string[], choose = false) => {
    if (canonicalRoot && shown && current) open(canonicalRoot, selectedId, shown.label, path, choose);
  };

  return <div ref={rootRef} className="wf-data-members" aria-busy={pending || refreshing || undefined}>
    {result.target?.type ? <p className="wf-data-chooser-note">Input type: {activityDataTypeLabel(result.target.type)}</p> : null}
    {shown ? <>
      <div className="wf-data-member-heading">
        <button type="button" onClick={() => (selection?.path ?? shown.path).length ? changePath((selection?.path ?? shown.path).slice(0, -1)) : setSelection(null)}>Back</button>
        <strong>{shown.label}</strong>
      </div>
      <nav aria-label="Member path" className="wf-data-member-path">
        {activityDataBreadcrumbs(shown.path).map(crumb => <button key={crumb.key} type="button"
          aria-current={crumb.memberPath.length === shown.path.length ? "location" : undefined}
          disabled={!current || pending} onClick={() => changePath(crumb.memberPath)}>{crumb.label}</button>)}
      </nav>
      <button type="button" className="wf-data-member-use" disabled={!ready || !!model?.reason || !packet?.source.binding}
        onClick={() => changePath(shown.path, true)}>{shown.path.length ? "Use this value" : "Use whole value"}</button>
      {model?.typeLabel ? <p className="wf-data-chooser-note">{model.typeLabel}{model.typeKnowledge === "deferred" ? " · Checked when the workflow runs" : ""}</p> : null}
      {error ? <div className="wf-data-chooser-notice" role="alert"><span>{error}</span>
        <button type="button" onClick={() => selection && setSelection({ ...selection, choose: false })}>Retry</button></div> : null}
      {!packet && pending ? <SkeletonRows rows={3} label="Loading members" /> : null}
      {model ? <>
        {model.information ? <p className="wf-data-chooser-note" role={model.reason ? "status" : undefined}>{model.information}</p> : null}
        {model.members.length ? <div className="wf-data-chooser-list" role="listbox" aria-label="Members">
          {model.members.map(member => <button key={member.key} type="button" role="option" aria-selected={false} tabIndex={-1}
            aria-disabled={!ready || undefined} className="wf-data-chooser-option" onKeyDown={activateOption} onClick={() => {
              if (ready) changePath(member.memberPath, member.shape.kind === "scalar" || member.shape.kind === "collection");
            }}>
            <span className="wf-data-chooser-option-label">{member.serializedName === "" ? '"" (empty name)' : member.serializedName}</span>
            <span className="wf-data-chooser-option-type" title={activityDataTypeLabel(member.shape.type) ?? undefined}>{activityDataTypeLabel(member.shape.type)}</span>
            {member.shape.kind === "scalar" || member.shape.kind === "collection" ? null : <ChevronRight size={12} aria-hidden="true" />}
          </button>)}
        </div> : null}
        {model.allowLiteral ? <div className="wf-data-member-literal">
          <StudioField label="Member name"><input aria-label="Member name" value={literal} disabled={!ready}
            onChange={event => setLiteral(event.target.value)} onKeyDown={event => {
              if (event.key === "Enter") { event.preventDefault(); const path = appendActivityDataMember(shown.path, literal); if (ready && path) changePath(path); }
            }} /></StudioField>
          <button type="button" disabled={!ready || !appendActivityDataMember(shown.path, literal)}
            onClick={() => { const path = appendActivityDataMember(shown.path, literal); if (path) changePath(path); }}>
            {literal === "" ? "Open empty member" : "Open member"}
          </button>
        </div> : null}
      </> : null}
    </> : <>
      {families.map(family => <div key={family.family} className="wf-data-chooser-group" role="group" aria-label={family.label}>
        <div className="wf-data-chooser-group-label">{family.label}</div>
        <div role="listbox" aria-label={family.label}>
          {family.options.map(option => <button key={option.source.id} type="button" role="option" aria-selected={false} tabIndex={-1}
            className="wf-data-chooser-option" aria-disabled={!!option.unavailableReason || !current || undefined} onKeyDown={activateOption}
            onClick={() => {
              if (!current || option.unavailableReason || !option.source.binding) return;
              open(option.source.binding, option.source.id, option.label, [],
                option.source.shape?.kind === "scalar" || option.source.shape?.kind === "collection");
            }}>
            <span className="wf-data-chooser-option-label">{option.label}{option.disambiguation ? ` · ${option.disambiguation}` : ""}{option.shadowed ? " (outer scope)" : ""}</span>
            <span className="wf-data-chooser-option-type" title={option.typeLabel ?? undefined}>{option.typeLabel}</span>
            {option.unavailableReason ? <small>{option.unavailableReason}</small> : <ChevronRight size={12} aria-hidden="true" />}
          </button>)}
        </div>
      </div>)}
      {families.length === 0 ? <p className="wf-data-chooser-note">No earlier data is available for this input.</p> : null}
      {result.hasMoreSources ? <p className="wf-data-chooser-note">More sources exist than this browser can show.</p> : null}
    </>}
    {pending || refreshing ? <p className="wf-data-chooser-note" role="status">Updating for your latest changes…</p> : null}
  </div>;
}

function sameWholeSource(requested: ActivityDataBinding, offered: ActivityDataBinding) {
  if (requested.expressionType !== offered.expressionType || !requested.value || !offered.value || typeof requested.value !== "object" || typeof offered.value !== "object") return false;
  const a = requested.value as Record<string, unknown>, b = offered.value as Record<string, unknown>;
  switch (requested.expressionType) {
    case "ActivityResult": return a.producerNodeId === b.producerNodeId && a.projectionKey === b.projectionKey && (a.producerScopeId ?? null) === (b.producerScopeId ?? null) && (a.isOptional === true) === (b.isOptional === true);
    case "Variable": return a.referenceKey === b.referenceKey && a.declaringScopeId === b.declaringScopeId;
    case "Input": return a.referenceKey === b.referenceKey;
    case "WorkflowRequest": return a.memberKey === b.memberKey && (a.path ?? null) === (b.path ?? null);
    default: return false;
  }
}

function activateOption(event: import("react").KeyboardEvent<HTMLButtonElement>) {
  if (event.key === "Enter" || event.key === " ") { event.preventDefault(); event.currentTarget.click(); }
}
