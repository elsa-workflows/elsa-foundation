export type ConnectionStatus = "Ready" | "NeedsAuthorization" | "Error" | "Revoked";

export interface ConnectionMetadata {
  tenantId: string;
  name: string;
  displayName: string;
  typeName: string;
  authScheme: string;
  settings: Record<string, string>;
  secretFieldNames: string[];
  managedBy: string;
  owner: string;
  status: ConnectionStatus;
  lastTestedAt?: string | null;
  lastUsedAt?: string | null;
  createdAt: string;
  tags: string[];
}

export interface ListConnectionsResponse {
  items: ConnectionMetadata[];
}

export interface ConnectionSettingField {
  name: string;
  displayName: string;
  editorHint: string;
  required?: boolean;
}

export interface ConnectionTypeDescriptor {
  name: string;
  displayName: string;
  supportedSchemes: string[];
  settings: ConnectionSettingField[];
}

export interface AuthSchemeField {
  name: string;
  displayName: string;
  sensitive: boolean;
  editorHint: string;
  required?: boolean;
}

export interface AuthSchemeDescriptor {
  name: string;
  displayName: string;
  fields: AuthSchemeField[];
  hasInteractiveAuthorization: boolean;
}

export interface ConnectionDescriptorsResponse {
  types: ConnectionTypeDescriptor[];
  schemes: AuthSchemeDescriptor[];
}

export interface CreateConnectionRequest {
  name: string;
  displayName?: string;
  typeName: string;
  authScheme: string;
  settings?: Record<string, string>;
  secretFields?: Record<string, { sourceKind?: string; existingSecretName?: string; value?: string; namedCredentialName?: string }>;
  tags?: string[];
}

export interface ConnectionTestResponse {
  succeeded: boolean;
  message?: string | null;
}

export interface ConnectionReference {
  connection: string;
  type?: string | null;
}

export interface ConnectionPickerResponse {
  items: ConnectionMetadata[];
}

export interface ConnectionPickerRequest {
  search?: string;
  typeName?: string;
}
