import React, { useEffect, useId, useRef, useState } from "react";
import { X } from "lucide-react";
import { useDialogFocus, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { AdvancedSection } from "@elsa-workflows/studio-ui";
import { createConnection } from "./connectionsApi";
import type {
  AuthSchemeDescriptor,
  AuthSchemeField,
  ConnectionCredentialInput,
  ConnectionDescriptorsResponse,
  ConnectionMetadata,
  ConnectionSettingField,
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
  const addressGuidanceId = useId();
  const placementErrorId = useId();
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
  const typeSettings = selectedType?.settings ?? [];
  const sensitiveFields = selectedScheme?.fields.filter(field => field.sensitive) ?? [];
  const nonSensitiveSchemeFields = selectedScheme?.fields.filter(field => !field.sensitive) ?? [];
  const httpAddressFields = getHttpAddressFields(selectedType);
  const apiKeyFields = getBuiltInApiKeyFields(selectedType, selectedScheme);
  const effectivePlacement = apiKeyFields ? resolveApiKeyPlacement(visibleSettings[apiKeyFields.placement.name]) : null;
  const invalidPlacement = !!apiKeyFields && effectivePlacement === null;
  const advancedTypeFields = httpAddressFields
    ? typeSettings.filter(field =>
      (!field.required && field.name === httpAddressFields.allowedHosts.name)
      || (!field.required && isKnownHttpSetting(field, "allowInsecureHttp", "checkbox")))
    : [];
  const directTypeFields = typeSettings.filter(field => !advancedTypeFields.includes(field));
  const directSchemeFields = nonSensitiveSchemeFields.filter(field => {
    if (!apiKeyFields) return true;
    if (field.required) return true;
    return field.name !== apiKeyFields.headerName.name
      && field.name !== apiKeyFields.placement.name
      && field.name !== apiKeyFields.queryName.name;
  });
  const visibleAdvancedApiKeyNames = apiKeyFields
    ? [
      apiKeyFields.headerName.required ? null : effectivePlacement === "header" ? apiKeyFields.headerName : null,
      apiKeyFields.queryName.required ? null : effectivePlacement === "query" ? apiKeyFields.queryName : null
    ].filter((field): field is AuthSchemeDescriptor["fields"][number] => field !== null)
    : [];
  const advancedSettingNames = new Set([
    ...advancedTypeFields.map(field => field.name),
    ...(apiKeyFields && !apiKeyFields.placement.required ? [apiKeyFields.placement.name] : []),
    ...(apiKeyFields && !apiKeyFields.headerName.required ? [apiKeyFields.headerName.name] : []),
    ...(apiKeyFields && !apiKeyFields.queryName.required ? [apiKeyFields.queryName.name] : [])
  ]);
  const advancedSetCount = [...advancedSettingNames].filter(fieldName => !!visibleSettings[fieldName]?.trim()).length;
  const hasHttpAddress = !httpAddressFields
    || isSettingPresent(httpAddressFields.baseUrl, visibleSettings[httpAddressFields.baseUrl.name])
    || isSettingPresent(httpAddressFields.allowedHosts, visibleSettings[httpAddressFields.allowedHosts.name]);
  const validRequiredSettings = typeSettings.filter(field => field.required).every(field => isSettingPresent(field, visibleSettings[field.name]));
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
    && hasHttpAddress
    && !invalidPlacement
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

  function renderTypeSetting(field: ConnectionSettingField) {
    const id = `${nameId}-type-${safeId(field.name)}`;
    const isCheckbox = field.editorHint === "checkbox";
    const checked = visibleSettings[field.name] === "true";
    const isBaseUrl = httpAddressFields?.baseUrl.name === field.name;
    const isInsecureHttp = !!httpAddressFields
      && selectedType?.name === "http"
      && !field.required
      && isKnownHttpSetting(field, "allowInsecureHttp", "checkbox");
    const isAllowedHosts = httpAddressFields?.allowedHosts.name === field.name;

    return (
      <label className="connection-setting-field" key={field.name} htmlFor={id}>
        {field.displayName}{field.required ? " (required)" : ""}
        <input
          id={id}
          type={isCheckbox ? "checkbox" : "text"}
          required={field.required && !isCheckbox}
          checked={isCheckbox ? checked : undefined}
          value={isCheckbox ? undefined : visibleSettings[field.name] ?? ""}
          placeholder={isAllowedHosts ? "api.example, *.googleapis.com" : undefined}
          aria-describedby={isBaseUrl && httpAddressFields && !hasHttpAddress ? addressGuidanceId : undefined}
          disabled={!contextIsCurrent || submitting}
          onChange={event => {
            setSettings(current => {
              if (isInsecureHttp && !event.target.checked) {
                return Object.fromEntries(Object.entries(current).filter(([name]) => name !== field.name));
              }
              return {
                ...current,
                [field.name]: isCheckbox ? String(event.target.checked) : event.target.value
              };
            });
            setError(null);
          }}
        />
      </label>
    );
  }

  function renderSchemeSetting(field: AuthSchemeField) {
    if (apiKeyFields?.placement.name === field.name) return renderApiKeyPlacement(field);

    const id = `${nameId}-scheme-${safeId(field.name)}`;
    const placeholder = field.name === apiKeyFields?.headerName.name
      ? "Authorization"
      : field.name === apiKeyFields?.queryName.name ? "api_key" : undefined;
    return (
      <label className="connection-setting-field" key={field.name} htmlFor={id}>
        {field.displayName}{field.required ? " (required)" : ""}
        <input
          id={id}
          value={visibleSettings[field.name] ?? ""}
          placeholder={placeholder}
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
  }

  function renderApiKeyPlacement(field: AuthSchemeField) {
    const id = `${nameId}-scheme-${safeId(field.name)}`;
    const rawPlacement = visibleSettings[field.name];
    return (
      <div className="connection-setting-field" key={field.name}>
        <label htmlFor={id}>{field.displayName}{field.required ? " (required)" : ""}</label>
        <select
          id={id}
          required={field.required}
          value={effectivePlacement ?? rawPlacement ?? "header"}
          aria-invalid={invalidPlacement || undefined}
          aria-describedby={invalidPlacement ? placementErrorId : undefined}
          disabled={!contextIsCurrent || submitting}
          onChange={event => {
            setSettings(current => ({ ...current, [field.name]: event.target.value }));
            setError(null);
          }}
        >
          {invalidPlacement ? <option value={rawPlacement}>Unsupported placement: {rawPlacement}</option> : null}
          <option value="header">Header</option>
          <option value="query">Query</option>
        </select>
        {invalidPlacement ? <span className="connections-create-field-error" id={placementErrorId}>Choose Header or Query for this connection.</span> : null}
      </div>
    );
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

            {directTypeFields.length > 0 ? (
              <section className="connections-create-section" aria-labelledby={serviceSettingsTitleId}>
                <h3 id={serviceSettingsTitleId}>Service settings</h3>
                {directTypeFields.map(renderTypeSetting)}
              </section>
            ) : typeSettings.length === 0 ? (
              <section className="connections-create-section" aria-labelledby={serviceSettingsTitleId}>
                <h3 id={serviceSettingsTitleId}>Service settings</h3>
                <p className="connections-create-section-empty">No additional service settings required.</p>
              </section>
            ) : null}

            {httpAddressFields && !hasHttpAddress ? (
              <p className="connections-create-guidance" id={addressGuidanceId} role="note">
                Enter either a Base URL or Allowed hosts in More options.
              </p>
            ) : null}

            <section className="connections-create-section" aria-labelledby={authenticationTitleId}>
              <h3 id={authenticationTitleId}>Authentication</h3>
              {directSchemeFields.map(renderSchemeSetting)}

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
              {directSchemeFields.length === 0 && sensitiveFields.length === 0
                ? <p className="connections-create-section-empty">No additional authentication fields required.</p>
                : null}
            </section>

            {(advancedTypeFields.length > 0 || apiKeyFields && advancedSettingNames.size > 0) ? (
              <AdvancedSection
                label="More options"
                setCount={advancedSetCount}
                hasError={!!error || invalidPlacement}
                allowCollapseWhenSet
              >
                <div className="connections-create-advanced-fields">
                  {advancedTypeFields.map(renderTypeSetting)}
                  {apiKeyFields && !apiKeyFields.placement.required ? renderApiKeyPlacement(apiKeyFields.placement) : null}
                  {visibleAdvancedApiKeyNames.map(renderSchemeSetting)}
                </div>
              </AdvancedSection>
            ) : null}
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

export function resolveApiKeyPlacement(value?: string): "header" | "query" | null {
  if (!value || value.trim().length === 0) return "header";
  const placement = value.toLowerCase();
  return placement === "header" || placement === "query" ? placement : null;
}

function getHttpAddressFields(type?: ConnectionTypeDescriptor) {
  if (type?.name !== "http") return undefined;
  const baseUrl = findUniqueField(type.settings, "baseUrl");
  const allowedHosts = findUniqueField(type.settings, "allowedHosts");
  if (!isKnownHttpSetting(baseUrl, "baseUrl", "url") || !isKnownHttpSetting(allowedHosts, "allowedHosts", "text")) return undefined;
  return { baseUrl, allowedHosts };
}

function getBuiltInApiKeyFields(type?: ConnectionTypeDescriptor, scheme?: AuthSchemeDescriptor) {
  if (type?.name !== "http" || scheme?.name !== "api-key") return undefined;
  const headerName = findUniqueField(scheme.fields, "headerName");
  const placement = findUniqueField(scheme.fields, "placement");
  const queryName = findUniqueField(scheme.fields, "queryName");
  const apiKey = findUniqueField(scheme.fields, "apiKey");
  if (!isKnownApiKeyTextField(headerName, "headerName")
    || !isKnownApiKeyTextField(placement, "placement")
    || !isKnownApiKeyTextField(queryName, "queryName")
    || placement.required === true
    || !apiKey?.sensitive
    || apiKey.editorHint !== "password"
    || apiKey.required !== true) return undefined;
  return { headerName, placement, queryName };
}

function findUniqueField<T extends { name: string }>(fields: readonly T[], name: string) {
  const matching = fields.filter(field => field.name === name);
  return matching.length === 1 ? matching[0] : undefined;
}

function isKnownHttpSetting(field: ConnectionSettingField | undefined, name: string, editorHint: string): field is ConnectionSettingField {
  return field?.name === name && field.editorHint === editorHint;
}

function isKnownApiKeyTextField(field: AuthSchemeField | undefined, name: string): field is AuthSchemeField {
  return field?.name === name && field.sensitive === false && field.editorHint === "text";
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
