import React, { useEffect, useId, useRef, useState, useSyncExternalStore } from "react";
import { ChevronDown, Plus, Server } from "lucide-react";
import { qualifyStudioBackendPath, useStudioWorkspaceTab, studioNavigationRequestedEvent, useStudioRouteLocation, type StudioApiCapabilityDocument, type StudioNavigationRequest, type StudioBackendProfile } from "@elsa-workflows/studio-sdk";
import { studioRowInteraction, AnchoredPopover, StudioShellBreadcrumbs, StudioPageHeader, StudioButton, StudioAlert, StudioTabs, StudioTabPanel, StudioField, StudioTableContainer, type StudioBreadcrumbLink } from "@elsa-workflows/studio-ui";
import { BackendConnections, backendConnectionNonce, canonicalBackendUrl, connectionError } from "./connections";
import "./backends.css";

export const backendNavigation = {id:"backends",label:"Backends",path:"/settings/backends",activePathPrefix:"/settings/backends",placement:"settings" as const,order:20};

function BackendBreadcrumbs({items,current,navigate}:{items:StudioBreadcrumbLink[];current:React.ReactNode;navigate:(path:string)=>void}) {
  return <StudioShellBreadcrumbs items={items} current={current} renderLink={item => {
    const href = qualifyStudioBackendPath(item.href);
    return <a href={href} onClick={event => {
      if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
      event.preventDefault();
      navigate(href);
    }}>{item.label}</a>;
  }} />;
}

export function BackendSwitcher({connections,onNavigate}:{connections:BackendConnections;onNavigate:(path:string)=>void}) {
  const snapshot = useSyncExternalStore(connections.subscribe,connections.getSnapshot);
  const [open,setOpen] = useState(false);
  const container = useRef<HTMLDivElement>(null);
  const button = useRef<HTMLButtonElement>(null);
  const active = snapshot.active;
  return <div className="backend-switcher" ref={container} onKeyDown={event => { if (event.key === "Escape") { setOpen(false); button.current?.focus(); } }}>
    <StudioButton ref={button} type="button" className="backend-switcher-trigger" aria-label={`Select backend. Current: ${active?.profile.name ?? "No connection"}`} aria-expanded={open} onClick={() => setOpen(value => !value)}>
      <Server size={17} aria-hidden="true" /><span><strong>{active?.profile.name ?? "Choose backend"}</strong><small>{active?.profile.environment === "Production" ? "Production · " : ""}{active ? new URL(active.profile.baseUrl).host : "Not connected"}</small></span><ChevronDown size={14} aria-hidden="true" />
    </StudioButton>
    <AnchoredPopover open={open} anchorRef={button} className="backend-switcher-popover" minWidth={260} maxHeight={420} onDismiss={() => setOpen(false)}>
      <div aria-label="Backend connections" className="backend-switcher-popover-content">
        <div className="backend-switcher-popover-header">Connect to backend</div>
        <div className="backend-switcher-popover-profiles">
          {snapshot.profiles.map(profile => <StudioButton type="button" key={profile.id} className="backend-switcher-popover-profile" disabled={snapshot.busy || profile.id === active?.profile.id && !active?.disposed && !active?.discoveryError} onClick={() => { setOpen(false); void connections.connect(profile.id); }}>
            <div className="backend-switcher-popover-profile-info">
              <strong>{profile.name}</strong>
              <small>{profile.environment} · {new URL(profile.baseUrl).host}</small>
            </div>
            <span className="backend-switcher-popover-profile-status">{profile.id === active?.profile.id ? "Active" : "Connect"}</span>
          </StudioButton>)}
        </div>
        <div className="backend-switcher-popover-footer">
          <StudioButton type="button" className="backend-switcher-popover-manage" onClick={() => {setOpen(false);onNavigate("/settings/backends");}}>Manage backends</StudioButton>
        </div>
      </div>
    </AnchoredPopover>
  </div>;
}

export function BackendConnectionNotice({connections}:{connections:BackendConnections}) {
  const snapshot = useSyncExternalStore(connections.subscribe,connections.getSnapshot);
  if (!snapshot.error && !snapshot.busy && !snapshot.signInProfile) return null;
  return <div className="backend-connection-notice" role="status" aria-live="polite">
    {snapshot.error ? <StudioAlert tone="warning">{snapshot.error}</StudioAlert> : null}
    {snapshot.busy ? <span>Checking backend connection… <StudioButton type="button" onClick={() => connections.cancelConnection()}>Cancel</StudioButton></span> : null}
    {snapshot.signInProfile ? <StudioButton type="button" disabled={snapshot.busy} onClick={() => {void connections.signIn();}}>Sign in to {snapshot.profiles.find(profile => profile.id === snapshot.signInProfile)?.name}</StudioButton> : null}
  </div>;
}

export function BackendsPage({connections,navigate,path:explicitPath,featureManagementAvailable=false}:{connections:BackendConnections;navigate:(path:string)=>void;path?:string;featureManagementAvailable?:boolean}) {
  // A background workspace tab can re-render on a connection change while another tab owns the address bar.
  const routeLocation = useStudioRouteLocation();
  const path = explicitPath ?? routeLocation.pathname;
  const snapshot = useSyncExternalStore(connections.subscribe,connections.getSnapshot);
  const segment = path.slice("/settings/backends".length).split("/").filter(Boolean);
  const create = segment[0] === "new";
  let profileId:string|undefined;
  try {profileId = segment[0] ? decodeURIComponent(segment[0]) : undefined;} catch {profileId = undefined;}
  const profile = profileId ? snapshot.profiles.find(item => item.id === profileId) : undefined;
  const edit = !!profile && segment[1] === "edit";
  const detailPath = profile ? `/settings/backends/${encodeURIComponent(profile.id)}` : "/settings/backends";
  if (create || edit) return <BackendForm key={create ? "new" : profile!.id} connections={connections} profile={edit ? profile : undefined} navigate={navigate} />;
  if (segment.length && !profile) return <div className="backends-page"><div style={{display:"flex",flexDirection:"column",gap:"16px"}}>
    <BackendBreadcrumbs items={[{href:"/settings/backends",label:"Backends"}]} current="Unavailable profile" navigate={navigate} />
    <StudioAlert tone="warning">This backend profile is no longer available.</StudioAlert>
  </div></div>;
  if (profile) return <BackendDetails key={profile.id} connections={connections} profile={profile} navigate={navigate} detailPath={detailPath} featureManagementAvailable={featureManagementAvailable} />;
  return <section className="backends-page" aria-label="Backends">
    <div className="backends-heading"><div><h2>Backends</h2><p>Choose where you design and run workflows.</p></div>{connections.allowPersonal ? <StudioButton variant="primary" type="button" onClick={() => navigate("/settings/backends/new")}><Plus size={16} aria-hidden="true" />Add backend</StudioButton> : null}</div>
    {snapshot.profileError ? <StudioAlert tone="warning">{snapshot.profileError}</StudioAlert> : null}
    <StudioTableContainer role="region" aria-label="Backends table" tabIndex={0}><table className="studio-table backends-list"><thead><tr><th scope="col">Backend</th><th scope="col">Environment</th><th scope="col">Managed by</th><th scope="col">Connection</th></tr></thead><tbody>
      {snapshot.profiles.map(item => <tr key={item.id} {...studioRowInteraction<HTMLTableRowElement>(() => navigate(`/settings/backends/${encodeURIComponent(item.id)}`))}><td><button type="button" className="backend-name-link studio-row-link" onClick={() => navigate(`/settings/backends/${encodeURIComponent(item.id)}`)}><strong>{item.name}</strong><small>{new URL(item.baseUrl).host}</small></button></td><td><span className={item.environment === "Production" ? "backend-production" : "backend-environment"}>{item.environment}</span></td><td>{item.source === "configuration" ? "Configuration" : "You"}</td><td>{snapshot.active?.profile.id === item.id ? "Active" : "Inactive"}</td></tr>)}
    </tbody></table></StudioTableContainer>
    {snapshot.loading ? <p role="status">Loading connections…</p> : null}
  </section>;
}

function BackendDetails({connections,profile,navigate,detailPath,featureManagementAvailable}:{connections:BackendConnections;profile:StudioBackendProfile;navigate:(path:string)=>void;detailPath:string;featureManagementAvailable:boolean}) {
  const snapshot = useSyncExternalStore(connections.subscribe,connections.getSnapshot);
  const [tab,setTab] = useState("connection");
  const tabsId = useId();
  const active = snapshot.active?.profile.id === profile.id;
  const [inspectedCapabilities,setCapabilities] = useState<StudioApiCapabilityDocument|null>(null);
  const capabilities = active ? snapshot.active?.capabilities ?? null : inspectedCapabilities;
  const [error,setError] = useState<string|null>(null);
  const [checking,setChecking] = useState(false);
  const tabOwner = useStudioWorkspaceTab();
  const [deleting,setDeleting] = useState(false);
  const mounted = useRef(true);
  useEffect(() => { mounted.current=true;return () => {mounted.current=false;}; },[]);
  async function remove() {
    setDeleting(true);
    setError(null);
    const current = () => mounted.current && (!tabOwner || tabOwner.isActive());
    try {
      const confirmed = await connections.dialogs.confirm({title:"Delete backend profile?",message:`Remove “${profile.name}” from your personal profiles? Its backend data will remain available.`,confirmLabel:"Delete",tone:"danger"});
      const latest = connections.getSnapshot();
      if (!confirmed || !current() || !latest.profiles.includes(profile) || latest.active?.profile.id === profile.id) return;
      await connections.remove(profile.id);
      if (current()) navigate("/settings/backends");
    } catch (error) {
      if (current()) setError(connectionError(error));
    } finally {
      if (mounted.current) setDeleting(false);
    }
  }
  async function inspect() {
    setChecking(true);setError(null);
    try { const document = await connections.inspect(profile.id); if (mounted.current) setCapabilities(document); }
    catch (error) {if (mounted.current) {setCapabilities(null);setError(connectionError(error));}}
    finally {if (mounted.current) setChecking(false);}
  }
  return <section className="backends-page" aria-label={`${profile.name} backend`}>
    <BackendBreadcrumbs items={[{href:"/settings/backends",label:"Backends"}]} current={profile.name} navigate={navigate} />
    <StudioPageHeader compact headingLevel={2} title={profile.name} description={profile.baseUrl} actions={<>
      <StudioButton variant="primary" type="button" disabled={snapshot.busy || active && !snapshot.active?.disposed && !snapshot.active?.discoveryError} onClick={() => {void connections.connect(profile.id);}}>{active && !snapshot.active?.disposed && !snapshot.active?.discoveryError ? "Connected" : "Connect"}</StudioButton>
      <StudioButton type="button" disabled={checking || snapshot.busy} onClick={() => {void inspect();}}>{checking ? "Checking…" : "Test connection"}</StudioButton>
      {profile.source === "personal" && connections.allowPersonal ? <StudioButton type="button" onClick={() => navigate(`${detailPath}/edit`)}>Edit</StudioButton> : null}
      {active && featureManagementAvailable ? <StudioButton type="button" onClick={() => navigate("/features?target=server")}>Manage features</StudioButton> : null}
    </>} />
    {error ? <StudioAlert tone="warning">{error}</StudioAlert> : null}
    <StudioTabs baseId={tabsId} tabs={[{id:"connection",label:"Connection"},{id:"capabilities",label:"Capabilities"}]} activeTab={tab} onSelect={setTab} ariaLabel="Backend details" />
    <StudioTabPanel baseId={tabsId} index={0} hidden={tab !== "connection"}>
      <dl className="backend-properties"><div><dt>Backend API URL</dt><dd className="backend-url">{profile.baseUrl}</dd></div><div><dt>Environment</dt><dd className={profile.environment === "Production" ? "backend-production" : ""}>{profile.environment}</dd></div><div><dt>Managed by</dt><dd>{profile.source === "configuration" ? "Studio configuration" : "You · personal profile"}</dd></div><div><dt>Connection</dt><dd>{active ? snapshot.active?.discoveryError ? "Unavailable" : "Active in this tab" : "Inactive"}</dd></div></dl>
      <p className="backend-note">{profile.source === "configuration" ? "This profile is managed in your Studio host configuration." : connections.hostIdentity.status === "authenticated" ? "This is a personal profile scoped to your Studio host, user and tenant." : "This personal profile is saved in this browser for the anonymous demo session."}</p>
      {!active && featureManagementAvailable ? <p className="backend-note">Connect to this backend before managing its features and per-feature settings.</p> : null}
      {profile.source === "personal" && connections.allowPersonal ? <StudioButton type="button" className="backend-remove" disabled={active || deleting} onClick={() => {void remove();}}>Delete profile</StudioButton> : null}
    </StudioTabPanel>
    <StudioTabPanel baseId={tabsId} index={1} hidden={tab !== "capabilities"}>
      <p className="backend-note">API functionality advertised by this backend. Feature enablement and settings are managed separately in Features.</p>
      {capabilities ? <ul className="backend-capabilities">{capabilities.capabilities.map(capability => <li key={capability.id}><div><strong>{capability.id}</strong><small>Contract {capability.contractVersion} · {capability.links.length} operations</small></div><span>{capability.status === "dormant" ? "Dormant" : "Advertised"}</span>{capability.reason ? <p>{capability.reason}</p> : null}</li>)}</ul> : <p className="backend-note">{active && snapshot.active?.discoveryError ? snapshot.active.discoveryError : "Test the connection to discover capabilities."}</p>}
      {capabilities?.capabilities.length === 0 ? <p>No API capabilities are advertised.</p> : null}
    </StudioTabPanel>
  </section>;
}

function BackendForm({connections,profile,navigate}:{connections:BackendConnections;profile?:StudioBackendProfile;navigate:(path:string)=>void}) {
  const [name,setName] = useState(profile?.name ?? "");
  const [baseUrl,setBaseUrl] = useState(profile?.baseUrl ?? "");
  const [environment,setEnvironment] = useState<StudioBackendProfile["environment"]>(profile?.environment ?? "Development");
  const [authenticationProfile,setAuthenticationProfile] = useState(profile?.authenticationProfile ?? "");
  const [error,setError] = useState<string|null>(null);
  const [saving,setSaving] = useState(false);
  const saved = useRef(false);
  const writing = useRef(false);
  const owner = useStudioWorkspaceTab();
  const dirty = name !== (profile?.name ?? "") || baseUrl !== (profile?.baseUrl ?? "") || environment !== (profile?.environment ?? "Development") || authenticationProfile !== (profile?.authenticationProfile ?? "");
  useEffect(() => {
    let registered = true;
    const active = () => registered && (!owner || owner.isActive());
    const guard = (event:Event) => {
      if (saved.current || !active() || !dirty && !writing.current) return;
      const detail = (event as CustomEvent<StudioNavigationRequest>).detail;
      if (writing.current || detail?.intent === "probe" || !detail?.confirmWith) {
        event.preventDefault();
        return;
      }
      detail.confirmWith(async () => {
        if (!active() || writing.current) return false;
        const confirmed = await connections.dialogs.confirm({title:"Discard backend profile changes?",message:"Your unsaved changes to this backend profile will be lost.",confirmLabel:"Discard",tone:"danger"});
        return confirmed && active() && !writing.current;
      });
    };
    const unload = (event:BeforeUnloadEvent) => {if (!saved.current && (dirty || writing.current)) event.preventDefault();};
    window.addEventListener(studioNavigationRequestedEvent,guard);
    window.addEventListener("beforeunload",unload);
    return () => {
      registered = false;
      window.removeEventListener(studioNavigationRequestedEvent,guard);
      window.removeEventListener("beforeunload",unload);
    };
  },[connections,owner,dirty,name,baseUrl,environment,authenticationProfile]);
  if (!connections.allowPersonal || profile?.source === "configuration") return <StudioAlert tone="warning">This profile is managed by your Studio host.</StudioAlert>;
  const cancel = () => navigate(profile ? `/settings/backends/${encodeURIComponent(profile.id)}` : "/settings/backends");
  return <section className="backends-page" aria-label={profile ? "Edit backend" : "Add backend"}>
    <div className="backends-heading"><div><BackendBreadcrumbs items={profile
      ? [{href:"/settings/backends",label:"Backends"},{href:`/settings/backends/${encodeURIComponent(profile.id)}`,label:profile.name}]
      : [{href:"/settings/backends",label:"Backends"}]}
      current={profile ? "Edit" : "Add backend"} navigate={navigate} />
      <h2>{profile ? "Edit backend" : "Add backend"}</h2><p>Save a personal connection profile. Your active backend stays unchanged.</p></div></div>
    <form className="backend-form" onSubmit={event => {
      event.preventDefault();
      if (writing.current) return;
      setError(null);
      let url:string;
      try {url=canonicalBackendUrl(baseUrl);} catch(error) {setError(connectionError(error));return;}
      const entry:StudioBackendProfile={id:profile?.id ?? `personal-${backendConnectionNonce()}`,name,baseUrl:url,environment,source:"personal",...(authenticationProfile ? {authenticationProfile} : {})};
      writing.current=true;setSaving(true);
      void connections.save(entry).then(() => {saved.current=true;navigate(`/settings/backends/${encodeURIComponent(entry.id)}`);}).catch(error => setError(connectionError(error))).finally(() => {writing.current=false;setSaving(false);});
    }}>
      <StudioField label="Name"><input required maxLength={80} value={name} onChange={event => setName(event.target.value)} disabled={saving} /></StudioField>
      <StudioField label="Backend API URL"><input required type="url" aria-label="Backend API URL" maxLength={2048} value={baseUrl} onChange={event => setBaseUrl(event.target.value)} disabled={saving} /><small>Include the full shell path, when applicable.</small></StudioField>
      <StudioField label="Environment"><select value={environment} onChange={event => setEnvironment(event.target.value as StudioBackendProfile["environment"])} disabled={saving}><option>Development</option><option>Test</option><option>Production</option></select></StudioField>
      {Object.keys(connections.config.backends?.authenticationProfiles ?? {}).length ? <StudioField label="Authentication"><select value={authenticationProfile} onChange={event => setAuthenticationProfile(event.target.value)} disabled={saving}><option value="">Discover on connection</option>{Object.keys(connections.config.backends?.authenticationProfiles ?? {}).map(id => <option key={id}>{id}</option>)}</select></StudioField> : null}
      {error ? <StudioAlert tone="danger">{error}</StudioAlert> : null}
      <p className="backend-note">Credentials are requested when you connect and are never saved in this profile.</p>
      <div className="backend-actions"><StudioButton type="submit" variant="primary" disabled={saving}>{saving ? "Saving…" : profile ? "Save" : "Create backend"}</StudioButton><StudioButton type="button" onClick={cancel} disabled={saving}>Cancel</StudioButton></div>
    </form>
  </section>;
}
