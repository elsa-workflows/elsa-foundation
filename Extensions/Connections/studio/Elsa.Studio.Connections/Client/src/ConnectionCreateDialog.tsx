import React, { useEffect, useId, useRef, useState } from "react";
import { X } from "lucide-react";
import { useDialogFocus, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { createConnection } from "./connectionsApi";
import type {
  AuthSchemeDescriptor,
  ConnectionCredentialInput,
  ConnectionDescriptorsResponse,
  ConnectionMetadata,
  ConnectionTypeDescriptor,
  CreateConnectionRequest
} from "./connectionTypes";
import { ConnectionCredentialField, isEligibleSecret } from "./ConnectionCredentialField";

export function ConnectionCreateDialog({
  context,
  descriptors,
  onCancel,
  onCreated
}: {
  context: StudioEndpointContext;
  descriptors: ConnectionDescriptorsResponse;
  onCancel(): void;
  onCreated(connection: ConnectionMetadata): void;
}) {
  const [name, setName] = useState("");
  const [typeName, setTypeName] = useState(() => initialTypeName(descriptors));
  const [authScheme, setAuthScheme] = useState(() => initialSchemeName(descriptors, initialTypeName(descriptors)));
  const [settings, setSettings] = useState<Record<string, string>>({});
  const [credentialInputs, setCredentialInputs] = useState<Record<string, ConnectionCredentialInput>>({});
  const [boundContext, setBoundContext] = useState(context);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const dialogRef = useRef<HTMLElement>(null);
  const errorRef = useRef<HTMLParagraphElement>(null);
  const currentContextRef = useRef(context);
  const submittingRef = useRef(false);
  const dialogTitleId = useId();
  const nameId = useId();
  const typeId = useId();
  const schemeId = useId();
  const serviceSettingsTitleId = useId();
  const authenticationTitleId = useId();
  currentContextRef.current = context;

  const contextIsCurrent = boundContext === context;
  const visibleTypeName = contextIsCurrent ? typeName : initialTypeName(descriptors);
  const visibleAuthScheme = contextIsCurrent ? authScheme : initialSchemeName(descriptors, visibleTypeName);
  const selectedType: ConnectionTypeDescriptor | undefined = descriptors.types.find(type => type.name === visibleTypeName);
  const supportedSchemes = selectedType?.supportedSchemes ?? [];
  const availableSchemes = descriptors.schemes.filter(scheme => supportedSchemes.includes(scheme.name));
  const selectedScheme: AuthSchemeDescriptor | undefined = availableSchemes.find(scheme => scheme.name === visibleAuthScheme);
  const visibleSettings = contextIsCurrent ? settings : {};
  const visibleCredentials = contextIsCurrent ? credentialInputs : {};
  const sensitiveFields = selectedScheme?.fields.filter(field => field.sensitive) ?? [];
  const nonSensitiveSchemeFields = selectedScheme?.fields.filter(field => !field.sensitive) ?? [];
  const validRequiredSettings = (selectedType?.settings ?? []).filter(field => field.required).every(field => isSettingPresent(field, visibleSettings[field.name]));
  const validRequiredSchemeSettings = nonSensitiveSchemeFields.filter(field => field.required).every(field => isSettingPresent(field, visibleSettings[field.name]));
  const validCredentials = sensitiveFields.every(field => {
    const input = visibleCredentials[field.name] ?? defaultCredentialInput();
    if (input.source === "value") return !field.required || input.value.trim().length > 0;
    if (!input.existingSecretName) return !field.required;
    return input.selectedSecret?.name === input.existingSecretName
      && isEligibleSecret(input.selectedSecret);
  });
  const canSubmit = contextIsCurrent
    && !!selectedType
    && !!selectedScheme
    && name.trim().length > 0
    && validRequiredSettings
    && validRequiredSchemeSettings
    && validCredentials;

  useDialogFocus(dialogRef, contextIsCurrent && !submitting ? onCancel : null);

  useEffect(() => {
    if (error) errorRef.current?.focus();
  }, [error]);

  useEffect(() => {
    if (boundContext === context) return;
    setBoundContext(context);
    setName("");
    const nextTypeName = initialTypeName(descriptors);
    setTypeName(nextTypeName);
    setAuthScheme(initialSchemeName(descriptors, nextTypeName));
    setSettings({});
    setCredentialInputs({});
    setError(null);
    submittingRef.current = false;
    setSubmitting(false);
  }, [boundContext, context, descriptors]);

  function resetCredentials() {
    setCredentialInputs({});
    setError(null);
  }

  function changeType(nextTypeName: string) {
    const nextType = descriptors.types.find(type => type.name === nextTypeName);
    setTypeName(nextTypeName);
    setAuthScheme(initialSchemeName(descriptors, nextTypeName, authScheme));
    setSettings({});
    resetCredentials();
    if (!nextType) setTypeName("");
  }

  function changeScheme(nextSchemeName: string) {
    setAuthScheme(nextSchemeName);
    setSettings({});
    resetCredentials();
  }

  function setCredentialInput(fieldName: string, input: ConnectionCredentialInput) {
    setCredentialInputs(current => ({ ...current, [fieldName]: input }));
    setError(null);
  }

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    if (submittingRef.current || !contextIsCurrent || !canSubmit) return;

    submittingRef.current = true;
    setSubmitting(true);
    setError(null);
    const operationContext = context;
    let closed = false;
    try {
      if (currentContextRef.current !== operationContext) return;

      const secretFields: CreateConnectionRequest["secretFields"] = {};
      for (const field of sensitiveFields) {
        const input = visibleCredentials[field.name] ?? defaultCredentialInput();
        if (input.source === "secret" && input.existingSecretName) {
          secretFields![field.name] = { sourceKind: "secret", existingSecretName: input.existingSecretName };
        } else if (input.source === "value" && input.value.trim()) {
          secretFields![field.name] = { value: input.value };
        }
      }
      const request: CreateConnectionRequest = {
        name: name.trim(),
        typeName: visibleTypeName,
        authScheme: visibleAuthScheme,
        settings: Object.fromEntries(Object.entries(visibleSettings).filter(([, value]) => value.trim().length > 0)),
        secretFields: secretFields!
      };
      const created = await createConnection(operationContext, request);
      if (currentContextRef.current !== operationContext) return;

      closed = true;
      submittingRef.current = false;
      setSubmitting(false);
      onCreated(created);
      onCancel();
    } catch (cause) {
      if (currentContextRef.current === operationContext) {
        setError(errorMessage(cause));
      }
    } finally {
      if (!closed && currentContextRef.current === operationContext) {
        submittingRef.current = false;
        setSubmitting(false);
      }
    }
  }

  return (
    <div
      className="connections-modal-backdrop"
      onMouseDown={event => {
        if (event.target === event.currentTarget && contextIsCurrent && !submitting) onCancel();
      }}
    >
      <section
        ref={dialogRef}
        className="connections-modal connections-create-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby={dialogTitleId}
        aria-busy={submitting || undefined}
        tabIndex={-1}
      >
        <form onSubmit={event => void submit(event)}>
          <header className="connections-create-header">
            <div>
              <p className="connections-create-eyebrow">New connection</p>
              <h2 id={dialogTitleId}>Create connection</h2>
              <p className="connections-modal-help">Choose a service and how it authenticates. Secret values are never shown here.</p>
            </div>
            <button
              type="button"
              className="connections-create-close"
              aria-label="Close create connection dialog"
              title="Close"
              disabled={!contextIsCurrent || submitting}
              onClick={() => {
                if (contextIsCurrent && !submitting) onCancel();
              }}
            >
              <X size={16} aria-hidden />
            </button>
          </header>

          <div className="connections-create-content">
            {error ? <p ref={errorRef} className="connections-create-error" role="alert" tabIndex={-1}>{error}</p> : null}
            <div className="connections-create-fields">
              <div className="connections-create-field connections-create-name">
                <label htmlFor={nameId}>Name</label>
                <input
                  id={nameId}
                  data-dialog-initial-focus
                  required
                  autoComplete="off"
                  disabled={!contextIsCurrent || submitting}
                  value={contextIsCurrent ? name : ""}
                  onChange={event => {
                    setName(event.target.value);
                    setError(null);
                  }}
                />
              </div>
              <div className="connections-create-field">
                <label htmlFor={typeId}>Type</label>
                <select
                  id={typeId}
                  required
                  value={visibleTypeName}
                  disabled={!contextIsCurrent || submitting}
                  onChange={event => changeType(event.target.value)}
                >
                  {descriptors.types.map(type => <option key={type.name} value={type.name}>{type.displayName}</option>)}
                </select>
              </div>
              <div className="connections-create-field">
                <label htmlFor={schemeId}>Auth scheme</label>
                <select
                  id={schemeId}
                  required
                  value={visibleAuthScheme}
                  disabled={!contextIsCurrent || submitting || availableSchemes.length === 0}
                  onChange={event => changeScheme(event.target.value)}
                >
                  {availableSchemes.map(scheme => <option key={scheme.name} value={scheme.name}>{scheme.displayName}</option>)}
                </select>
              </div>
            </div>

            <section className="connections-create-section" aria-labelledby={serviceSettingsTitleId}>
              <h3 id={serviceSettingsTitleId}>Service settings</h3>
              {(selectedType?.settings ?? []).length > 0 ? (selectedType?.settings ?? []).map(field => {
                const id = `${nameId}-type-${safeId(field.name)}`;
                const checked = visibleSettings[field.name] === "true";
                return (
                  <label className="connection-setting-field" key={field.name} htmlFor={id}>
                    {field.displayName}{field.required ? " (required)" : ""}
                    <input
                      id={id}
                      type={field.editorHint === "checkbox" ? "checkbox" : "text"}
                      required={field.required && field.editorHint !== "checkbox"}
                      checked={field.editorHint === "checkbox" ? checked : undefined}
                      value={field.editorHint === "checkbox" ? undefined : visibleSettings[field.name] ?? ""}
                      placeholder={field.name === "allowedHosts" ? "api.example, *.googleapis.com" : undefined}
                      disabled={!contextIsCurrent || submitting}
                      onChange={event => {
                        setSettings(current => ({
                          ...current,
                          [field.name]: field.editorHint === "checkbox" ? String(event.target.checked) : event.target.value
                        }));
                        setError(null);
                      }}
                    />
                  </label>
                );
              }) : <p className="connections-create-section-empty">No additional service settings required.</p>}
            </section>

            <section className="connections-create-section" aria-labelledby={authenticationTitleId}>
              <h3 id={authenticationTitleId}>Authentication</h3>
              {nonSensitiveSchemeFields.map(field => {
                const id = `${nameId}-scheme-${safeId(field.name)}`;
                return (
                  <label className="connection-setting-field" key={field.name} htmlFor={id}>
                    {field.displayName}{field.required ? " (required)" : ""}
                    <input
                      id={id}
                      value={visibleSettings[field.name] ?? ""}
                      required={field.required}
                      autoComplete="off"
                      disabled={!contextIsCurrent || submitting}
                      onChange={event => {
                        setSettings(current => ({ ...current, [field.name]: event.target.value }));
                        setError(null);
                      }}
                    />
                  </label>
                );
              })}

              {sensitiveFields.map(field => (
                <ConnectionCredentialField
                  key={`${typeName}:${authScheme}:${field.name}`}
                  context={context}
                  field={field}
                  value={visibleCredentials[field.name] ?? defaultCredentialInput()}
                  disabled={!contextIsCurrent || submitting}
                  onChange={input => setCredentialInput(field.name, input)}
                />
              ))}
              {nonSensitiveSchemeFields.length === 0 && sensitiveFields.length === 0
                ? <p className="connections-create-section-empty">No additional authentication fields required.</p>
                : null}
            </section>
          </div>

          <div className="connections-create-actions">
            <button type="button" disabled={submitting} onClick={onCancel}>Cancel</button>
            <button type="submit" disabled={!canSubmit || submitting}>{submitting ? "Creating…" : "Create"}</button>
          </div>
        </form>
      </section>
    </div>
  );
}

function initialTypeName(descriptors: ConnectionDescriptorsResponse) {
  return descriptors.types.some(type => type.name === "http") ? "http" : descriptors.types[0]?.name ?? "";
}

function initialSchemeName(descriptors: ConnectionDescriptorsResponse, typeName: string, preferredName?: string) {
  const supported = descriptors.types.find(type => type.name === typeName)?.supportedSchemes ?? [];
  if (preferredName && supported.includes(preferredName)) return preferredName;
  return descriptors.schemes.find(scheme => supported.includes(scheme.name))?.name ?? "";
}

function defaultCredentialInput(): ConnectionCredentialInput {
  return { source: "secret", existingSecretName: "" };
}

function isSettingPresent(field: { editorHint: string }, value?: string) {
  return field.editorHint === "checkbox" ? value === "true" : !!value?.trim();
}

function errorMessage(error: unknown) {
  if (error instanceof Error && error.message.trim()) return error.message;
  return "The connection could not be created.";
}

function safeId(value: string) {
  return value.replace(/[^a-zA-Z0-9_-]/g, "-");
}
