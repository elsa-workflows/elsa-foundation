import React, { useEffect, useId, useMemo, useRef, useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { pickConnectionSecrets } from "./connectionsApi";
import type { AuthSchemeField, ConnectionCredentialInput, ConnectionSecretPickerMetadata } from "./connectionTypes";

type PickerState = {
  requestKey: object;
  status: "loading" | "ready" | "error";
  items: ConnectionSecretPickerMetadata[];
  capped: boolean;
  denied: boolean;
};

export function ConnectionCredentialField({
  context,
  field,
  value,
  disabled = false,
  error,
  onChange
}: {
  context: StudioEndpointContext;
  field: AuthSchemeField;
  value: ConnectionCredentialInput;
  disabled?: boolean;
  error?: string;
  onChange(value: ConnectionCredentialInput): void;
}) {
  const [search, setSearch] = useState("");
  const [touched, setTouched] = useState(false);
  const [refreshAttempt, setRefreshAttempt] = useState(0);
  const requestKey = useMemo(() => ({ context, search, refreshAttempt }), [context, search, refreshAttempt]);
  const currentRequestKeyRef = useRef(requestKey);
  currentRequestKeyRef.current = requestKey;
  const previousSearchRef = useRef(search);
  const [pickerState, setPickerState] = useState<PickerState | null>(null);
  const visibleState: PickerState = pickerState?.requestKey === requestKey
    ? pickerState
    : { requestKey, status: "loading", items: [], capped: false, denied: false };
  const labelId = useId();
  const searchId = useId();
  const selectId = useId();
  const statusId = useId();
  const detailsId = useId();
  const radioName = useId();
  const source = value.source === "secret" ? "secret" : "value";
  const selectedName = value.source === "secret" ? value.existingSecretName : "";
  const selectedSecret = value.source === "secret" ? value.selectedSecret : undefined;
  const currentResult = visibleState.items.find(item => item.name === selectedName);
  const hasSelectedOption = !currentResult && !!selectedName;
  const selectionUnresolved = !!selectedName && (!selectedSecret || selectedSecret.name !== selectedName);
  const selectionExpired = !!selectedSecret && !isEligibleSecret(selectedSecret);
  const selectionUnavailable = selectionUnresolved || selectionExpired;
  const invalid = selectionUnavailable && source === "secret";
  const missing = field.required && (source === "secret" ? !selectedName : value.source !== "value" || !value.value.trim());
  const validationMessage = error || (invalid ? "Select an available active text Secret before creating this connection." :
    touched && missing ? `${field.displayName} is required. Choose an existing Secret or enter a new value.` : null);
  const errorId = `${selectId}-error`;

  useEffect(() => {
    let active = true;
    let timeout: ReturnType<typeof setTimeout> | undefined;
    setPickerState({ requestKey, status: "loading", items: [], capped: false, denied: false });
    const load = () => {
      void pickConnectionSecrets(context, search).then(result => {
        if (!active || currentRequestKeyRef.current !== requestKey) return;
        setPickerState({ requestKey, status: "ready", items: result.items, capped: result.capped, denied: false });
      }).catch(error => {
        if (!active || currentRequestKeyRef.current !== requestKey) return;
        setPickerState({ requestKey, status: "error", items: [], capped: false, denied: isForbidden(error) });
      });
    };
    const shouldDebounce = previousSearchRef.current !== search;
    previousSearchRef.current = search;
    if (shouldDebounce) timeout = setTimeout(load, 250);
    else load();
    return () => {
      active = false;
      if (timeout !== undefined) clearTimeout(timeout);
    };
  }, [context, requestKey, search]);

  function changeSource(nextSource: "secret" | "value") {
    setTouched(false);
    if (nextSource === "secret") onChange({ source: "secret", existingSecretName: "" });
    else onChange({ source: "value", value: "" });
  }

  return (
    <fieldset className="connection-credential-field" disabled={disabled} aria-describedby={detailsId}>
      <legend id={labelId}>{field.displayName}{field.required ? " (required)" : ""}</legend>
      <p id={detailsId} className="connection-credential-help">
        Choose an existing Secret or enter a new value. Secret values are never loaded here.
      </p>
      <div className="connection-credential-sources">
        <label>
          <input
            type="radio"
            name={radioName}
            value="secret"
            checked={source === "secret"}
            onChange={() => changeSource("secret")}
          />
          Existing Secret
        </label>
        <label>
          <input
            type="radio"
            name={radioName}
            value="value"
            checked={source === "value"}
            onChange={() => changeSource("value")}
          />
          New value
        </label>
      </div>

      {source === "secret" ? (
        <div className="connection-secret-picker">
          <div className="connection-secret-search-row">
            <label htmlFor={searchId}>Search Secrets for {field.displayName}</label>
            <input
              id={searchId}
              type="search"
              value={search}
              autoComplete="off"
              onChange={event => setSearch(event.target.value)}
            />
            <button type="button" onClick={() => setRefreshAttempt(attempt => attempt + 1)}>
              {visibleState.status === "error" ? "Retry" : "Refresh Secrets"}
            </button>
          </div>
          <label htmlFor={selectId}>Existing Secret for {field.displayName}</label>
          <select
            id={selectId}
            value={selectedName}
            required={field.required}
            aria-invalid={!!validationMessage || undefined}
            aria-describedby={`${statusId} ${detailsId}${validationMessage ? ` ${errorId}` : ""}`}
            onBlur={() => setTouched(true)}
            onChange={event => {
              const nextName = event.target.value;
              if (!nextName) {
                onChange({ source: "secret", existingSecretName: "" });
                return;
              }
              const nextSecret = visibleState.items.find(item => item.name === nextName);
              if (nextSecret) onChange({ source: "secret", existingSecretName: nextSecret.name, selectedSecret: nextSecret });
            }}
          >
            <option value="">Select an active text Secret</option>
            {hasSelectedOption ? (
              <option value={selectedName}>{formatSecret(selectedSecret, selectedName, "Selected")}</option>
            ) : null}
            {visibleState.items.map(secret => (
              <option key={secret.name} value={secret.name}>{formatSecret(secret)}</option>
            ))}
          </select>
          <p
            id={statusId}
            className={`connection-secret-picker-status ${visibleState.status}`}
            role={visibleState.status === "error" ? "alert" : "status"}
            aria-live="polite"
          >
            {visibleState.status === "loading" ? "Loading eligible text Secrets…" : null}
            {visibleState.status === "error" && visibleState.denied ? "Access to existing Secrets is denied. You need Secrets read permission; you can enter a new value instead." : null}
            {visibleState.status === "error" && !visibleState.denied ? "Existing Secrets could not be loaded. Retry or enter a new value." : null}
            {visibleState.status === "ready" && visibleState.items.length === 0 ? "No active text Secrets match this search." : null}
            {visibleState.status === "ready" && visibleState.capped ? "Showing up to 50 matches. Refine your search to find another Secret." : null}
          </p>
          {!validationMessage && selectedSecret ? (
            <p className="connection-secret-selection">Selected: {formatSecret(selectedSecret)}</p>
          ) : null}
        </div>
      ) : (
        <label className="connection-new-credential" htmlFor={`${selectId}-value`}>
          New value for {field.displayName}
          <input
            id={`${selectId}-value`}
            type="password"
            autoComplete="new-password"
            required={field.required}
            aria-invalid={!!validationMessage || undefined}
            aria-describedby={validationMessage ? errorId : detailsId}
            onBlur={() => setTouched(true)}
            value={value.source === "value" ? value.value : ""}
            onChange={event => onChange({ source: "value", value: event.target.value })}
          />
        </label>
      )}
      {validationMessage ? <p id={errorId} className="connection-credential-error" role="alert">{validationMessage}</p> : null}
    </fieldset>
  );
}

export function isEligibleSecret(secret: ConnectionSecretPickerMetadata, now = Date.now()) {
  if (secret.typeName !== "text" || secret.status.toLowerCase() !== "active") return false;
  if (!secret.expiresAt) return true;
  const expiry = Date.parse(secret.expiresAt);
  return Number.isFinite(expiry) && expiry > now;
}

function isForbidden(error: unknown) {
  if (!error || typeof error !== "object") return false;
  const candidate = error as { status?: unknown; statusCode?: unknown };
  return candidate.status === 403 || candidate.statusCode === 403;
}

function formatSecret(secret: ConnectionSecretPickerMetadata | undefined, fallbackName?: string, suffix?: string) {
  const name = secret?.name ?? fallbackName ?? "";
  const identity = secret?.displayName && secret.displayName !== name ? `${secret.displayName} · ${name}` : name;
  return [identity, secret?.scope ? `Scope: ${secret.scope}` : null, secret?.typeName, secret?.status, suffix].filter(Boolean).join(" · ");
}
