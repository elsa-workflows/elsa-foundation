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

export interface ListSecretsRequest {
  search?: string;
  pageSize?: number;
  activeOnly?: boolean;
}

export interface SecretTypeDescriptor {
  name: string;
  displayName: string;
  description?: string | null;
  editorHint?: string | null;
  supportedStoreNames: string[];
}

export interface SecretStoreDescriptor {
  name: string;
  displayName: string;
  description?: string | null;
  capabilities?: number | string[] | null;
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
  scope?: string;
  tags?: string[];
  value?: string;
  configurationKey?: string;
  expiresAt?: string;
  metadata?: Record<string, unknown>;
}

export interface UpdateSecretRequest {
  displayName: string;
  description?: string;
}

export interface RotateSecretRequest {
  value?: string;
  configurationKey?: string;
  expiresAt?: string;
  metadata?: Record<string, unknown>;
}

export interface SecretTestResponse {
  succeeded: boolean;
  code: string;
  message?: string | null;
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

export interface SecretPickerRequest {
  search?: string;
  typeNames?: string[];
  storeNames?: string[];
  scope?: string;
  activeOnly?: boolean;
}
