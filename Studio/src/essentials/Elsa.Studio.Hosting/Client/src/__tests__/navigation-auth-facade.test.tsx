import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { expect, it, vi } from "vitest";
import { useShellNavigationAccess } from "../app/navigation/useShellNavigationAccess";

// The app build externalizes the SDK. A local AuthContext is a different instance in that build.
vi.mock("@elsa-workflows/studio-sdk",async original=>({
  ...await original<typeof import("@elsa-workflows/studio-sdk")>(),
  useOptionalAuthContext:()=>({session:{status:"authenticated",subject:"restricted-user",tenantId:"tenant-b",roles:["reader"],permissions:["workflows/runtime:read"]}})
}));

it("uses the shared SDK identity for optional navigation and host-control permission checks",()=>{
  let observed:ReturnType<typeof useShellNavigationAccess>|undefined;
  function Probe(){observed=useShellNavigationAccess();return null;}
  const container=document.createElement("div");const root=createRoot(container);
  try {
    flushSync(()=>root.render(<Probe />));
    expect(observed?.authenticated).toBe(true);
    expect(observed?.roles).toEqual(["reader"]);
    expect(observed?.permissions).toEqual(["workflows/runtime:read"]);
    expect(observed?.canReadAdmin).toBe(false);
  } finally {flushSync(()=>root.unmount());}
});
