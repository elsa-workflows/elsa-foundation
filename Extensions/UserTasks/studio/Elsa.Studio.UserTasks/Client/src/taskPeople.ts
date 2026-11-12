import { useEffect, useState } from "react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { targets, type TaskActor } from "./taskApi";

/**
 * Turns task user and group IDs into the names people recognise. The signed-in person reads as "You".
 * Other names come from the assignment-target directory, which only task and form managers may read;
 * anyone else, and anyone beyond its first page, still sees the stored ID.
 */
export function useTaskPeople(api: ElsaStudioModuleApi, identity: TaskActor | undefined) {
  const [names, setNames] = useState<ReadonlyMap<string, string>>(() => new Map());
  const directory = Boolean(identity && (identity.canManage || identity.canManageForms));
  useEffect(() => {
    if (!directory) { setNames(new Map()); return; }
    let active = true;
    targets(api).then(page => { if (active) setNames(new Map([...page.users, ...page.groups].map(item => [item.id, item.name]))); }, () => { /* IDs remain readable. */ });
    return () => { active = false; };
  }, [api, directory]);
  return (id: string | undefined | null) => !id ? "" : id === identity?.userId ? "You" : names.get(id) ?? id;
}
