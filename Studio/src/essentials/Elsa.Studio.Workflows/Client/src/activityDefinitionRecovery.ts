import type { StudioActivityDefinitionRecoverySettings, StudioRuntimeIdentity } from "@elsa-workflows/studio-sdk";
import type { ActivityContract, ActivityDefinitionDraftView, ActivityDefinitionLayoutRecord } from "./activityDefinitionTypes";

const recoveryPrefix = "elsa.activity-definitions.recovery.v1";
const defaultTtlMinutes = 24 * 60;

export interface ActivityDefinitionRecoverySnapshot {
  capturedAt: string;
  expiresAt: string;
  definitionId: string;
  draftId: string;
  providerKey: string;
  providerSchemaVersion: string;
  baseRevision: number;
  contract: ActivityContract;
  payload: unknown;
  layout: ActivityDefinitionLayoutRecord[];
  presentationLabel?: string | null;
}

export interface ActivityDefinitionRecoveryStore {
  read(draft: ActivityDefinitionDraftView): ActivityDefinitionRecoverySnapshot | null;
  write(draft: ActivityDefinitionDraftView): void;
  clear(draft: ActivityDefinitionDraftView): void;
  clearIdentity(): void;
}

export function createActivityDefinitionRecoveryStore(
  settings: StudioActivityDefinitionRecoverySettings | undefined,
  identity: StudioRuntimeIdentity | undefined,
  storage: Storage | undefined = safeLocalStorage()
): ActivityDefinitionRecoveryStore | null {
  const subject = identity?.subject?.trim();
  const tenant = identity?.tenantId?.trim();
  if (settings?.enabled !== true || !subject || !tenant || !storage) return null;
  const identityPrefix = recoveryIdentityPrefix(tenant, subject);
  const ttlMinutes = Number.isFinite(settings.ttlMinutes) && (settings.ttlMinutes ?? 0) > 0 ? settings.ttlMinutes! : defaultTtlMinutes;

  const keyFor = (draft: ActivityDefinitionDraftView) => `${identityPrefix}${encode(draft.definitionId)}:${encode(draft.draftId)}:${encode(draft.provider.providerKey)}:${encode(draft.provider.schemaVersion)}`;

  return {
    read(draft) {
      const key = keyFor(draft);
      try {
        const raw = storage.getItem(key);
        if (!raw) return null;
        const snapshot = JSON.parse(raw) as ActivityDefinitionRecoverySnapshot;
        if (!isSnapshotFor(snapshot, draft) || Date.parse(snapshot.expiresAt) <= Date.now()) {
          removeSafely(storage, key);
          return null;
        }
        return snapshot;
      } catch {
        removeSafely(storage, key);
        return null;
      }
    },
    write(draft) {
      const capturedAt = new Date();
      const snapshot: ActivityDefinitionRecoverySnapshot = {
        capturedAt: capturedAt.toISOString(),
        expiresAt: new Date(capturedAt.getTime() + ttlMinutes * 60_000).toISOString(),
        definitionId: draft.definitionId,
        draftId: draft.draftId,
        providerKey: draft.provider.providerKey,
        providerSchemaVersion: draft.provider.schemaVersion,
        baseRevision: draft.revision,
        contract: draft.contract,
        payload: draft.provider.payload,
        layout: draft.layout,
        presentationLabel: draft.presentationLabel ?? null
      };
      try {
        storage.setItem(keyFor(draft), JSON.stringify(snapshot));
      } catch {
        // Device-local recovery is best effort. Persistence failures must not replace server autosave semantics.
      }
    },
    clear(draft) {
      try {
        storage.removeItem(keyFor(draft));
      } catch {
        // Contain storage policy/browser failures.
      }
    },
    clearIdentity() {
      clearActivityDefinitionRecoveryForIdentity(identity, storage);
    }
  };
}

export function clearActivityDefinitionRecoveryForIdentity(
  identity: StudioRuntimeIdentity | undefined,
  storage: Storage | undefined = safeLocalStorage()
) {
  const subject = identity?.subject?.trim();
  const tenant = identity?.tenantId?.trim();
  if (!subject || !tenant || !storage) return;
  const prefix = recoveryIdentityPrefix(tenant, subject);
  try {
    const keys = Array.from({ length: storage.length }, (_, index) => storage.key(index)).filter((key): key is string => Boolean(key?.startsWith(prefix)));
    for (const key of keys) storage.removeItem(key);
  } catch {
    // Logout continues even when browser storage is unavailable.
  }
}

function isSnapshotFor(snapshot: ActivityDefinitionRecoverySnapshot, draft: ActivityDefinitionDraftView) {
  return snapshot?.definitionId === draft.definitionId &&
    snapshot.draftId === draft.draftId &&
    snapshot.providerKey === draft.provider.providerKey &&
    snapshot.providerSchemaVersion === draft.provider.schemaVersion &&
    Number.isFinite(snapshot.baseRevision) &&
    Number.isFinite(Date.parse(snapshot.capturedAt)) &&
    Number.isFinite(Date.parse(snapshot.expiresAt));
}

function encode(value: string) {
  return encodeURIComponent(value);
}

function recoveryIdentityPrefix(tenantId: string, subject: string) {
  return `${recoveryPrefix}:${encode(tenantId)}:${encode(subject)}:`;
}

function safeLocalStorage() {
  try {
    return typeof window === "undefined" ? undefined : window.localStorage;
  } catch {
    return undefined;
  }
}

function removeSafely(storage: Storage, key: string) {
  try {
    storage.removeItem(key);
  } catch {
    // Ignore storage policy/browser failures.
  }
}
