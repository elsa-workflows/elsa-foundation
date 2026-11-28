import { CopyableIdentifier } from "@elsa-workflows/studio-ui";
import type { StudioNotificationApi } from "@elsa-workflows/studio-sdk";

export function CopySecretNameButton({ name, notifications, scope, externalValue }: {
  name: string; notifications?: StudioNotificationApi; scope?: object; externalValue?: boolean;
}) {
  return <CopyableIdentifier label="technical name" value={name} copyLabel={`Copy technical name ${name}`}
    externalValue={externalValue} labelPlacement="external" scope={scope}
    onCopied={() => notifications?.notify({ level: "success", title: "Secret name copied." })} />;
}
