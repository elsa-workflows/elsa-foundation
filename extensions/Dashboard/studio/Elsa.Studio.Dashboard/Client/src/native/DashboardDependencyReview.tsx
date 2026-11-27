import React, { useEffect, useState } from "react";
import { StudioButton } from "@elsa-workflows/studio-ui";
import type { DashboardApi } from "./api";
import { accessLost, resourcePath, type AudienceGrant, type DashboardResource } from "./types";
import { useRequestLifetime } from "./useRequestLifetime";
import "./DashboardDependencyReview.css";

type Dependency = { positions: number[]; question?: DashboardResource; audience?: AudienceGrant[] };

/** Reviews saved references only. Separate question copies never replace a dashboard binding. */
export function DashboardDependencyReview({ api, resource, mode, onReadyChange }: {
  api: DashboardApi; resource: DashboardResource; mode: "share" | "copy"; onReadyChange(ready: boolean): void;
}) {
  const lifetime = useRequestLifetime();
  const [dependencies, setDependencies] = useState<Dependency[]>();
  const [failed, setFailed] = useState(false);
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    const request = lifetime.begin("dependencies");
    onReadyChange(false); setDependencies(undefined); setFailed(false);
    void (async () => {
      try {
        const current = await api.get("dashboard", resource.id, request.signal);
        if (current.revision !== resource.revision) throw new Error("Dashboard changed");
        const grouped = new Map<string, { positions: number[]; questionId?: string }>();
        for (const [index, tile] of (current.tiles ?? []).entries()) {
          const questionId = tile.referenceState !== "unavailable" ? tile.questionId : undefined;
          const key = questionId ? `question:${questionId}` : `unavailable:${tile.id}`;
          const entry = grouped.get(key) ?? { positions: [], questionId };
          entry.positions.push(index + 1); grouped.set(key, entry);
        }
        const reviewed = await Promise.all([...grouped.values()].map(async (entry): Promise<Dependency> => {
          if (!entry.questionId) return { positions: entry.positions };
          try {
            const question = await api.get("question", entry.questionId, request.signal);
            if (!question.capabilities.canRead || question.definitionState === "withheld") return { positions: entry.positions };
            // Audience identities are owner-only. Non-owners receive no grant lookup or claims
            // about whether another recipient can inspect this question or its source data.
            const audience = question.capabilities.canShare ? (await api.audience(question, request.signal)).items : undefined;
            return { positions: entry.positions, question, audience };
          } catch (cause) {
            if (accessLost(cause)) return { positions: entry.positions };
            throw cause;
          }
        }));
        if (!request.current()) return;
        setDependencies(reviewed); onReadyChange(true);
      } catch {
        if (request.current()) { setFailed(true); onReadyChange(false); }
      } finally { request.finish(); }
    })();
    return () => { lifetime.cancel("dependencies"); onReadyChange(false); };
  }, [api, resource.id, resource.revision, lifetime, attempt, onReadyChange]);
  return <section aria-label="Included question access review">
    <h4>Included questions</h4>
    <p>{mode === "copy" ? "The private dashboard copy will retain these saved question references. It will not copy the questions." : "Sharing this dashboard retains these question references. It does not change their audiences."} Each viewer needs separate question and source-data access. This review does not establish a recipient's data permissions.</p>
    {failed ? <><p role="alert">Included questions could not be reviewed. The dashboard may have changed or current access could not be established. Retry, or close this dialog and reload the dashboard.</p><StudioButton type="button" onClick={() => setAttempt(value => value + 1)}>Retry included question review</StudioButton></> : dependencies === undefined ? <p role="status">Reviewing current included questions…</p> : dependencies.length ? <ul className="named-picker-list">{dependencies.map((dependency, index) => <li key={index}>
      {dependency.question ? <div className="named-dependency-details"><p><strong>{dependency.question.name}</strong> — {dependency.positions.length === 1 ? "Tile" : "Tiles"} {dependency.positions.join(", ")}</p>
        {dependency.audience ? <p>{dependency.audience.length ? `Current question audience: ${dependency.audience.filter(grant => grant.kind === "user").length} user grants and ${dependency.audience.filter(grant => grant.kind === "role").length} role grants.` : "This question is personal; it has no audience grants."} Dashboard sharing does not add these grants.</p> : <p>Only the question's owner can review or change its audience.</p>}
        {dependency.question.capabilities.canShare ? <p><a href={resourcePath("question", dependency.question.id)} target="_blank" rel="noopener noreferrer">Open {dependency.question.name} to review sharing (new tab)</a></p> : null}
        {dependency.question.capabilities.canCopy ? <IndependentQuestionCopy api={api} question={dependency.question} /> : null}
      </div> : <p className="named-dependency-details">Unavailable question — {dependency.positions.length === 1 ? "Tile" : "Tiles"} {dependency.positions.join(", ")}. Its reference is retained without disclosing its identity or settings.</p>}
    </li>)}</ul> : <p>No saved questions are included in this dashboard.</p>}
    <p>Later question changes apply when refreshed; revoked or deleted questions can make retained tiles unavailable. Copying a question separately creates an independent private resource and leaves these dashboard references unchanged.</p>
  </section>;
}

function IndependentQuestionCopy({ api, question }: { api: DashboardApi; question: DashboardResource }) {
  const lifetime = useRequestLifetime();
  const [editing, setEditing] = useState(false);
  const [name, setName] = useState(`${question.name} copy`.slice(0, 200));
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);
  const [copied, setCopied] = useState<DashboardResource>();
  const [keys] = useState(() => new Map<string, string>());
  const copy = async () => {
    if (busy || !name.trim()) return;
    const request = lifetime.begin("copy"); setBusy(true); setFailed(false);
    const acceptedName = name.trim(); let key = keys.get(acceptedName);
    if (!key) { key = crypto.randomUUID(); keys.set(acceptedName, key); }
    try { const value = await api.copy(question, acceptedName, key, request.signal); if (request.current()) { setCopied(value); setEditing(false); } }
    catch { if (request.current()) setFailed(true); }
    finally { if (request.current()) setBusy(false); request.finish(); }
  };
  return <div>{copied ? <p role="status">Private question created: <a href={resourcePath("question", copied.id)} target="_blank" rel="noopener noreferrer">{copied.name} (new tab)</a>. This dashboard still references the original question.</p> : null}
    {editing ? <form onSubmit={event => { event.preventDefault(); void copy(); }}><label>Name for private copy of {question.name}<input maxLength={200} value={name} disabled={busy} onChange={event => setName(event.target.value)} /></label><div className="named-actions"><StudioButton type="submit" disabled={busy || !name.trim()}>{busy ? "Copying question…" : "Create independent private question"}</StudioButton><StudioButton type="button" disabled={busy} onClick={() => setEditing(false)}>Cancel question copy</StudioButton></div>{failed ? <p role="alert">The question copy could not be confirmed. Retry with the same name, or reload this review if its definition or access changed. The dashboard has not been changed.</p> : null}</form> : <StudioButton type="button" onClick={() => { setCopied(undefined); setEditing(true); }}>Copy {question.name} as an independent question</StudioButton>}
  </div>;
}
