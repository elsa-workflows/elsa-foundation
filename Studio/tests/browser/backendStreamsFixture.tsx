import React, { useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import { StudioBackendConnection as PreparedBackend } from "@elsa-workflows/studio-sdk";
import { subscribeWeaverSessionStream } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/weaver/weaverStream";
import { register as registerConsole, ConsoleStreamPanel } from "../../../Extensions/ConsoleStream/studio/Elsa.Studio.ConsoleStream/Client/src/module";
import { register as registerLogs, StructuredLogsPanel } from "../../../Extensions/Diagnostics.StructuredLogs/studio/Elsa.Studio.Diagnostics.StructuredLogs/Client/src/module";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";

const backend = new PreparedBackend({id:"probe",name:"Probe",baseUrl:`${window.location.origin}/_backend-transport-probe/`,environment:"Development",source:"configuration"},null,{status:"anonymous",roles:[],permissions:[]});
const registry = {add:()=>{}};
const api = {backend:backend.context,host:backend.context,navigation:registry,routes:registry,panels:registry} as unknown as ElsaStudioModuleApi;
registerConsole(api);registerLogs(api);
function Probe() {
  const [weaver,setWeaver] = useState("");
  useEffect(() => {
    const subscription = subscribeWeaverSessionStream(backend.context,"/weaver/stream",event => {if (event.type === "message-delta") setWeaver(event.content);},error => setWeaver(error.message));
    return () => subscription.close();
  },[]);
  return <main><p>{weaver}</p><ConsoleStreamPanel /><StructuredLogsPanel /></main>;
}
createRoot(document.getElementById("root")!).render(<Probe />);
