import React, { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { Plus, RefreshCw, Upload } from "lucide-react";
import { notifyStudioNavigationGuardReleased, qualifyStudioBackendPath, studioNavigationRequestedEvent, type ElsaStudioModuleApi, type StudioNavigationRequest } from "@elsa-workflows/studio-sdk";
import { SkeletonRows, StudioAlert, StudioButton, StudioCollectionToolbar, StudioPage, StudioPageHeader, StudioResultState, StudioSearchInput, StudioTableContainer } from "@elsa-workflows/studio-ui";
import { can, cloneVersion, createDecision, exportDecision, failureMessage, getDecision, isConflict, listDecisions, publishDecision, saveDecision, validateDecision, version, versions, type DecisionAsset, type DecisionVersion, type Diagnostic } from "./decisionsApi";
import { decodeDecisionXml, newDecisionXml } from "./decisionXml";
import { TableEditor } from "./TableEditor";
import { CasePanel } from "./CasePanel";
import { EvaluationPanel } from "./EvaluationPanel";
export interface DecisionsPageProps { api: ElsaStudioModuleApi; navigate(path: string): void; assetId?: string | null; versionId?: string | null; isNew?: boolean }
export function DecisionsPage({ api, navigate, assetId, versionId, isNew = false }: DecisionsPageProps) {
  const [assets, setAssets] = useState<DecisionAsset[]>([]), [asset, setAsset] = useState<DecisionAsset | null>(null), [published, setPublished] = useState<DecisionVersion | undefined>(), [availableVersions, setAvailableVersions] = useState<DecisionVersion[]>([]);
  const [name, setName] = useState(""), [description, setDescription] = useState(""), [xml, setXml] = useState(""), [sensitive, setSensitive] = useState(false);
  const [loading, setLoading] = useState(true), [busy, setBusy] = useState(false), [error, setError] = useState(""), [notice, setNotice] = useState(""), [conflict, setConflict] = useState(false), [diagnostics, setDiagnostics] = useState<Diagnostic[]>([]), [search, setSearch] = useState("");
  const [caseDirty, setCaseDirty] = useState(false), [caseBusy, setCaseBusy] = useState(false), casePending = useRef(false);
  const reportCaseState = useCallback((changed: boolean, pending: boolean) => { setCaseDirty(changed); setCaseBusy(pending); casePending.current = pending; }, []);
  const generation = useRef(0), writing = useRef(false), initialXml = useRef(""), importFile = useRef<HTMLInputElement>(null), titleInput = useRef<HTMLInputElement>(null), heading = useRef<HTMLHeadingElement>(null), newButton = useRef<HTMLButtonElement>(null);
  const editor = !!assetId || isNew, readOnly = !!published || !can(api, "decisions:manage") || (!!assetId && asset?.xml === null);
  const dirty = editor && !published && (isNew ? !!name || !!description || sensitive || xml !== "" && xml !== initialXml.current : !!asset && (name !== asset.name || description !== (asset.description ?? "") || xml !== (asset.xml ?? "") || sensitive !== asset.isSensitive));
  const dirtyRef = useRef(false); dirtyRef.current = dirty || caseDirty;
  useEffect(() => { if (!caseDirty && !caseBusy) notifyStudioNavigationGuardReleased(); }, [caseDirty, caseBusy]);
  const apply = useCallback((next: DecisionAsset, candidate?: DecisionVersion) => {
    setAsset(next); setPublished(candidate); setName(next.name); setDescription(next.description ?? ""); setSensitive(candidate?.isSensitive ?? next.isSensitive); setXml(candidate?.xml ?? (candidate ? "" : next.xml ?? "")); setConflict(false); setDiagnostics([]);
  }, []);
  const load = useCallback(async () => {
    const ticket = ++generation.current; setLoading(true); setError(""); setNotice(""); setConflict(false); setAsset(null); setPublished(undefined); setAvailableVersions([]); setDiagnostics([]);
    try {
      if (isNew) { const initial = newDecisionXml(); initialXml.current = initial; setName(""); setDescription(""); setSensitive(false); setXml(initial); }
      else if (assetId) {
        const next = await getDecision(api, assetId);
        const candidate = versionId ? await version(api, assetId, versionId) : undefined;
        const choices = await versions(api, assetId);
        if (ticket !== generation.current) return;
        apply(next, candidate); setAvailableVersions(choices);
      } else {
        const items = await listDecisions(api); if (ticket !== generation.current) return; setAssets(items); setXml(""); setName("");
      }
    } catch (failure) { if (ticket === generation.current) setError(failureMessage(failure)); }
    finally { if (ticket === generation.current) { setLoading(false); requestAnimationFrame(() => isNew ? titleInput.current?.focus() : editor ? heading.current?.focus() : newButton.current?.focus()); } }
  }, [api, assetId, versionId, isNew, apply, editor]);
  const invalidate = useCallback(() => { generation.current += 1; }, []);
  useLayoutEffect(() => { void load(); return invalidate; }, [load, invalidate]);
  const path = (id?: string, selectedVersion?: string) => qualifyStudioBackendPath(`/decisions${id ? `?decision=${encodeURIComponent(id)}${selectedVersion ? `&version=${encodeURIComponent(selectedVersion)}` : ""}` : ""}`);
  const savedNavigation = (target: string) => { writing.current = false; dirtyRef.current = false; notifyStudioNavigationGuardReleased(); navigate(target); };
  const leave = useCallback(async (target: string) => {
    if (writing.current || casePending.current) return;
    if (dirtyRef.current && !await api.dialogs.confirm({ title: "Unsaved decision", message: "Discard your unsaved changes and leave this decision?", confirmLabel: "Discard changes" })) return;
    dirtyRef.current = false; notifyStudioNavigationGuardReleased(); navigate(target);
  }, [api, navigate]);
  useEffect(() => {
    const beforeUnload = (event: BeforeUnloadEvent) => { if (dirtyRef.current || writing.current || casePending.current) { event.preventDefault(); event.returnValue = ""; } };
    const guard = (event: Event) => {
      const request = event as CustomEvent<StudioNavigationRequest>;
      if (!dirtyRef.current && !writing.current && !casePending.current) return;
      request.preventDefault();
      if (!request.detail || request.detail.intent === "probe" || request.detail.intent === "close") return;
      if (!writing.current && !casePending.current) void leave(request.detail.toPath);
    };
    window.addEventListener("beforeunload", beforeUnload); window.addEventListener(studioNavigationRequestedEvent, guard);
    return () => { window.removeEventListener("beforeunload", beforeUnload); window.removeEventListener(studioNavigationRequestedEvent, guard); };
  }, [leave]);
  const work = async (action: (ticket: number) => Promise<void>) => {
    if (writing.current) return; const ticket = generation.current; writing.current = true; setBusy(true); setError(""); setNotice("");
    try { await action(ticket); } catch (failure) { if (ticket === generation.current) { setError(failureMessage(failure)); setConflict(isConflict(failure)); } }
    finally { writing.current = false; if (ticket === generation.current) setBusy(false); notifyStudioNavigationGuardReleased(); }
  };
  const save = () => work(async ticket => {
    const input = { name: name.trim(), description, xml, isSensitive: sensitive };
    const next = asset ? await saveDecision(api, asset, input) : await createDecision(api, input);
    if (ticket !== generation.current) return; apply(next); dirtyRef.current = caseDirty; api.notifications?.notify({ level: "success", title: "Decision saved" });
    if (!asset) savedNavigation(path(next.id));
  });
  const importDocument = async (file: File) => {
    if (file.size > 1024 * 1024) { setError("Decision documents must be at most 1 MiB."); return; }
    const importedName = await api.dialogs.prompt({ title: "Import decision", message: "Name this imported decision. Unsupported documents can be retained without becoming executable.", defaultValue: file.name.replace(/\.dmn$/i, ""), confirmLabel: "Import" });
    if (!importedName?.trim()) return;
    await work(async ticket => { const importedXml = decodeDecisionXml(await file.arrayBuffer()); const next = await createDecision(api, { name: importedName.trim(), xml: importedXml, isSensitive: false }); if (ticket === generation.current) savedNavigation(path(next.id)); });
  };
  const download = () => work(async ticket => {
    if (!asset) return; const source = await exportDecision(api, asset.id, published?.id); if (ticket !== generation.current) return;
    const url = URL.createObjectURL(new Blob([source], { type: "application/xml" })); const anchor = document.createElement("a"); anchor.href = url; anchor.download = `${asset.name.replace(/[^a-zA-Z0-9_-]/g, "-") || "decision"}.dmn`; anchor.click(); URL.revokeObjectURL(url);
  });
  const filtered = assets.filter(item => `${item.name} ${item.description ?? ""}`.toLocaleLowerCase().includes(search.trim().toLocaleLowerCase()));
  return <StudioPage className="decisions-page" aria-labelledby="decisions-title">
    <StudioPageHeader title={editor ? published ? `${asset?.name ?? "Decision"} · Version ${published.number}` : isNew ? "New decision" : asset?.name ?? "Decision" : "Decisions"} titleId="decisions-title" description={editor ? published ? "Published content is immutable. Clone this version to start an explicit draft change." : "Maintain a business policy independently of the workflow that uses its result." : "Reusable business policies with versioned decision tables, saved cases and explanations."} actions={editor ? <StudioButton disabled={busy || caseBusy} onClick={() => void leave(path())}>Back to decisions</StudioButton> : <>
      <StudioButton ref={newButton} variant="primary" disabled={busy || !can(api, "decisions:manage")} onClick={() => navigate(qualifyStudioBackendPath("/decisions?new=1"))}><Plus size={15} /> New decision</StudioButton><StudioButton disabled={busy || !can(api, "decisions:manage")} onClick={() => importFile.current?.click()}><Upload size={15} /> Import DMN</StudioButton><StudioButton disabled={busy || loading} onClick={() => void load()}><RefreshCw size={15} /> Refresh</StudioButton>
    </>} />
    <input ref={importFile} hidden type="file" accept=".dmn,.xml,application/xml,text/xml" aria-label="Import DMN file" onChange={event => { const file = event.target.files?.[0]; event.target.value = ""; if (file) void importDocument(file); }} />
    {error ? <StudioAlert tone="danger">{error}</StudioAlert> : null}{notice ? <StudioAlert tone="success">{notice}</StudioAlert> : null}
    {conflict ? <div className="decision-conflict"><p>Your unsaved work is still here. Reload the current server draft or save your work as a separate decision.</p><div className="decision-actions"><StudioButton onClick={() => void api.dialogs.confirm({ title: "Reload decision", message: "Discard your unsaved changes and load the server draft?", confirmLabel: "Reload" }).then(confirmed => { if (confirmed) void load(); })}>Reload server draft</StudioButton><StudioButton disabled={busy} onClick={() => void work(async ticket => { const copy = await createDecision(api, { name: `${name.trim()} copy`, description, xml, isSensitive: sensitive }); if (ticket === generation.current) savedNavigation(path(copy.id)); })}>Save as a copy</StudioButton></div></div> : null}
    {loading ? <SkeletonRows label="Loading decisions" /> : !editor ? <>
      <StudioCollectionToolbar><StudioSearchInput aria-label="Search decisions" value={search} onChange={event => setSearch(event.target.value)} placeholder="Search decisions" /></StudioCollectionToolbar>
      {!assets.length ? <StudioResultState title={error ? "Decisions could not be loaded" : "No decisions yet"} description={error ? "Refresh after the backend or your access is restored." : "Create a table for eligibility, approval requirements, discounts or another reusable policy."} /> : !filtered.length ? <StudioResultState title="No matching decisions" description="Try another name or clear your search." actions={<StudioButton onClick={() => setSearch("")}>Clear search</StudioButton>} /> : <StudioTableContainer><table className="decision-assets"><thead><tr><th scope="col">Decision</th><th scope="col">Status</th><th scope="col">Updated</th></tr></thead><tbody>{filtered.map(item => <tr key={item.id}><th scope="row"><button className="decision-link" onClick={() => navigate(path(item.id))}>{item.name}</button>{item.description ? <p className="decision-muted">{item.description}</p> : null}</th><td>{item.publishedVersionId ? "Published · Draft available" : "Draft"}{item.isSensitive ? " · Protected" : ""}</td><td>{new Date(item.updatedAt).toLocaleDateString()}</td></tr>)}</tbody></table></StudioTableContainer>}
    </> : (!isNew && !asset) ? <StudioResultState title="Decision unavailable" description="The resource could not be loaded. Your account may not have access." /> : <>
      <h2 ref={heading} tabIndex={-1} className="decision-visually-hidden">{published ? "Published decision" : "Decision draft"}</h2>
      <div className="decision-draft-bar"><span>{published ? `Version ${published.number} · Read only` : !asset ? "Unsaved decision" : dirty ? "Unsaved changes" : "Saved draft"}</span>{asset ? <label>View<select aria-label="Decision version" value={published?.id ?? "draft"} disabled={busy} onChange={event => void leave(path(asset.id, event.target.value === "draft" ? undefined : event.target.value))}><option value="draft">Current draft</option>{availableVersions.map(item => <option key={item.id} value={item.id}>Version {item.number}</option>)}</select></label> : null}<div className="decision-actions">
        {!published ? <StudioButton variant="primary" disabled={busy || readOnly || !name.trim() || !xml || !dirty && !!asset} onClick={() => void save()}>{busy ? "Working…" : "Save decision"}</StudioButton> : <StudioButton variant="primary" disabled={busy || caseBusy || caseDirty || !can(api, "decisions:manage") || !published.xml} onClick={() => void work(async ticket => { if (!asset || !await api.dialogs.confirm({ title: "Clone version to draft", message: "Replace the current saved draft with this version? The published version remains immutable.", confirmLabel: "Clone to draft" })) return; const next = await cloneVersion(api, asset, published.id); if (ticket === generation.current) { apply(next); api.notifications?.notify({ level: "success", title: "Version copied to the draft" }); savedNavigation(path(next.id)); } })}>Clone to draft</StudioButton>}
        <StudioButton disabled={busy || !asset || dirty || !!published || !can(api, "decisions:manage")} onClick={() => void work(async ticket => { if (!asset) return; const result = await validateDecision(api, asset); if (ticket === generation.current) { setDiagnostics(result.diagnostics); setNotice(result.success ? "The saved draft is valid for the supported DMN profile." : "Validation found unsupported or invalid content."); } })}>Validate</StudioButton>
        {!published ? <StudioButton disabled={busy || caseBusy || caseDirty || !asset || dirty || !can(api, "decisions:publish")} onClick={() => void work(async ticket => { if (!asset) return; const result = await publishDecision(api, asset); if (ticket === generation.current) { api.notifications?.notify({ level: "success", title: `Version ${result.number} published` }); savedNavigation(path(asset.id, result.id)); } })}>Publish</StudioButton> : null}
        <StudioButton disabled={busy || !asset || !can(api, "decisions:source")} onClick={() => void download()}>Export DMN</StudioButton>
      </div></div>
      <fieldset disabled={readOnly || busy} className="decision-metadata"><legend className="decision-visually-hidden">Decision details</legend><label>Name<input ref={titleInput} value={name} maxLength={200} onChange={event => setName(event.target.value)} /></label><label>Description<input value={description} maxLength={2048} onChange={event => setDescription(event.target.value)} /></label><label className="decision-checkbox"><input type="checkbox" checked={sensitive} onChange={event => setSensitive(event.target.checked)} /> Protect decision source and values</label></fieldset>
      {diagnostics.length ? <section aria-labelledby="decision-diagnostics-title" className="decision-diagnostics"><h2 id="decision-diagnostics-title">Compatibility and validation</h2><ul>{diagnostics.map((item, index) => <li key={`${item.code}-${index}`}><button className="decision-link" onClick={() => { const target = item.ruleId && item.clauseId ? document.getElementById(`cell-${item.ruleId}-${item.clauseId}`) : null; target?.focus(); }}><strong>{item.code}</strong> · {item.message}{item.ruleId ? ` · Rule ${item.ruleId}` : ""}{item.clauseId ? ` · Column ${item.clauseId}` : ""}</button></li>)}</ul><p>Retaining or viewing a document does not make unsupported content executable.</p></section> : null}
      {xml ? <TableEditor xml={xml} readOnly={readOnly || busy} onChange={setXml} onError={setError} /> : <StudioResultState title="Decision source is protected" description="Source access and any required sensitive-value grant are needed to inspect or edit this table." />}
      {asset && published && xml && can(api, "decisions:evaluate") ? <EvaluationPanel key={published.id} api={api} id={asset.id} versionId={published.id} xml={xml} /> : null}
      {asset && can(api, "decisions:test") ? <CasePanel key={`${asset.id}:${published?.id ?? "draft"}`} api={api} asset={asset} candidate={published} xml={xml} dirty={dirty} onDraftStateChange={reportCaseState} /> : null}
    </>}
  </StudioPage>;
}
