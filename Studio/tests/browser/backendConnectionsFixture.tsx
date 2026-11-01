import React, { useEffect, useState, useSyncExternalStore } from "react";
import { createRoot } from "react-dom/client";
import { createEndpointContext } from "@elsa-workflows/studio-sdk";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { BackendConnections } from "../../src/essentials/Elsa.Studio.Backends/Client/src/connections";
import { BackendsPage, BackendSwitcher, BackendConnectionNotice } from "../../src/essentials/Elsa.Studio.Backends/Client/src/BackendsPage";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";

const connections = new BackendConnections({profileApiPath:"/_elsa/studio/backends/profiles",backendBaseUrl:`${window.location.origin}/authoring/`,backends:{profiles:[
  {id:"authoring",name:"Authoring · European development environment with a long name",baseUrl:`${window.location.origin}/authoring/`,environment:"Development",source:"configuration"},
  {id:"runtime",name:"Runtime",baseUrl:`${window.location.origin}/runtime/`,environment:"Test",source:"configuration"}
]}},createEndpointContext(window.location.origin),null,{status:"anonymous",roles:[],permissions:[]},url => {
  document.documentElement.dataset.committedBackendUrl=url;
});

function Fixture() {
  const [path,setPath] = useState("/settings/backends");
  const snapshot=useSyncExternalStore(connections.subscribe,connections.getSnapshot);
  useEffect(()=>{void connections.initialize();},[]);
  const navigate=(next:string)=>setPath(next);
  return <ThemeProvider><main style={{padding:20,minWidth:0}}>
    <BackendSwitcher connections={connections} onNavigate={navigate} />
    <BackendConnectionNotice connections={connections} />
    {snapshot.loading ? <p>Loading…</p> : <BackendsPage connections={connections} navigate={navigate} path={path} featureManagementAvailable />}
  </main></ThemeProvider>;
}
createRoot(document.getElementById("root")!).render(<Fixture />);
