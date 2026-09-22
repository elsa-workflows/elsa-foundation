import React, { useEffect, useId, useMemo, useRef, useState } from "react";
import type { StudioEndpointContext, StudioExpressionEditorProps } from "@elsa-workflows/studio-sdk";
import { pickSecrets } from "./secretsApi";
import type { SecretMetadata, SecretReference } from "./secretTypes";

type SecretPickerMetadata = Pick<SecretMetadata, "id" | "name" | "displayName" | "typeName" | "storeName" | "scope" | "status">;
type LoadState =
  | { endpointContext: StudioEndpointContext; status: "loading"; items: SecretPickerMetadata[] }
  | { endpointContext: StudioEndpointContext; status: "ready"; items: SecretPickerMetadata[] }
  | { endpointContext: StudioEndpointContext; status: "error"; items: SecretPickerMetadata[] };

export function SecretPickerEditor({
  value,
  disabled,
  initialFocus,
  onChange,
  endpointContext
}: StudioExpressionEditorProps & { endpointContext: StudioEndpointContext }) {
  const [loadAttempt, setLoadAttempt] = useState(0);
  const [state, setState] = useState<LoadState>({ endpointContext, status: "loading", items: [] });
  const visibleState: LoadState = state.endpointContext === endpointContext
    ? state
    : { endpointContext, status: "loading", items: [] };
  const selectRef = useRef<HTMLSelectElement>(null);
  const focusPendingRef = useRef(initialFocus === true);
  const selectId = useId();
  const statusId = useId();
  const metadataId = useId();
  const reference = useMemo(() => toReference(value), [value]);
  const selected = reference ? visibleState.items.find(item => referencesMatch(item, reference)) : undefined;
  const currentValue = selected?.id ?? (reference ? "__current-reference" : "");

  useEffect(() => {
    let active = true;
    setState({ endpointContext, status: "loading", items: [] });
    void pickSecrets(endpointContext, { activeOnly: true }).then(response => {
      if (!active) return;
      setState({ endpointContext, status: "ready", items: allowlistMetadata(response.items) });
    }).catch(() => {
      if (!active) return;
      setState({ endpointContext, status: "error", items: [] });
    });
    return () => {
      active = false;
    };
  }, [endpointContext, loadAttempt]);

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
    <div className="secret-picker-editor">
      <label htmlFor={selectId}>Secret</label>
      <select
        ref={selectRef}
        id={selectId}
        aria-describedby={`${statusId} ${metadataId}`}
        disabled={selectionDisabled}
        value={currentValue}
        onChange={event => {
          const next = visibleState.items.find(item => item.id === event.target.value);
          onChange(next ? toOpaqueReference(next) : null);
        }}
      >
        <option value="">Select secret</option>
        {unresolvedReference ? (
          <option value="__current-reference">{reference.name}{unavailable ? " — Unavailable" : ""}</option>
        ) : null}
        {visibleState.items.map(secret => (
          <option key={secret.id} value={secret.id}>
            {formatMetadata(secret)}
          </option>
        ))}
      </select>
      <p id={statusId} className={`secret-picker-status ${visibleState.status}`} role={visibleState.status === "error" ? "alert" : "status"}>
        {visibleState.status === "loading" ? "Loading Secret metadata..." : null}
        {visibleState.status === "error" ? (
          <>
            <span>Secret metadata is unavailable.</span>
            <button type="button" onClick={() => setLoadAttempt(attempt => attempt + 1)}>Retry</button>
          </>
        ) : null}
        {visibleState.status === "ready" && visibleState.items.length === 0 ? "No active Secrets are available." : null}
      </p>
      <p id={metadataId} className="secret-picker-metadata">
        {selected
          ? formatMetadata(selected)
          : reference
            ? formatReference(reference, unavailable)
            : "Secret values are never displayed."}
      </p>
    </div>
  );
}

export function toReference(value: unknown): SecretReference | null {
  if (!value || typeof value !== "object") return null;
  const reference = value as Partial<SecretReference>;
  if (typeof reference.name !== "string" || !reference.name.trim()) return null;
  return {
    name: reference.name,
    typeName: typeof reference.typeName === "string" ? reference.typeName : null,
    scope: typeof reference.scope === "string" ? reference.scope : null
  };
}

function allowlistMetadata(items: SecretMetadata[]): SecretPickerMetadata[] {
  return items
    .filter(secret => !secret.status || secret.status === "Active")
    .map(secret => ({
      id: secret.id,
      name: secret.name,
      displayName: secret.displayName,
      typeName: secret.typeName,
      storeName: secret.storeName,
      scope: secret.scope ?? null,
      status: secret.status
    }));
}

function toOpaqueReference(secret: SecretPickerMetadata): SecretReference {
  return {
    name: secret.name,
    typeName: secret.typeName,
    ...(secret.scope ? { scope: secret.scope } : {})
  };
}

function referencesMatch(secret: SecretPickerMetadata, reference: SecretReference) {
  return secret.name === reference.name
    && (!reference.typeName || secret.typeName === reference.typeName)
    && (!reference.scope || secret.scope === reference.scope);
}

function formatMetadata(secret: SecretPickerMetadata) {
  return [
    secret.displayName || secret.name,
    secret.displayName && secret.displayName !== secret.name ? secret.name : null,
    secret.scope,
    secret.storeName,
    secret.typeName,
    secret.status || "Available"
  ].filter(Boolean).join(" · ");
}

function formatReference(reference: SecretReference, unavailable: boolean) {
  return [
    reference.name,
    reference.scope,
    reference.typeName,
    unavailable ? "Unavailable" : null
  ].filter(Boolean).join(" · ");
}
