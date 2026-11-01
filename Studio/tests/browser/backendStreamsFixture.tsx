import React, { useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import { PreparedBackend } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/backends/connections";
import { subscribeAgentSessionStream } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/agent/agentStream";
import { register as registerConsole, ConsoleStreamPanel } from "../../../Extensions/ConsoleStream/studio/Elsa.Studio.ConsoleStream/Client/src/module";
import { register as registerLogs, StructuredLogsPanel } from "../../../Extensions/Diagnostics.StructuredLogs/studio/Elsa.Studio.Diagnostics.StructuredLogs/Client/src/module";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";

const backend = new PreparedBackend({id:"probe",name:"Probe",baseUrl:`${window.location.origin}/_backend-transport-probe/`,environment:"Development",source:"configuration"},null,{status:"anonymous",roles:[],permissions:[]});
const registry = {add:()=>{}};
const api = {backend:backend.context,host:backend.context,navigation:registry,routes:registry,panels:registry} as unknown as ElsaStudioModuleApi;
registerConsole(api);registerLogs(api);
function Probe() {
  const [agent,setAgent] = useState("");
  useEffect(() => {
    const subscription = subscribeAgentSessionStream(backend.context,"/agent/stream",event => {if (event.type === "message-delta") setAgent(event.content);},error => setAgent(error.message));
    return () => subscription.close();
  },[]);
  return <main><p>{agent}</p><ConsoleStreamPanel /><StructuredLogsPanel /></main>;
}
createRoot(document.getElementById("root")!).render(<Probe />);
