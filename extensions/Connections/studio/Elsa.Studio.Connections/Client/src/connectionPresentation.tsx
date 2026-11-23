import React from "react";
import { AlertCircle, CheckCircle2, CircleSlash, Clock3 } from "lucide-react";
import { StatusChip } from "@elsa-workflows/studio-ui";
import type { ConnectionDescriptorsResponse, ConnectionStatus } from "./connectionTypes";

const statuses = {
  Ready: { label: "Ready", tone: "success", icon: CheckCircle2 },
  NeedsAuthorization: { label: "Needs authorization", tone: "warning", icon: Clock3 },
  Error: { label: "Error", tone: "danger", icon: AlertCircle },
  Revoked: { label: "Revoked", tone: "neutral", icon: CircleSlash }
} as const;

export function ConnectionStatusBadge({ status }: { status: ConnectionStatus }) {
  const presentation = statuses[status] ?? { label: status, tone: "neutral", icon: AlertCircle };
  const Icon = presentation.icon;
  return <StatusChip tone={presentation.tone}><Icon size={13} aria-hidden="true" />{presentation.label}</StatusChip>;
}

export function connectionTypeLabel(descriptors: ConnectionDescriptorsResponse | null, name: string) {
  return descriptors?.types.find(type => type.name === name)?.displayName || name;
}

export function connectionSchemeLabel(descriptors: ConnectionDescriptorsResponse | null, name: string) {
  return descriptors?.schemes.find(scheme => scheme.name === name)?.displayName || name;
}
