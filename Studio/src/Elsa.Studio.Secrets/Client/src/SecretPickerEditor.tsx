import React, { useEffect, useMemo, useState } from "react";
import type { StudioActivityPropertyEditorProps, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { pickSecrets } from "./secretsApi";
import type { SecretMetadata, SecretReference } from "./secretTypes";

export function SecretPickerEditor({ value, disabled, onChange, endpointContext }: StudioActivityPropertyEditorProps & { endpointContext: StudioEndpointContext }) {
  const [items, setItems] = useState<SecretMetadata[]>([]);
  const reference = useMemo(() => toReference(value), [value]);

  useEffect(() => {
    void pickSecrets(endpointContext).then(response => setItems(response.items));
  }, [endpointContext]);

  return (
    <label className="secret-picker-editor">
      <span>Secret</span>
      <select
        disabled={disabled}
        value={reference?.name ?? ""}
        onChange={event => {
          const selected = items.find(item => item.name === event.target.value);
          onChange(selected ? { type: "Secret", value: { name: selected.name, typeName: selected.typeName } } : null);
        }}
      >
        <option value="">Select secret</option>
        {items.map(secret => <option key={secret.name} value={secret.name}>{secret.displayName || secret.name}</option>)}
      </select>
    </label>
  );
}

export function toReference(value: unknown): SecretReference | null {
  if (!value || typeof value !== "object") return null;
  const expression = value as { type?: string; value?: unknown };
  if (expression.type !== "Secret" || !expression.value || typeof expression.value !== "object") return null;
  const reference = expression.value as Partial<SecretReference>;
  return typeof reference.name === "string" ? { name: reference.name, typeName: reference.typeName ?? null, scope: reference.scope ?? null } : null;
}
