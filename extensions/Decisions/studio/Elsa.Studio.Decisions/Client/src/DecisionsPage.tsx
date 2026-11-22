import React, { useCallback, useEffect, useId, useLayoutEffect, useMemo, useRef, useState } from "react";
import { Plus, Upload } from "lucide-react";
import { notifyStudioNavigationGuardReleased, qualifyStudioBackendPath, studioNavigationRequestedEvent, useStudioWorkspaceTitle, type ElsaStudioModuleApi, type StudioNavigationRequest } from "@elsa-workflows/studio-sdk";
import { StudioNewResourceDialog, StatusChip, PeerViewSwitch, studioRowInteraction, SkeletonRows, StudioAlert, StudioButton, StudioCollectionEmptyState, StudioCollectionToolbar, StudioPage, StudioPageHeader, StudioResultState, StudioSearchInput, StudioTableContainer, StudioField, OverflowMenu, StudioTabs, StudioTabPanel, StudioTimestamp, StudioShellBreadcrumbs } from "@elsa-workflows/studio-ui";
import { can, cloneVersion, createDecision, exportDecision, failureMessage, getDecision, isConflict, listDecisions, listRecycleBin, publishDecision, purgeDecision, recycleDecision, restoreDecision, saveDecision, unpublishDecision, validateDecision, version, versions, type DecisionAsset, type DecisionVersion, type Diagnostic } from "./decisionsApi";
import { decodeDecisionXml, newDecisionXml, parseTable } from "./decisionXml";
import { TableEditor } from "./TableEditor";
import { CasePanel } from "./CasePanel";
import { EvaluationPanel } from "./EvaluationPanel";
import { completeNewDecisionHandoff } from "./newDecisionHandoff";
export interface DecisionsPageProps { api: ElsaStudioModuleApi; navigate(path: string): void; assetId?: string | null; versionId?: string | null; isNew?: boolean; /** Set when an Evaluate Decision picker opened this editor and waits for the decision it creates. */ handoff?: string | null }
function diagnosticLocation(item: Diagnostic, table: ReturnType<typeof parseTable> | null) {
  const ruleIndex = item.ruleId && table ? table.rules.findIndex(rule => rule.id === item.ruleId) : -1;
  const clause = item.clauseId && table ? [...table.inputs, ...table.outputs].find(entry => entry.id === item.clauseId) : undefined;
  const locations = [item.ruleId ? ruleIndex >= 0 ? `Rule ${ruleIndex + 1}` : "Rule" : "", item.clauseId ? clause?.name ? `Column ${clause.name}` : "Column" : ""].filter(Boolean);
  return locations.length ? ` · ${locations.join(" · ")}` : "";
}
export function DecisionsPage({ api, navigate, assetId, versionId, isNew = false, handoff }: DecisionsPageProps) {
  const [assets, setAssets] = useState<DecisionAsset[]>([]), [asset, setAsset] = useState<DecisionAsset | null>(null), [published, setPublished] = useState<DecisionVersion | undefined>(), [availableVersions, setAvailableVersions] = useState<DecisionVersion[]>([]);
  const [metadataEntered, setMetadataEntered] = useState(false);
  const [name, setName] = useState(""), [description, setDescription] = useState(""), [xml, setXml] = useState(""), [sensitive, setSensitive] = useState(false);
  const [loading, setLoading] = useState(true), [busy, setBusy] = useState(false), [error, setError] = useState(""), [conflict, setConflict] = useState(false), [diagnostics, setDiagnostics] = useState<Diagnostic[]>([]), [search, setSearch] = useState("");
  const [recycleBin, setRecycleBin] = useState(false), [editorView, setEditorView] = useState<"rules" | "tests" | "settings">(isNew ? "settings" : "rules");
  const [caseDirty, setCaseDirty] = useState(false), [caseBusy, setCaseBusy] = useState(false), casePending = useRef(false);
  const reportCaseState = useCallback((changed: boolean, pending: boolean) => { setCaseDirty(changed); setCaseBusy(pending); casePending.current = pending; }, []);
  const generation = useRef(0), writing = useRef(false), initialXml = useRef(""), importFile = useRef<HTMLInputElement>(null), titleInput = useRef<HTMLInputElement>(null), heading = useRef<HTMLHeadingElement>(null), collectionCreateButton = useRef<HTMLButtonElement>(null), focusCollectionOnLoad = useRef(false), reviewFocus = useRef<Diagnostic | null>(null);
  const collectionTabsId = useId();
  const editorTabsId = useId();
  const editor = !!assetId || isNew, readOnly = !!published || !can(api, "decisions:manage") || (!!assetId && asset?.xml === null);
  const diagnosticTable = useMemo(() => { try { return xml ? parseTable(xml) : null; } catch { return null; } }, [xml]);
  const dirty = editor && !published && (isNew ? !!name || !!description || sensitive || xml !== "" && xml !== initialXml.current : !!asset && (name !== asset.name || description !== (asset.description ?? "") || xml !== (asset.xml ?? "") || sensitive !== asset.isSensitive));
  useLayoutEffect(() => { if (isNew && metadataEntered) titleInput.current?.focus(); }, [isNew, metadataEntered]);
  const dirtyRef = useRef(false); dirtyRef.current = dirty || caseDirty;
  // The tab names only what this route shows: the host keeps its route label while another decision or version is still loading,
  // and "Decision" marks one whose own load finished without it.
  const route = `${assetId ?? ""}|${versionId ?? ""}|${isNew ? "new" : "existing"}`, settledRoute = useRef<string | null>(null), shown = asset && asset.id === assetId && (published?.id ?? null) === (versionId ?? null) ? asset : null;
  const routePending = loading || settledRoute.current !== route;
  const editorTitle = isNew ? "New decision" : shown?.name ?? (routePending ? "Loading decision" : "Decision unavailable");
  const editorStatus = routePending || !isNew && !shown ? "" : published ? `Version ${published.number} · Read only` : !asset ? "Unsaved decision" : dirty ? "Unsaved changes" : "Saved draft";
  useEffect(() => { setEditorView(isNew ? "settings" : "rules"); }, [route, isNew]);
  useLayoutEffect(() => {
    if (editorView !== "rules" || !reviewFocus.current) return;
    const item = reviewFocus.current;
    const target = item.ruleId && item.clauseId ? document.getElementById(`cell-${item.ruleId}-${item.clauseId}`) : null;
    if (target) { target.focus(); reviewFocus.current = null; }
  }, [editorView, diagnostics]);
  useStudioWorkspaceTitle(!editor ? "Decisions" : isNew ? "New decision" : shown ? shown.name : !loading && settledRoute.current === route ? "Decision" : null, shown && !isNew ? published ? `Version ${published.number}` : "Draft" : null);
  useEffect(() => { if (!caseDirty && !caseBusy) notifyStudioNavigationGuardReleased(); }, [caseDirty, caseBusy]);
  const apply = useCallback((next: DecisionAsset, candidate?: DecisionVersion) => {
    setAsset(next); setPublished(candidate); setName(next.name); setDescription(next.description ?? ""); setSensitive(candidate?.isSensitive ?? next.isSensitive); setXml(candidate?.xml ?? (candidate ? "" : next.xml ?? "")); setConflict(false); setDiagnostics([]);
  }, []);
  const load = useCallback(async () => {
    const ticket = ++generation.current; writing.current = false; setBusy(false); setLoading(true); setError(""); setConflict(false); setAsset(null); setPublished(undefined); setAvailableVersions([]); setDiagnostics([]);
    try {
      if (isNew) { setMetadataEntered(false); const initial = newDecisionXml(); initialXml.current = initial; setName(""); setDescription(""); setSensitive(false); setXml(initial); }
      else if (assetId) {
        const next = await getDecision(api, assetId);
        const candidate = versionId ? await version(api, assetId, versionId) : undefined;
        const choices = await versions(api, assetId);
        if (ticket !== generation.current) return;
        apply(next, candidate); setAvailableVersions(choices);
      } else {
        const items = recycleBin ? await listRecycleBin(api) : await listDecisions(api); if (ticket !== generation.current) return; setAssets(items); setXml(""); setName("");
      }
    } catch (failure) { if (ticket === generation.current) setError(failureMessage(failure)); }
    finally {
      if (ticket === generation.current) {
        settledRoute.current = route;
        const previousFocus = document.activeElement;
        setLoading(false);
        const focusCollection = focusCollectionOnLoad.current && !editor;
        if (focusCollection) focusCollectionOnLoad.current = false;
        requestAnimationFrame(() => {
          if (ticket !== generation.current) return;
          const target = isNew ? titleInput.current : editor ? heading.current : focusCollection ? collectionCreateButton.current : null;
          if (!target?.isConnected) return;
          const active = target.ownerDocument.activeElement;
          // A later field selection belongs to the author, even before this frame runs.
          if (active && active !== target.ownerDocument.body && active !== previousFocus) return;
          target.focus();
        });
      }
    }
  }, [api, assetId, versionId, isNew, apply, editor, recycleBin, route]);
  const invalidate = useCallback(() => { generation.current += 1; }, []);
  // Retire old completions before the host hands notifications to the next visit.
  useLayoutEffect(() => invalidate, [load, invalidate]);
  useEffect(() => { void load(); }, [load]);
  const path = (id?: string, selectedVersion?: string) => qualifyStudioBackendPath(`/decisions${id ? `?decision=${encodeURIComponent(id)}${selectedVersion ? `&version=${encodeURIComponent(selectedVersion)}` : ""}` : ""}`);
  const savedNavigation = (target: string) => { writing.current = false; dirtyRef.current = false; notifyStudioNavigationGuardReleased(); navigate(target); };
  const leave = useCallback(async (target: string, onLeaving?: () => void) => {
    if (writing.current || casePending.current) return false;
    if (dirtyRef.current && !await api.dialogs.confirm({ title: "Unsaved decision", message: "Discard your unsaved changes and leave this decision?", confirmLabel: "Discard changes" })) return false;
    dirtyRef.current = false; notifyStudioNavigationGuardReleased(); onLeaving?.(); navigate(target); return true;
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
    if (writing.current) return; const ticket = generation.current; writing.current = true; setBusy(true); setError("");
    try { await action(ticket); } catch (failure) { if (ticket === generation.current) { setError(failureMessage(failure)); setConflict(editor && isConflict(failure)); } }
    finally { if (ticket === generation.current) { writing.current = false; setBusy(false); notifyStudioNavigationGuardReleased(); } }
  };
  const save = () => work(async ticket => {
    const input = { name: name.trim(), description, xml, isSensitive: sensitive };
    const next = asset ? await saveDecision(api, asset, input) : await createDecision(api, input);
    // The decision exists once the create returns, even if this tab was hidden meanwhile (which invalidates the ticket).
    if (!asset && isNew && handoff) completeNewDecisionHandoff(handoff, { id: next.id, name: next.name });
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
  const validate = () => work(async ticket => {
    if (!asset) return; const result = await validateDecision(api, asset);
    if (ticket === generation.current) { setDiagnostics(result.diagnostics); api.notifications?.notify({ level: result.success ? "success" : "warning", title: result.success ? "The saved draft is valid for the supported DMN profile." : "Validation found unsupported or invalid content." }); }
  });
  const publish = () => work(async ticket => {
    if (!asset) return; const result = await publishDecision(api, asset);
    if (ticket === generation.current) { api.notifications?.notify({ level: "success", title: `Version ${result.number} published` }); savedNavigation(path(asset.id, result.id)); }
  });
  const clonePublished = () => work(async ticket => {
    if (!asset || !published || !await api.dialogs.confirm({ title: "Clone version to draft", message: "Replace the current saved draft with this version? The published version remains immutable.", confirmLabel: "Clone to draft" })) return;
    const next = await cloneVersion(api, asset, published.id);
    if (ticket === generation.current) { apply(next); api.notifications?.notify({ level: "success", title: "Version copied to the draft" }); savedNavigation(path(next.id)); }
  });
  const unpublish = () => work(async ticket => {
    if (!asset || dirty || caseDirty || caseBusy || !await api.dialogs.confirm({ title: "Unpublish decision", message: "This removes the decision from new evaluations and workflow bindings. Existing published versions and already-frozen workflow executions remain unchanged.", confirmLabel: "Unpublish" })) return;
    await unpublishDecision(api, asset);
    if (ticket === generation.current) { api.notifications?.notify({ level: "success", title: "Decision unpublished" }); savedNavigation(path(asset.id)); }
  });
  const recycle = (target: DecisionAsset | null = asset, fromCollection = false) => work(async ticket => {
    if (!target || (!fromCollection && (dirty || caseDirty || caseBusy))) return;
    const confirmed = await api.dialogs.confirm({ title: "Move decision to recycle bin", message: `${fromCollection ? `Move ${target.name} to the recycle bin? ` : ""}The decision will no longer be available for new use. Its versions, saved cases and test history will be retained until permanent deletion. Existing frozen workflow executions remain unchanged.`, confirmLabel: "Move to recycle bin", tone: "danger" });
    if (!confirmed || ticket !== generation.current) return;
    if (fromCollection && (recycleBin || !assets.some(item => item.id === target.id && item.revision === target.revision))) return;
    await recycleDecision(api, target);
    if (ticket !== generation.current) return;
    if (fromCollection) {
      const items = await listDecisions(api);
      if (ticket !== generation.current) return;
      setAssets(items);
      api.notifications?.notify({ level: "success", title: "Decision moved to recycle bin" });
    } else {
      api.notifications?.notify({ level: "success", title: "Decision moved to recycle bin" }); savedNavigation(path());
    }
  });
  const restore = (item: DecisionAsset) => work(async ticket => {
    await restoreDecision(api, item);
    const items = await listRecycleBin(api);
    if (ticket === generation.current) { setAssets(items); api.notifications?.notify({ level: "success", title: `${item.name} was restored as an unpublished decision.` }); }
  });
  const purge = (item: DecisionAsset) => work(async ticket => {
    if (!await api.dialogs.confirm({ title: "Permanently delete decision", message: `This permanently deletes ${item.name}, its versions, saved cases and test history. Already-frozen workflow executions remain unchanged. This cannot be undone.`, confirmLabel: "Delete permanently", tone: "danger" })) return;
    await purgeDecision(api, item);
    const items = await listRecycleBin(api);
    if (ticket === generation.current) { setAssets(items); api.notifications?.notify({ level: "success", title: `${item.name} was permanently deleted.` }); }
  });
  const filtered = assets.filter(item => `${item.name} ${item.description ?? ""}`.toLocaleLowerCase().includes(search.trim().toLocaleLowerCase()));
  if (isNew && !metadataEntered) return <StudioPage className="decisions-page" aria-label="New decision">
    {can(api, "decisions:manage") ? <StudioNewResourceDialog title="New decision" onCancel={() => { focusCollectionOnLoad.current = true; navigate(path()); }}
      onCreate={metadata => { setName(metadata.name); setDescription(metadata.description); setMetadataEntered(true); }} />
      : <StudioResultState title="Decision creation unavailable" description="Your account does not have permission to create decisions." />}
  </StudioPage>;
  return <StudioPage className="decisions-page" aria-labelledby={editor ? "decision-title" : "decisions-title"}>
    {!editor ? <StudioPageHeader title="Decisions" titleId="decisions-title"
      description="Reusable business policies with versioned decision tables, saved cases and explanations."
      refresh={{ id: "decisions", label: "Refresh decisions", refreshing: loading, disabled: busy, autoRefresh: true, onRefresh: load }}
      actions={<>
        <StudioButton ref={collectionCreateButton} variant="primary" disabled={busy || !can(api, "decisions:manage")} onClick={() => navigate(qualifyStudioBackendPath("/decisions?new=1"))}><Plus size={15} /> New decision</StudioButton>
        <StudioButton disabled={busy || !can(api, "decisions:manage")} onClick={() => importFile.current?.click()}><Upload size={15} /> Import DMN</StudioButton>
      </>} /> : null}
    <input ref={importFile} hidden type="file" accept=".dmn,.xml,application/xml,text/xml" aria-label="Import DMN file" onChange={event => { const file = event.target.files?.[0]; event.target.value = ""; if (file) void importDocument(file); }} />
    {error ? <StudioAlert tone="danger">{error}</StudioAlert> : null}
    {conflict ? <div className="decision-conflict"><p>Your unsaved work is still here. Reload the current server draft or save your work as a separate decision.</p><div className="decision-actions"><StudioButton onClick={() => void api.dialogs.confirm({ title: "Reload decision", message: "Discard your unsaved changes and load the server draft?", confirmLabel: "Reload" }).then(confirmed => { if (confirmed) void load(); })}>Reload server draft</StudioButton><StudioButton disabled={busy} onClick={() => void work(async ticket => { const copy = await createDecision(api, { name: `${name.trim()} copy`, description, xml, isSensitive: sensitive }); if (ticket === generation.current) savedNavigation(path(copy.id)); })}>Save as a copy</StudioButton></div></div> : null}
    {!editor ? <>
      <div className="decision-collection-toolbar">
        <StudioCollectionToolbar><StudioSearchInput aria-label="Search decisions" value={search} onChange={event => setSearch(event.target.value)} placeholder="Search decisions" /></StudioCollectionToolbar>
        <PeerViewSwitch baseId={collectionTabsId} ariaLabel="Decision collections" views={[{ id: "decisions", label: "Decisions" }, { id: "recycle-bin", label: "Recycle bin" }]} value={recycleBin ? "recycle-bin" : "decisions"} onChange={id => { setSearch(""); setRecycleBin(id === "recycle-bin"); }} />
      </div>
      <StudioTabPanel baseId={collectionTabsId} index={0} hidden={recycleBin}>
        {loading ? <SkeletonRows label="Loading decisions" /> : !recycleBin ? !assets.length ? error ? <StudioResultState tone="danger" title="Decisions could not be loaded" description="Refresh after the backend or your access is restored." />
          : <StudioCollectionEmptyState title="No decisions yet" description="Create a table for eligibility, approval requirements, discounts or another reusable policy."
            actions={can(api, "decisions:manage") ? <StudioButton variant="primary" onClick={() => navigate(qualifyStudioBackendPath("/decisions?new=1"))}><Plus size={15} /> New decision</StudioButton> : undefined} />
          : !filtered.length ? error ? null : <StudioCollectionEmptyState title="No matching decisions" description="Try another name or clear your search." actions={<StudioButton onClick={() => setSearch("")}>Clear search</StudioButton>} />
            : <StudioTableContainer role="region" aria-label="Decisions table" tabIndex={0}>
              <table className="studio-table decision-assets"><thead><tr><th scope="col">Decision</th><th scope="col">Status</th><th scope="col">Updated</th><th scope="col">Actions</th></tr></thead>
                <tbody>{filtered.map(item => <tr key={item.id} {...studioRowInteraction<HTMLTableRowElement>(() => navigate(path(item.id)))}><th scope="row"><button className="decision-link studio-row-link" onClick={() => navigate(path(item.id))}>{item.name}</button>{item.description ? <p className="decision-muted">{item.description}</p> : null}</th>
                  <td>{item.publishedVersionId ? "Published · Draft available" : "Unpublished"}{item.isSensitive ? " · Protected" : ""}</td><td><StudioTimestamp value={item.updatedAt} fallback="—" invalidFallback="Invalid Date" /></td>
                  <td><OverflowMenu ariaLabel={`Actions for ${item.name}`} items={[{ id: "delete", label: "Move to recycle bin", destructive: true, disabled: busy || loading || !can(api, "decisions:manage"), onSelect: () => void recycle(item, true) }]} /></td></tr>)}</tbody>
              </table>
            </StudioTableContainer> : null}
      </StudioTabPanel>
      <StudioTabPanel baseId={collectionTabsId} index={1} hidden={!recycleBin}>
        {loading ? <SkeletonRows label="Loading recycled decisions" /> : recycleBin ? !assets.length ? error ? <StudioResultState tone="danger" title="Decisions could not be loaded" description="Refresh after the backend or your access is restored." />
          : <StudioCollectionEmptyState title="Recycle bin is empty" description="Recycled decisions stay here until restored or permanently deleted." />
          : !filtered.length ? error ? null : <StudioCollectionEmptyState title="No matching decisions" description="Try another name or clear your search." actions={<StudioButton onClick={() => setSearch("")}>Clear search</StudioButton>} />
            : <StudioTableContainer role="region" aria-label="Recycled decisions table" tabIndex={0}>
              <table className="studio-table decision-assets"><thead><tr><th scope="col">Decision</th><th scope="col">Recycled</th><th scope="col">Actions</th></tr></thead>
                <tbody>{filtered.map(item => <tr key={item.id}><th scope="row">{item.name}{item.description ? <p className="decision-muted">{item.description}</p> : null}</th>
                  <td><StudioTimestamp value={item.deletedAt} fallback="Recycled" invalidFallback="Invalid Date" /></td>
                  <td><OverflowMenu ariaLabel={`Actions for ${item.name}`} items={[
                    { id: "restore", label: "Restore", disabled: busy || !can(api, "decisions:manage"), onSelect: () => void restore(item) },
                    { id: "purge", label: "Delete permanently", destructive: true, disabled: busy || !can(api, "decisions:manage"), onSelect: () => void purge(item) }
                  ]} /></td></tr>)}</tbody>
              </table>
            </StudioTableContainer> : null}
      </StudioTabPanel>
    </> : <div className="decision-editor-workspace">
      <header className="decision-draft-bar decision-editor-header">
        <div className="decision-draft-context">
          <><StudioShellBreadcrumbs items={[{ href: path(), label: "Decisions" }]} current={editorTitle}
          renderLink={item => <a href={item.href} aria-disabled={busy || caseBusy || undefined} onClick={event => {
            if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
            event.preventDefault();
            void leave(item.href, () => { focusCollectionOnLoad.current = true; });
          }}>{item.label}</a>} />
          <span className="decision-editor-title">
            <h1 id="decision-title" ref={heading} tabIndex={-1} title={editorTitle}>{editorTitle}</h1>
            {editorStatus ? <StatusChip tone={dirty || isNew ? "warning" : "neutral"}>{editorStatus}</StatusChip> : null}
          </span></>
          {shown && availableVersions.length > 0 ? <fieldset className="decision-version-picker" disabled={busy} aria-label="Decision version">
            <PeerViewSwitch ariaLabel="Decision version" value={published?.id ?? "draft"} views={[
              { id: "draft", label: "Current draft" }, ...availableVersions.map(item => ({ id: item.id, label: `Version ${item.number}` }))
            ]} onChange={version => void leave(path(shown.id, version === "draft" ? undefined : version))} />
          </fieldset> : null}
        </div>
        {!routePending && (isNew || shown) ? <div className="decision-actions">
          {!published ? <StudioButton variant="primary" disabled={busy || caseBusy || caseDirty || readOnly || !name.trim() || !xml || !dirty && !!asset} onClick={() => void save()}>{busy ? "Working…" : "Save decision"}</StudioButton>
            : <StudioButton variant="primary" disabled={busy || caseBusy || caseDirty || !can(api, "decisions:manage") || !published.xml} onClick={() => void clonePublished()}>Clone to draft</StudioButton>}
          <OverflowMenu ariaLabel="More decision actions" items={[
            { id: "validate", label: "Validate", disabled: busy || !asset || dirty || !!published || !can(api, "decisions:manage"), onSelect: validate },
            ...(!published ? [{ id: "publish", label: "Publish", disabled: busy || caseBusy || caseDirty || !asset || dirty || !can(api, "decisions:publish"), onSelect: publish }] : []),
            { id: "export", label: "Export DMN", disabled: busy || !asset || !can(api, "decisions:source"), onSelect: download }
          ]} />
        </div> : null}
      </header>
      {routePending ? <SkeletonRows label="Loading decision" /> : !isNew && !shown ? <StudioResultState title="Unable to open this decision" description="The resource could not be loaded. It may have been recycled or your account may not have access." /> : <>
        <StudioTabs tabs={[{ id: "rules", label: "Rules" }, { id: "tests", label: "Tests" }, { id: "settings", label: "Settings" }]} activeTab={editorView} onSelect={tabId => setEditorView(tabId as "rules" | "tests" | "settings")} baseId={editorTabsId} ariaLabel="Decision views" className="decision-editor-tabs" />
        <StudioTabPanel baseId={editorTabsId} index={0} hidden={editorView !== "rules"}>
          {diagnostics.length ? <section aria-labelledby="decision-diagnostics-title" className="decision-diagnostics"><h2 id="decision-diagnostics-title">Compatibility and validation</h2><ul>{diagnostics.map((item, index) => <li key={`${item.code}-${index}`}><button className="decision-link" onClick={() => { const target = item.ruleId && item.clauseId ? document.getElementById(`cell-${item.ruleId}-${item.clauseId}`) : null; target?.focus(); }}><strong>{item.code}</strong> · {item.message}{diagnosticLocation(item, diagnosticTable)}</button></li>)}</ul><p>Retaining or viewing a document does not make unsupported content executable.</p></section> : null}
          {xml ? <TableEditor key={`${asset?.id ?? "new"}:${published?.id ?? "draft"}`} xml={xml} readOnly={readOnly || busy} diagnostics={diagnostics} onChange={value => { setXml(value); setError(""); setDiagnostics([]); }} onError={setError} /> : <StudioResultState title="Decision source is protected" description="Source access and any required sensitive-value grant are needed to inspect or edit this table." />}
        </StudioTabPanel>
        <StudioTabPanel baseId={editorTabsId} index={1} hidden={editorView !== "tests"}>
          {asset && published && xml && can(api, "decisions:evaluate") ? <EvaluationPanel key={published.id} api={api} id={asset.id} versionId={published.id} xml={xml} /> : null}
          {asset && can(api, "decisions:test") ? <CasePanel key={`${asset.id}:${published?.id ?? "draft"}`} api={api} asset={asset} candidate={published} xml={xml} dirty={dirty} onDraftStateChange={reportCaseState} onReviewRules={items => {
            setDiagnostics(items); reviewFocus.current = items.find(item => item.ruleId && item.clauseId) ?? null; setEditorView("rules");
          }} /> : <p className="decision-muted">Testing is not available with your current permissions.</p>}
        </StudioTabPanel>
        <StudioTabPanel baseId={editorTabsId} index={2} hidden={editorView !== "settings"}>
          <fieldset disabled={readOnly || busy} className="decision-metadata"><legend className="decision-visually-hidden">Decision details</legend>
            <StudioField label="Name"><input ref={titleInput} value={name} maxLength={200} onChange={event => setName(event.target.value)} /></StudioField>
            <StudioField label="Description"><input value={description} maxLength={2048} onChange={event => setDescription(event.target.value)} /></StudioField>
            <StudioField className="decision-checkbox" label="Protect decision source and values"><input type="checkbox" checked={sensitive} onChange={event => setSensitive(event.target.checked)} /></StudioField>
          </fieldset>
          {asset ? <section className="decision-lifecycle-settings" aria-labelledby="decision-availability-title"><h2 id="decision-availability-title">Availability</h2>
            <p className="decision-muted">{asset.publishedVersionId ? "Published versions are immutable. Unpublishing only affects new evaluations and workflow bindings." : "This decision is not available for new workflow bindings until you publish a version."}</p>
            <div className="decision-actions">
              {asset.publishedVersionId ? <StudioButton disabled={busy || dirty || caseBusy || caseDirty || !can(api, "decisions:publish")} onClick={() => void unpublish()}>Unpublish</StudioButton> : null}
              <StudioButton disabled={busy || dirty || caseBusy || caseDirty || !can(api, "decisions:manage")} onClick={() => void recycle()}>Move to recycle bin</StudioButton>
            </div>
          </section> : null}
        </StudioTabPanel>
      </>}
    </div>}
  </StudioPage>;
}
