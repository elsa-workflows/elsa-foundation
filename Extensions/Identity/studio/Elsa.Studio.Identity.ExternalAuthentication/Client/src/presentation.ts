import type { ConnectionCatalog, ConnectionPreview, ConnectionTest, ConnectionView, Readiness } from "./sdk";

export function connectionState(view: ConnectionView) {
  if (view.document.archivedAt) return "Archived";
  if (!view.effective) return "Shadowed";
  return view.document.enabled ? "Enabled in configuration" : "Disabled";
}
export function sourceEffect(catalog: ConnectionCatalog, key: string) {
  return catalog.connections.filter(view => view.document.key === key).map(view =>
    `${view.source === 0 ? "Deployment" : "Studio"}: ${view.document.displayName || key} — ${connectionState(view).toLowerCase()}.`).join("\n");
}
export function testSucceeded(test: ConnectionTest | null, view: ConnectionView | null) {
  return Boolean(test && view && test.connectionRecordId === view.document.id && test.materialRevision === view.materialRevision &&
    !test.stale && test.readiness.ready && test.protocolFailure === 0);
}
export function previewCurrent(preview: ConnectionPreview | null, view: ConnectionView | null) {
  return Boolean(preview && view && !preview.stale && preview.connectionRecordId === view.document.id && preview.materialRevision === view.materialRevision);
}
export function testDescription(test: ConnectionTest) {
  if (test.stale) return "Configuration changed during the test. Run it again on the current draft.";
  if (test.readiness.ready && test.protocolFailure === 0) return "Provider metadata and signing keys verified. Continue with an interactive preview.";
  return readinessMessages[test.readiness.failure] ?? ({
    2: "The referenced secret is unavailable.", 3: "The provider could not be reached. Check its availability and retry.",
    4: "Provider metadata could not be verified. Check the issuer URL.", 5: "A provider endpoint is outside the configured trust boundary.",
    6: "The provider does not support the selected client authentication method.", 7: "The provider changed. Test the current configuration again.",
    8: "The client credential changed. Test and preview again."
  } as Record<number, string>)[test.protocolFailure] ?? "The provider test did not succeed. Check the configuration and retry.";
}
export function readinessDescription(readiness: Readiness) {
  return readiness.ready ? "Current settings and required services are ready. Availability is checked again at sign-in."
    : readinessMessages[readiness.failure] ?? "The current configuration is unavailable. Check its settings and required services.";
}
const readinessMessages: Record<number, string> = {
    2: "Check the connection fields.", 3: "The OIDC adapter is not installed on this backend.",
    4: "Check the issuer URL, client ID and client authentication settings.", 5: "The referenced secret could not be resolved. Check its name, scope, type and status.",
    6: "Configure the trusted public backend URL before testing callbacks.", 7: "The backend has no usable sign-in session authority.",
    8: "Configure durable authentication state storage on the backend.", 9: "The backend's identity store does not support this connection.",
    10: "The selected provisioning policy is not installed.", 11: "Check the provisioning policy and matching settings.",
    12: "An assigned Elsa role no longer exists. Update the role selection.", 13: "A role is outside the deployment's allowed role boundary."
};
export function previewDescription(preview: ConnectionPreview) {
  if (preview.stale) return "The connection changed after this preview. Test and preview the current configuration again.";
  if (preview.admission === 2) return "This identity would create a new Elsa user with the selected existing roles.";
  if (preview.admission === 1) return "This identity would sign in as the existing Elsa user. Saved role assignments remain unchanged.";
  const reasons: Record<number, string> = {
    1: "This identity is not linked to an Elsa user. The connection rejects unknown identities.",
    4: "The Elsa user is unavailable or disabled.", 5: "The user has no eligible active membership.",
    8: "Matching requires a verified email address.", 9: "No unique eligible Elsa user matches this identity.",
    10: "The configured identity ownership mode does not permit provisioning or matching here.",
    12: "A referenced Elsa role is unavailable.", 13: "The identity or requested roles exceed the connection's allowed grants."
  };
  return reasons[preview.admissionFailure] ?? "This identity would be rejected by the current provisioning policy.";
}
