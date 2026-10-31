import React, { useEffect, useId, useRef, useState, useSyncExternalStore } from "react";
import { ArrowLeft, ChevronDown, Plus, Server } from "lucide-react";
import { studioNavigationRequestedEvent, type StudioApiCapabilityDocument, type StudioBackendProfile } from "../../sdk";
import { AnchoredPopover, StudioAlert, StudioTabs, StudioTabPanel } from "../ui";
import { BackendConnections, canonicalBackendUrl, connectionError } from "./connections";
import "./backends.css";

export const backendNavigation = {id:"backends",label:"Backends",path:"/settings/backends",activePathPrefix:"/settings/backends",placement:"settings" as const,order:20};

export function BackendSwitcher({connections,onNavigate}:{connections:BackendConnections;onNavigate:(path:string)=>void}) {
  const snapshot = useSyncExternalStore(connections.subscribe,connections.getSnapshot);
  const [open,setOpen] = useState(false);
  const container = useRef<HTMLDivElement>(null);
  const button = useRef<HTMLButtonElement>(null);
  const active = snapshot.active;
  return <div className="backend-switcher" ref={container} onKeyDown={event => { if (event.key === "Escape") { setOpen(false); button.current?.focus(); } }}>
    <button ref={button} type="button" className="backend-switcher-trigger" aria-label={`Select backend. Current: ${active?.profile.name ?? "No connection"}`} aria-expanded={open} onClick={() => setOpen(value => !value)}>
      <Server size={17} aria-hidden="true" /><span><strong>{active?.profile.name ?? "Choose backend"}</strong><small>{active?.profile.environment === "Production" ? "Production · " : ""}{active ? new URL(active.profile.baseUrl).host : "Not connected"}</small></span><ChevronDown size={14} aria-hidden="true" />
    </button>
    <AnchoredPopover open={open} anchorRef={button} className="backend-switcher-popover" minWidth={260} maxHeight={420} onDismiss={() => setOpen(false)}>
      <div aria-label="Backend connections">
      <strong>Connect to backend</strong>
      {snapshot.profiles.map(profile => <button type="button" key={profile.id} disabled={snapshot.busy || profile.id === active?.profile.id && !active?.disposed && !active?.discoveryError} onClick={() => { setOpen(false); void connections.connect(profile.id); }}>
        <span><strong>{profile.name}</strong><small>{profile.environment} · {new URL(profile.baseUrl).host}</small></span><span>{profile.id === active?.profile.id ? "Active" : "Connect"}</span>
      </button>)}
      <button type="button" onClick={() => {setOpen(false);onNavigate("/settings/backends");}}>Manage backends</button>
    </div></AnchoredPopover>
  </div>;
}

export function BackendConnectionNotice({connections}:{connections:BackendConnections}) {
  const snapshot = useSyncExternalStore(connections.subscribe,connections.getSnapshot);
  if (!snapshot.error && !snapshot.busy && !snapshot.signInProfile) return null;
  return <div className="backend-connection-notice" role="status" aria-live="polite">
    {snapshot.error ? <StudioAlert tone="warning">{snapshot.error}</StudioAlert> : null}
    {snapshot.busy ? <span>Checking backend connection… <button type="button" onClick={() => connections.cancelConnection()}>Cancel</button></span> : null}
    {snapshot.signInProfile ? <button type="button" onClick={() => {void connections.signIn().catch(error => window.alert(connectionError(error)));}}>Sign in to {snapshot.profiles.find(profile => profile.id === snapshot.signInProfile)?.name}</button> : null}
  </div>;
}

export function BackendsPage({connections,navigate,path=window.location.pathname,featureManagementAvailable=false}:{connections:BackendConnections;navigate:(path:string)=>void;path?:string;featureManagementAvailable?:boolean}) {
  const snapshot = useSyncExternalStore(connections.subscribe,connections.getSnapshot);
  const segment = path.slice("/settings/backends".length).split("/").filter(Boolean);
  const create = segment[0] === "new";
  let profileId:string|undefined;
  try {profileId = segment[0] ? decodeURIComponent(segment[0]) : undefined;} catch {profileId = undefined;}
  const profile = profileId ? snapshot.profiles.find(item => item.id === profileId) : undefined;
  const edit = !!profile && segment[1] === "edit";
  const detailPath = profile ? `/settings/backends/${encodeURIComponent(profile.id)}` : "/settings/backends";
  if (create || edit) return <BackendForm key={create ? "new" : profile!.id} connections={connections} profile={edit ? profile : undefined} navigate={navigate} />;
  if (segment.length && !profile) return <div className="backends-page"><button type="button" onClick={() => navigate("/settings/backends")}>Back to backends</button><StudioAlert tone="warning">This backend profile is no longer available.</StudioAlert></div>;
  if (profile) return <BackendDetails key={profile.id} connections={connections} profile={profile} navigate={navigate} detailPath={detailPath} featureManagementAvailable={featureManagementAvailable} />;
  return <section className="backends-page" aria-label="Backends">
    <div className="backends-heading"><div><h2>Backends</h2><p>Choose where you design and run workflows.</p></div>{connections.allowPersonal ? <button className="primary" type="button" onClick={() => navigate("/settings/backends/new")}><Plus size={16} aria-hidden="true" />Add backend</button> : null}</div>
    {snapshot.profileError ? <StudioAlert tone="warning">{snapshot.profileError}</StudioAlert> : null}
    <table className="backends-list"><thead><tr><th scope="col">Backend</th><th scope="col">Environment</th><th scope="col">Managed by</th><th scope="col">Connection</th></tr></thead><tbody>
      {snapshot.profiles.map(item => <tr key={item.id}><td><button type="button" className="backend-name-link" onClick={() => navigate(`/settings/backends/${encodeURIComponent(item.id)}`)}><strong>{item.name}</strong><small>{new URL(item.baseUrl).host}</small></button></td><td><span className={item.environment === "Production" ? "backend-production" : "backend-environment"}>{item.environment}</span></td><td>{item.source === "configuration" ? "Configuration" : "You"}</td><td>{snapshot.active?.profile.id === item.id ? "Active" : "Inactive"}</td></tr>)}
    </tbody></table>
    {snapshot.loading ? <p role="status">Loading connections…</p> : null}
  </section>;
}

function BackendDetails({connections,profile,navigate,detailPath,featureManagementAvailable}:{connections:BackendConnections;profile:StudioBackendProfile;navigate:(path:string)=>void;detailPath:string;featureManagementAvailable:boolean}) {
  const snapshot = useSyncExternalStore(connections.subscribe,connections.getSnapshot);
  const [tab,setTab] = useState("connection");
  const tabsId = useId();
  const active = snapshot.active?.profile.id === profile.id;
  const [capabilities,setCapabilities] = useState<StudioApiCapabilityDocument|null>(active ? snapshot.active?.capabilities ?? null : null);
  const [error,setError] = useState<string|null>(null);
  const [checking,setChecking] = useState(false);
  const mounted = useRef(true);
  useEffect(() => { mounted.current=true;return () => {mounted.current=false;}; },[]);
  async function inspect() {
    setChecking(true);setError(null);
    try { const document = await connections.inspect(profile.id); if (mounted.current) setCapabilities(document); }
    catch (error) {if (mounted.current) setError(connectionError(error));}
    finally {if (mounted.current) setChecking(false);}
  }
  return <section className="backends-page" aria-label={`${profile.name} backend`}>
    <button className="backend-back" type="button" onClick={() => navigate("/settings/backends")}><ArrowLeft size={15} aria-hidden="true" />Backends</button>
    <div className="backends-heading"><div><h2>{profile.name}</h2><p className="backend-url">{profile.baseUrl}</p></div><div className="backend-actions">
      <button className="primary" type="button" disabled={snapshot.busy || active && !snapshot.active?.disposed && !snapshot.active?.discoveryError} onClick={() => {void connections.connect(profile.id);}}>{active && !snapshot.active?.disposed && !snapshot.active?.discoveryError ? "Connected" : "Connect"}</button>
      <button type="button" disabled={checking || snapshot.busy} onClick={() => {void inspect();}}>{checking ? "Checking…" : "Test connection"}</button>
      {profile.source === "personal" && connections.allowPersonal ? <button type="button" onClick={() => navigate(`${detailPath}/edit`)}>Edit</button> : null}
      {active && featureManagementAvailable ? <button type="button" onClick={() => navigate("/features?target=server")}>Manage features</button> : null}
    </div></div>
    {error ? <StudioAlert tone="warning">{error}</StudioAlert> : null}
    <StudioTabs baseId={tabsId} tabs={[{id:"connection",label:"Connection"},{id:"capabilities",label:"Capabilities"}]} activeTab={tab} onSelect={setTab} ariaLabel="Backend details" />
    <StudioTabPanel baseId={tabsId} index={0} hidden={tab !== "connection"}>
      <dl className="backend-properties"><div><dt>Backend API URL</dt><dd className="backend-url">{profile.baseUrl}</dd></div><div><dt>Environment</dt><dd className={profile.environment === "Production" ? "backend-production" : ""}>{profile.environment}</dd></div><div><dt>Managed by</dt><dd>{profile.source === "configuration" ? "Studio configuration" : "You · personal profile"}</dd></div><div><dt>Connection</dt><dd>{active ? snapshot.active?.discoveryError ? "Unavailable" : "Active in this tab" : "Inactive"}</dd></div></dl>
      <p className="backend-note">{profile.source === "configuration" ? "This profile is managed in your Studio host configuration." : connections.hostIdentity.status === "authenticated" ? "This is a personal profile scoped to your Studio host, user and tenant." : "This personal profile is saved in this browser for the anonymous demo session."}</p>
      {!active && featureManagementAvailable ? <p className="backend-note">Connect to this backend before managing its features and per-feature settings.</p> : null}
      {profile.source === "personal" && connections.allowPersonal ? <button type="button" className="backend-remove" disabled={active} onClick={() => { if (window.confirm(`Remove connection profile “${profile.name}”? Backend data is unchanged.`)) void connections.remove(profile.id).then(() => navigate("/settings/backends")).catch(error => setError(connectionError(error))); }}>Remove backend</button> : null}
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
  const dirty = name !== (profile?.name ?? "") || baseUrl !== (profile?.baseUrl ?? "") || environment !== (profile?.environment ?? "Development") || authenticationProfile !== (profile?.authenticationProfile ?? "");
  useEffect(() => {
    const guard = (event:Event) => { if (!saved.current && (saving || dirty && !window.confirm("Discard the unsaved backend profile?"))) event.preventDefault(); };
    const unload = (event:BeforeUnloadEvent) => {if (saving || dirty && !saved.current) event.preventDefault();};
    window.addEventListener(studioNavigationRequestedEvent,guard);window.addEventListener("beforeunload",unload);
    return () => {window.removeEventListener(studioNavigationRequestedEvent,guard);window.removeEventListener("beforeunload",unload);};
  },[dirty,saving]);
  if (!connections.allowPersonal || profile?.source === "configuration") return <StudioAlert tone="warning">This profile is managed by your Studio host.</StudioAlert>;
  const cancel = () => navigate(profile ? `/settings/backends/${encodeURIComponent(profile.id)}` : "/settings/backends");
  return <section className="backends-page" aria-label={profile ? "Edit backend" : "Add backend"}>
    <button className="backend-back" type="button" onClick={cancel}><ArrowLeft size={15} aria-hidden="true" />{profile?.name ?? "Backends"}</button>
    <div className="backends-heading"><div><h2>{profile ? "Edit backend" : "Add backend"}</h2><p>Save a personal connection profile. Your active backend stays unchanged.</p></div></div>
    <form className="backend-form" onSubmit={event => {
      event.preventDefault();setError(null);setSaving(true);
      let url:string;
      try {url=canonicalBackendUrl(baseUrl);} catch(error) {setError(connectionError(error));setSaving(false);return;}
      const entry:StudioBackendProfile={id:profile?.id ?? `personal-${crypto.randomUUID()}`,name,baseUrl:url,environment,source:"personal",...(authenticationProfile ? {authenticationProfile} : {})};
      void connections.save(entry).then(() => {saved.current=true;navigate(`/settings/backends/${encodeURIComponent(entry.id)}`);}).catch(error => setError(connectionError(error))).finally(() => setSaving(false));
    }}>
      <label>Name<input required maxLength={80} value={name} onChange={event => setName(event.target.value)} disabled={saving} /></label>
      <label>Backend API URL<input required type="url" aria-label="Backend API URL" maxLength={2048} value={baseUrl} onChange={event => setBaseUrl(event.target.value)} disabled={saving} /><small>Include the full shell path, when applicable.</small></label>
      <label>Environment<select value={environment} onChange={event => setEnvironment(event.target.value as StudioBackendProfile["environment"])} disabled={saving}><option>Development</option><option>Test</option><option>Production</option></select></label>
      {Object.keys(connections.config.backends?.authenticationProfiles ?? {}).length ? <label>Authentication<select value={authenticationProfile} onChange={event => setAuthenticationProfile(event.target.value)} disabled={saving}><option value="">Discover on connection</option>{Object.keys(connections.config.backends?.authenticationProfiles ?? {}).map(id => <option key={id}>{id}</option>)}</select></label> : null}
      {error ? <StudioAlert tone="danger">{error}</StudioAlert> : null}
      <p className="backend-note">Credentials are requested when you connect and are never saved in this profile.</p>
      <div className="backend-actions"><button type="submit" className="primary" disabled={saving}>{saving ? "Saving…" : profile ? "Save changes" : "Create backend"}</button><button type="button" onClick={cancel} disabled={saving}>Cancel</button></div>
    </form>
  </section>;
}
