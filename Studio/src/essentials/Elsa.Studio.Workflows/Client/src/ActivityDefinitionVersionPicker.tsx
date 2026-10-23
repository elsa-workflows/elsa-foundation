import { useState } from "react";
import { ChevronLeft, ChevronRight } from "lucide-react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { useActivityDefinitionVersions, useFullActivityDefinitionVersion } from "./api/activityDesign";

export function ActivityDefinitionVersionPicker({
  context,
  definitionId,
  selectedVersionId,
  onChange
}: {
  context: StudioEndpointContext;
  definitionId: string;
  selectedVersionId: string;
  onChange(versionId: string): void;
}) {
  const [cursorStack, setCursorStack] = useState<(string | null)[]>([null]);
  const [cursorIndex, setCursorIndex] = useState(0);
  const query = useActivityDefinitionVersions(context, {
    definitionId,
    limit: 25,
    cursor: cursorStack[cursorIndex]
  });
  const selectedQuery = useFullActivityDefinitionVersion(context, selectedVersionId || null);
  const visible = query.data?.items ?? [];
  const selectedOnPage = visible.some(item => item.version.versionId === selectedVersionId);
  const selectedOption = !selectedOnPage && selectedQuery.data
    ? <option value={selectedQuery.data.versionId}>v{selectedQuery.data.version} · {selectedQuery.data.lifecycle} · exact selected version</option>
    : null;

  return <section className="ad-version-picker" aria-label="Exact immutable source version">
    <label className="ad-dialog-field"><span>Exact immutable version</span><select value={selectedVersionId} onChange={event => onChange(event.target.value)}><option value="" disabled>Select an exact visible version</option>{selectedOption}{visible.map(item => <option key={item.version.versionId} value={item.version.versionId}>v{item.version.version} · {item.version.lifecycle} · {item.providerKey}</option>)}</select></label>
    {query.isPending ? <div className="ad-inline-status" role="status">Loading a bounded version page…</div> : null}
    {query.isError ? <div className="ad-inline-error" role="alert">The bounded version page could not be confirmed.</div> : null}
    {query.isSuccess && visible.length === 0 && !selectedOption ? <div className="ad-inline-status" role="status">No authorized immutable versions are available to select.</div> : null}
    {query.data ? <div className="ad-inline-pager"><button type="button" onClick={() => setCursorIndex(current => Math.max(0, current - 1))} disabled={cursorIndex === 0}><ChevronLeft size={14} /> Previous versions</button><span>Page {cursorIndex + 1}</span><button type="button" onClick={() => { const continuation = query.data?.continuation; if (!continuation) return; setCursorStack(current => [...current.slice(0, cursorIndex + 1), continuation]); setCursorIndex(current => current + 1); }} disabled={!query.data.hasMore || !query.data.continuation}>Next versions <ChevronRight size={14} /></button></div> : null}
  </section>;
}
