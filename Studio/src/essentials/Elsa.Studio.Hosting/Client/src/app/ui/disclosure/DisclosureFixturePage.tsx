import React, { useState } from "react";
import { StudioField } from "../forms/Field";
import { AddOption } from "./AddOption";
import { AdvancedSection } from "./AdvancedSection";
import { ExpressionToggle, type ExpressionMode } from "./ExpressionToggle";
import { OverflowMenu } from "./OverflowMenu";
import { PeerViewSwitch } from "./PeerViewSwitch";
import { RevealToggle } from "./RevealToggle";
import { TechnicalDetails } from "./TechnicalDetails";

const optionalInputs = [
  { id: "timeout", label: "Timeout", description: "Request timeout in milliseconds" },
  { id: "contentType", label: "Content type" },
  { id: "statusCodes", label: "Expected status codes" },
  { id: "authentication", label: "Authentication" },
  { id: "proxy", label: "Proxy" },
  { id: "maxSize", label: "Max response size" },
  { id: "allowedHosts", label: "Allowed hosts" },
  { id: "retryAfter", label: "Retry-after" },
  { id: "followRedirects", label: "Follow redirects" }
];

export function DisclosureFixturePage({
  title = "Disclosure kit"
}: {
  title?: string;
}) {
  const [addedIds, setAddedIds] = useState(["timeout"]);
  const [sendHeaders, setSendHeaders] = useState(true);
  const [sendQuery, setSendQuery] = useState(false);
  const [urlMode, setUrlMode] = useState<ExpressionMode>("expression");
  const [methodMode, setMethodMode] = useState<ExpressionMode>("fixed");
  const [view, setView] = useState("designer");
  const [lastAction, setLastAction] = useState("None yet");

  return (
    <main className="studio-disclosure-fixture" aria-label="Disclosure kit fixture">
      <header className="studio-disclosure-fixture-header">
        <div>
          <h1>{title}</h1>
          <p>Shared progressive-disclosure primitives. A non-default, bound or invalid field stays visible.</p>
        </div>
        <PeerViewSwitch
          views={[{ id: "designer", label: "Designer" }, { id: "code", label: "Code" }]}
          value={view}
          onChange={setView}
          ariaLabel="Peer view"
        />
      </header>

      <div className="studio-disclosure-fixture-grid">
        <section className="studio-disclosure-fixture-card">
          <h2>Add option</h2>
          <p>Optional fields one at a time. Timeout is already present because it is non-default. Remove resets it.</p>
          <AddOption
            options={optionalInputs.map(item => item.id === "timeout"
              ? { ...item, isNonDefault: true }
              : item)}
            addedIds={addedIds}
            onAdd={id => setAddedIds(current => current.includes(id) ? current : [...current, id])}
            onRemove={id => setAddedIds(current => current.filter(item => item !== id))}
            renderField={item => (
              <StudioField label={item.label}>
                <input className="studio-disclosure-fixture-demo-input" defaultValue={item.id === "timeout" ? "10000" : ""} />
              </StudioField>
            )}
          />
        </section>

        <section className="studio-disclosure-fixture-card">
          <h2>Reveal toggle</h2>
          <p>A switch reveals a group. Turning it off keeps the values and marks them ignored.</p>
          <RevealToggle label="Send headers" checked={sendHeaders} onCheckedChange={setSendHeaders}>
            <StudioField label="Headers">
              <input className="studio-disclosure-fixture-demo-input" defaultValue="Authorization: Bearer …" />
            </StudioField>
          </RevealToggle>
          <RevealToggle
            label="Send query parameters"
            checked={sendQuery}
            onCheckedChange={setSendQuery}
            forceOpen
          >
            <StudioField label="Query">
              <input className="studio-disclosure-fixture-demo-input" defaultValue="orderId=42" />
            </StudioField>
          </RevealToggle>
        </section>

        <section className="studio-disclosure-fixture-card">
          <h2>Advanced section</h2>
          <p>Collapsed by default. Opens itself when something is set or invalid. Badge shows the count.</p>
          <AdvancedSection label="Advanced" summary="retries, proxy, TLS, max response size, allowed hosts" setCount={1} defaultOpen>
            <StudioField label="Retries" description="Changed from the default of 0.">
              <input className="studio-disclosure-fixture-demo-input" defaultValue="3" />
            </StudioField>
          </AdvancedSection>
        </section>

        <section className="studio-disclosure-fixture-card">
          <h2>Technical details</h2>
          <p>Read-only diagnostics at the end of a surface. Never holds inputs.</p>
          <TechnicalDetails label="Technical details" summary="feature version, config source, registered endpoints" defaultOpen>
            <dl>
              <dt>Feature version</dt>
              <dd>Elsa.Http 4.0.0-preview</dd>
              <dt>Config source</dt>
              <dd>appsettings.json</dd>
              <dt>Registered endpoints</dt>
              <dd>12</dd>
            </dl>
          </TechnicalDetails>
        </section>

        <section className="studio-disclosure-fixture-card">
          <h2>Expression toggle</h2>
          <p>Fixed / Expression appears on hover or focus. It stays visible in Expression or when the value is non-default.</p>
          <ExpressionToggle label="URL" value={urlMode} onChange={setUrlMode}>
            <input className="studio-disclosure-fixture-demo-input" defaultValue="${input.orderId}" />
          </ExpressionToggle>
          <ExpressionToggle label="Method" value={methodMode} onChange={setMethodMode}>
            <input className="studio-disclosure-fixture-demo-input" defaultValue="POST" />
          </ExpressionToggle>
        </section>

        <section className="studio-disclosure-fixture-card">
          <div className="studio-disclosure-fixture-inline">
            <h2>Overflow menu</h2>
            <OverflowMenu
              defaultOpen
              items={[
                { id: "retry", label: "Retry", onSelect: () => setLastAction("Retry") },
                { id: "copy", label: "Copy ID", onSelect: () => setLastAction("Copy ID") },
                { id: "export", label: "Export JSON", onSelect: () => setLastAction("Export JSON") },
                { id: "cancel", label: "Cancel", destructive: true, onSelect: () => setLastAction("Cancel") },
                { id: "delete", label: "Delete", destructive: true, onSelect: () => setLastAction("Delete") }
              ]}
            />
          </div>
          <p>Secondary actions behind ⋯. Destructive items are last and separated. Last action: {lastAction}.</p>
          <span>Order intake run</span>
        </section>
      </div>
    </main>
  );
}
