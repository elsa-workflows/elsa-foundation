import React from "react";
import type { WeaverPrivilegePolicyView } from "./weaverTypes";

/** A bootstrap observation only. The backend rechecks host policy when an action is admitted. */
export function WeaverPrivilegePolicy({ privileges }: { privileges?: WeaverPrivilegePolicyView }) {
  return (
    <details className="weaver-privilege-policy">
      <summary>Host privilege policy</summary>
      {privileges ? <>
        <dl aria-label="Reported host privileges">
          <div><dt>Administrative</dt><dd>{privileges.administrative ? "Enabled" : "Disabled"}</dd></div>
          <div><dt>Irrecoverable</dt><dd>{privileges.irrecoverable ? "Enabled" : "Disabled"}</dd></div>
          <div><dt>Activation</dt><dd>{privileges.activation ? "Enabled" : "Disabled"}</dd></div>
        </dl>
        <p>Reported by the backend. Current host settings still apply when an action runs.</p>
      </> : <p>Not reported by this backend.</p>}
    </details>
  );
}
