import React, { useEffect, useMemo, useState } from "react";
import { useAuthSession, type ElsaStudioModuleApi, type StudioDashboardWidgetBodyProps } from "@elsa-workflows/studio-sdk";
import { attentionScopeKey, groupAttentionItems, type AttentionSnapshot } from "./attentionApi";
import { AttentionPreferenceStore, isSnoozed, snooze, type AttentionPreferences } from "./attentionPreferences";
type Filter = "all" | "critical" | "snoozed";
export function AttentionWidget({ api, snapshot, size }: { api: ElsaStudioModuleApi } & StudioDashboardWidgetBodyProps<AttentionSnapshot, unknown>) {
  const [filter, setFilter] = useState<Filter>("all"); const [preferences, setPreferences] = useState<AttentionPreferences>({ snoozes: [] });
  const [preferenceError, setPreferenceError] = useState<string | null>(null);
  const session = useAuthSession();
  const store = useMemo(() => new AttentionPreferenceStore(api, api.runtime.hostId ?? "default", attentionScopeKey(api, session.subject ?? "anonymous", session.tenantId ?? "default"), session.status !== "authenticated"), [api, session]);
  useEffect(() => { let current = true; setPreferenceError(null); void store.load().then(value => { if (current) setPreferences(value); }).catch(() => { if (current) setPreferenceError("Attention preferences are unavailable."); }); return () => { current = false; }; }, [store]);
  if (!snapshot) return <div role="status">Loading attention…</div>;
  const visible = snapshot.items
    .filter(item => filter === "snoozed" ? isSnoozed(item, preferences.snoozes) : !isSnoozed(item, preferences.snoozes) && (filter !== "critical" || item.severity === "critical"))
    .slice(0, size === "full" ? 20 : 5);
  const applySnooze = (items: typeof snapshot.items, duration: number) => { const keys = new Set(items.map(item => `${item.contributorId}:${item.id}`)); const next = { snoozes: [...preferences.snoozes.filter(value => !keys.has(`${value.contributorId}:${value.itemId}`)), ...items.map(item => snooze(item, duration))] }; setPreferences(next); setPreferenceError(null); void store.save(next).catch(() => setPreferenceError("Snooze could not be saved.")); };
  return <div className="attention-widget"><div role="group" aria-label="Attention filters">{(["all", "critical", "snoozed"] as Filter[]).map(value => <button type="button" aria-pressed={filter === value} key={value} onClick={() => setFilter(value)}>{value[0].toUpperCase() + value.slice(1)}</button>)}</div>
    {snapshot.failures.length ? <div role="alert">Dashboard data incomplete: {snapshot.failures.map(value => value.displayName).join(", ")}</div> : null}
    {preferenceError ? <div role="status">{preferenceError}</div> : null}
    {!visible.length ? <p>{filter === "snoozed" ? "No snoozed items." : snapshot.failures.length ? "No conditions were returned from the available sources; attention data is incomplete." : "All clear — no items need attention."}</p> : groupAttentionItems(visible).map(group => <section key={`${group[0].contributorId}:${group[0].id}`}><h4>{group[0].title}{group.length > 1 ? ` (${group.length} related)` : ""}</h4>{group.map(item => <article key={`${item.contributorId}:${item.id}`} data-severity={item.severity}><span>{item.contributorName}</span><p>{item.summary}</p><a href={item.destination.path}>{item.destination.label}</a></article>)}<label>Snooze <select defaultValue="" onChange={event => { if (event.target.value) applySnooze(group, Number(event.target.value)); }}><option value="">Choose…</option><option value="3600000">1 hour</option><option value="86400000">1 day</option><option value="604800000">1 week</option></select></label></section>)}</div>;
}
