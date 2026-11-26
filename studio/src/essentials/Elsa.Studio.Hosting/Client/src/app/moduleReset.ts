import {requestStudioNavigation,studioNavigationGuardReleasedEvent} from "../sdk";

export const studioModulesChangedEvent = "elsa-studio:modules-changed";

/** A rejected reset remains queued until guards release or the user explicitly retries. */
export function watchStudioModuleChanges(options:{canReload():boolean;resetDocument(path:string):void;onPending():void}) {
 let queued=false;
 const retry=()=>{
  if(!queued || !options.canReload())return;
  const current=`${window.location.pathname}${window.location.search}${window.location.hash}`;
  if(!requestStudioNavigation(current,"/"))return;
  queued=false;options.resetDocument("/");
 };
 const changed=()=>{queued=true;options.onPending();retry();};
 window.addEventListener(studioModulesChangedEvent,changed);
 window.addEventListener(studioNavigationGuardReleasedEvent,retry);
 return {retry,dispose(){window.removeEventListener(studioModulesChangedEvent,changed);window.removeEventListener(studioNavigationGuardReleasedEvent,retry);}};
}
