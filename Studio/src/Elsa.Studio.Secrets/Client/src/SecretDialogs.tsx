import React, { useState } from "react";
import type { CreateSecretRequest, RotateSecretRequest, SecretDescriptorsResponse, SecretMetadata } from "./secretTypes";

export function CreateSecretDialog({
  descriptors,
  onCancel,
  onSave
}: {
  descriptors: SecretDescriptorsResponse;
  onCancel(): void;
  onSave(request: CreateSecretRequest): Promise<void>;
}) {
  const [request, setRequest] = useState<CreateSecretRequest>({
    name: "",
    typeName: descriptors.types[0]?.name ?? "text",
    storeName: descriptors.stores[0]?.name ?? "encrypted",
    value: ""
  });

  return (
    <div className="secrets-modal" role="dialog" aria-modal="true" aria-label="Create secret">
      <form onSubmit={event => {
        event.preventDefault();
        void onSave(request);
      }}>
        <h3>New secret</h3>
        <Field label="Name" value={request.name} onChange={name => setRequest({ ...request, name })} />
        <Field label="Display name" value={request.displayName ?? ""} onChange={displayName => setRequest({ ...request, displayName })} />
        <label>Type<select value={request.typeName} onChange={event => setRequest({ ...request, typeName: event.target.value })}>{descriptors.types.map(type => <option key={type.name} value={type.name}>{type.displayName}</option>)}</select></label>
        <label>Store<select value={request.storeName} onChange={event => setRequest({ ...request, storeName: event.target.value })}>{descriptors.stores.map(store => <option key={store.name} value={store.name}>{store.displayName}</option>)}</select></label>
        {request.storeName === "configuration"
          ? <Field label="Configuration key" value={request.configurationKey ?? ""} onChange={configurationKey => setRequest({ ...request, configurationKey })} />
          : <Field label="Value" type="password" value={request.value ?? ""} onChange={value => setRequest({ ...request, value })} />}
        <div className="secrets-dialog-actions"><button type="button" onClick={onCancel}>Cancel</button><button type="submit">Create</button></div>
      </form>
    </div>
  );
}

export function RotateSecretDialog({ secret, onCancel, onSave }: { secret: SecretMetadata; onCancel(): void; onSave(request: RotateSecretRequest): Promise<void> }) {
  const [value, setValue] = useState("");
  const [configurationKey, setConfigurationKey] = useState("");
  const isConfiguration = secret.storeName === "configuration";

  return (
    <div className="secrets-modal" role="dialog" aria-modal="true" aria-label="Rotate secret">
      <form onSubmit={event => {
        event.preventDefault();
        void onSave(isConfiguration ? { configurationKey } : { value });
      }}>
        <h3>Rotate {secret.name}</h3>
        {isConfiguration
          ? <Field label="Configuration key" value={configurationKey} onChange={setConfigurationKey} />
          : <Field label="New value" type="password" value={value} onChange={setValue} />}
        <div className="secrets-dialog-actions"><button type="button" onClick={onCancel}>Cancel</button><button type="submit">Rotate</button></div>
      </form>
    </div>
  );
}

function Field({ label, value, type = "text", onChange }: { label: string; value: string; type?: string; onChange(value: string): void }) {
  return <label>{label}<input type={type} value={value} onChange={event => onChange(event.target.value)} /></label>;
}
