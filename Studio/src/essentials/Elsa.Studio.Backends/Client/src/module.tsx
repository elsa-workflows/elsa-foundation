import React from "react";
import type { StudioModuleHostContext, StudioModuleInstance } from "@elsa-workflows/studio-sdk";
import { BackendConnections } from "./connections";
import { backendNavigation, BackendsPage, BackendSwitcher, BackendConnectionNotice } from "./BackendsPage";
import type { BackendRuntimeConfig } from "./runtime";

export function initialize(host: StudioModuleHostContext): StudioModuleInstance {
 const extension = host.configuration.extensions as Record<string, unknown> | undefined;
 const settings = extension?.backends as {configuration:BackendRuntimeConfig["backends"];profileApiPath:string} | undefined;
 if (!settings?.profileApiPath) throw new Error("Backend management runtime configuration is unavailable.");
 const connections = new BackendConnections({backendBaseUrl:host.configuration.backendBaseUrl as string,hostId:host.configuration.hostId as string,backends:settings.configuration,profileApiPath:settings.profileApiPath},host.host,host.authManager,host.identity);
 return {
  connection:connections,
  shell:[
   {id:"backends.connection",placement:"connection",component:({navigate})=><BackendSwitcher connections={connections} onNavigate={navigate} />},
   {id:"backends.notice",placement:"notice",component:()=> <BackendConnectionNotice connections={connections} />},
   {id:"backends.recovery",placement:"recovery",component:({navigate})=><BackendsPage connections={connections} navigate={navigate} />}
  ],
  register(api) {
   api.navigation.add(backendNavigation);
   api.routes.add({id:"backends",path:"/settings/backends",label:"Backends",component:({navigate})=><BackendsPage connections={connections} navigate={navigate} featureManagementAvailable={api.routes.list({includeUnavailable:true}).some(route=>route.id==="feature-management")} />});
  }
 };
}
