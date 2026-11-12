import {expect,it,vi} from "vitest";
import {watchStudioModuleChanges,studioModulesChangedEvent} from "../app/moduleReset";
import {notifyStudioNavigationGuardReleased,studioNavigationRequestedEvent} from "../sdk";
it("queues module reset when Apply emits before its draft guard releases",()=>{
 window.history.replaceState({},"","/workflows/foreign?backend=retired");
 const reload=vi.fn(),onPending=vi.fn();let guarded=true;
 const guard=(event:Event)=>{if(guarded)event.preventDefault();};
 window.addEventListener(studioNavigationRequestedEvent,guard);
 const reset=watchStudioModuleChanges({canReload:()=>true,resetDocument:reload,onPending});
 try {
  window.dispatchEvent(new Event(studioModulesChangedEvent));
  expect(onPending).toHaveBeenCalledOnce();expect(reload).not.toHaveBeenCalled();
  guarded=false;notifyStudioNavigationGuardReleased();expect(reload).toHaveBeenCalledOnce();expect(reload).toHaveBeenCalledWith("/");
  notifyStudioNavigationGuardReleased();expect(reload).toHaveBeenCalledOnce();
 } finally {reset.dispose();window.removeEventListener(studioNavigationRequestedEvent,guard);window.history.replaceState({},"","/");}
});
it("retains a module reset while writes are in flight",()=>{
 let writes=1;const reload=vi.fn();
 const reset=watchStudioModuleChanges({canReload:()=>writes===0,resetDocument:reload,onPending:vi.fn()});
 try {window.dispatchEvent(new Event(studioModulesChangedEvent));expect(reload).not.toHaveBeenCalled();writes=0;notifyStudioNavigationGuardReleased();expect(reload).toHaveBeenCalledOnce();}finally{reset.dispose();}
});
