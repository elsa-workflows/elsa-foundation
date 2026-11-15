import type { ConnectionCatalog, ConnectionDocument, ConnectionView } from "../sdk";

export const connectionDocument: ConnectionDocument = { id: "record", key: "company", displayName: "Company", protocol: "oidc", settings: { authority: "https://idp.example", clientId: "client" },
  secrets: { clientSecret: { name: "credential", typeName: "text", scope: "identity" } }, admission: { type: "reject", defaultRoleIds: [] }, enabled: false, preferred: false, displayOrder: 0, overridesDeployment: false };
export const view: ConnectionView = { document: connectionDocument, source: 1, effective: true, overridePermitted: false, materialRevision: "material", configurationEditable: true, configurationSupported: true,
  signInCallback: "https://backend.example/shell/sso/signin/company/callback", previewCallback: "https://backend.example/shell/sso/preview/company/callback" };
export const catalog: ConnectionCatalog = { tenantId: "default", protocolPath: "/sso", revision: "r1", databaseManagement: true, canManageConnections: true,
  provisioningPolicies: ["reject", "jit", "match", "integrator"], capabilities: { canManageUsers: true, canManageRoles: true, canMapGroups: false, canRevokeTokens: true }, connections: [view] };
