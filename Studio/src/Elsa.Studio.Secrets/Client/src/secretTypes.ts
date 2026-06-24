export type SecretStatus = "Active" | "Retired" | "Expired" | "Revoked" | "Deleted";

export interface SecretMetadata {
  id: string;
  name: string;
  displayName: string;
  description?: string | null;
  typeName: string;
  storeName: string;
  scope?: string | null;
  tags: string[];
  status: SecretStatus;
  currentVersion?: number | null;
  createdAt: string;
  updatedAt?: string | null;
  expiresAt?: string | null;
}

export interface ListSecretsResponse {
  items: SecretMetadata[];
  totalCount: number;
}

export interface SecretTypeDescriptor {
  name: string;
  displayName: string;
  description: string;
  editorHint: string;
  supportedStoreNames: string[];
}

export interface SecretStoreDescriptor {
  name: string;
  displayName: string;
  description: string;
  capabilities: number;
  isReadOnly: boolean;
}

export interface SecretDescriptorsResponse {
  types: SecretTypeDescriptor[];
  stores: SecretStoreDescriptor[];
}

export interface CreateSecretRequest {
  name: string;
  displayName?: string;
  description?: string;
  typeName: string;
  storeName: string;
  value?: string;
  configurationKey?: string;
}

export interface RotateSecretRequest {
  value?: string;
  configurationKey?: string;
}

export interface SecretReference {
  name: string;
  typeName?: string | null;
  scope?: string | null;
}

export interface SecretPickerResponse {
  items: SecretMetadata[];
  canCreateInline: boolean;
}
