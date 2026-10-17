import React, { useEffect, useId, useMemo, useRef, useState } from "react";
import type { StudioEndpointContext, StudioExpressionEditorProps } from "@elsa-workflows/studio-sdk";
import { pickConnections } from "./connectionsApi";
import type { ConnectionMetadata, ConnectionReference } from "./connectionTypes";

type LoadState =
  | { endpointContext: StudioEndpointContext; status: "loading"; items: ConnectionMetadata[] }
  | { endpointContext: StudioEndpointContext; status: "ready"; items: ConnectionMetadata[] }
  | { endpointContext: StudioEndpointContext; status: "error"; items: ConnectionMetadata[] };

export function ConnectionPickerEditor({
  value,
  disabled,
  initialFocus,
  onChange,
  descriptor,
  endpointContext
}: StudioExpressionEditorProps & { endpointContext: StudioEndpointContext }) {
  const requiredType = descriptor.connectionType ?? undefined;
  const [loadAttempt, setLoadAttempt] = useState(0);
  const [state, setState] = useState<LoadState>({ endpointContext, status: "loading", items: [] });
  const visibleState: LoadState = state.endpointContext === endpointContext
    ? state
    : { endpointContext, status: "loading", items: [] };
  const selectRef = useRef<HTMLSelectElement>(null);
  const focusPendingRef = useRef(initialFocus === true);
  const selectId = useId();
  const statusId = useId();
  const reference = useMemo(() => toReference(value), [value]);
  const selected = reference ? visibleState.items.find(item => item.name === reference.connection) : undefined;
  const currentValue = selected?.name ?? (reference ? "__current-reference" : "");
  const needsAuthorization = selected?.status === "NeedsAuthorization";

  useEffect(() => {
    let active = true;
    setState({ endpointContext, status: "loading", items: [] });
    void pickConnections(endpointContext, { typeName: requiredType }).then(response => {
      if (!active) return;
      setState({ endpointContext, status: "ready", items: response.items });
    }).catch(() => {
      if (!active) return;
      setState({ endpointContext, status: "error", items: [] });
    });
    return () => {
      active = false;
    };
  }, [endpointContext, loadAttempt, requiredType]);

  useEffect(() => {
    if (initialFocus) focusPendingRef.current = true;
    if (!focusPendingRef.current || visibleState.status !== "ready" || disabled) return;
    selectRef.current?.focus();
    focusPendingRef.current = false;
  }, [disabled, initialFocus, visibleState.status]);

  const unavailable = visibleState.status === "ready" && !!reference && !selected;
  const unresolvedReference = !!reference && !selected;
  const selectionDisabled = !!disabled || visibleState.status !== "ready";

  return (
    <div className="connection-picker-editor">
      <label htmlFor={selectId}>Connection</label>
      <select
        ref={selectRef}
        id={selectId}
        aria-describedby={statusId}
        disabled={selectionDisabled}
        value={currentValue}
        onChange={event => {
          const next = visibleState.items.find(item => item.name === event.target.value);
          onChange(next ? toOpaqueReference(next, requiredType) : null);
        }}
      >
        <option value="">Select connection</option>
        {unresolvedReference ? (
          <option value="__current-reference">{reference.connection}{unavailable ? " — Unavailable" : ""}</option>
        ) : null}
        {visibleState.items.map(item => (
          <option key={item.name} value={item.name}>
            {item.displayName || item.name} ({item.typeName} · {item.status})
          </option>
        ))}
      </select>
      <p id={statusId}>
        {visibleState.status === "loading" ? "Loading connections…" : null}
        {visibleState.status === "error" ? (
          <button type="button" onClick={() => setLoadAttempt(attempt => attempt + 1)}>Retry</button>
        ) : null}
        {unavailable ? "This connection is unavailable." : null}
        {needsAuthorization ? "Needs authorization. Publishing still succeeds; a run will fault until authorized." : null}
      </p>
    </div>
  );
}

function toReference(value: unknown): ConnectionReference | null {
  if (!value || typeof value !== "object") return null;
  const record = value as Record<string, unknown>;
  const nested = record.value && typeof record.value === "object" ? record.value as Record<string, unknown> : record;
  const name = typeof nested.connection === "string" ? nested.connection : typeof nested.name === "string" ? nested.name : "";
  if (!name.trim()) return null;
  const type = typeof nested.type === "string" ? nested.type : null;
  return { connection: name, type };
}

function toOpaqueReference(item: ConnectionMetadata, requiredType?: string): ConnectionReference {
  return { connection: item.name, type: requiredType || item.typeName };
}
