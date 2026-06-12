import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import * as signalR from "@microsoft/signalr";
import {
  Activity,
  Boxes,
  Check,
  ChevronRight,
  Circle,
  CloudUpload,
  Code2,
  FileJson,
  PackagePlus,
  Play,
  PlugZap,
  Radio,
  RefreshCw,
  Rocket,
  Save,
  Server,
  Terminal,
  Upload
} from "lucide-react";

const sampleWorkflows = {
  hello: {
    label: "Hello World",
    activityTokens: {
      "{{writeLineActivityVersionId}}": "WriteLine"
    },
    value: {
      name: "Hello World",
      description: "Writes Hello World through the WriteLine activity.",
      state: {
        variables: [],
        rootActivity: {
          nodeId: "write-hello-world",
          activityVersionId: "{{writeLineActivityVersionId}}",
          inputs: [
            {
              referenceKey: "Text",
              value: {
                value: "Hello World",
                expressionType: "Literal"
              },
              autoEvaluate: null,
              evaluatorType: null,
              storageDriverType: null,
              isSensitive: null
            }
          ],
          outputs: [],
          childSlots: null
        },
        inputs: [],
        outputs: [],
        workflowActivityOptions: null,
        strategyOptions: null
      }
    }
  },
  sequence: {
    label: "Sequence Write Lines",
    activityTokens: {
      "{{writeLineActivityVersionId}}": "WriteLine",
      "{{sequenceActivityVersionId}}": "Sequence"
    },
    value: {
      name: "Sequence Write Lines",
      description: "Runs two WriteLine activities inside a Sequence root activity.",
      state: {
        variables: [],
        rootActivity: {
          nodeId: "sequence-root",
          activityVersionId: "{{sequenceActivityVersionId}}",
          inputs: [],
          outputs: [],
          childSlots: [
            {
              name: "Sequence.Activities",
              activities: [
                {
                  nodeId: "write-sequence-line-one",
                  activityVersionId: "{{writeLineActivityVersionId}}",
                  inputs: [
                    {
                      referenceKey: "Text",
                      value: {
                        value: "Sequence line 1",
                        expressionType: "Literal"
                      },
                      autoEvaluate: null,
                      evaluatorType: null,
                      storageDriverType: null,
                      isSensitive: null
                    }
                  ],
                  outputs: [],
                  childSlots: null
                },
                {
                  nodeId: "write-sequence-line-two",
                  activityVersionId: "{{writeLineActivityVersionId}}",
                  inputs: [
                    {
                      referenceKey: "Text",
                      value: {
                        value: "Sequence line 2",
                        expressionType: "Literal"
                      },
                      autoEvaluate: null,
                      evaluatorType: null,
                      storageDriverType: null,
                      isSensitive: null
                    }
                  ],
                  outputs: [],
                  childSlots: null
                }
              ],
              metadata: null
            }
          ]
        },
        inputs: [],
        outputs: [],
        workflowActivityOptions: null,
        strategyOptions: null
      }
    }
  },
  nuplane: {
    label: "Nuplane Activity",
    activityTokens: {
      "{{sayHelloFromNuplaneActivityVersionId}}": "SayHelloFromNuplane"
    },
    value: {
      name: "Say Hello From Nuplane",
      description: "Runs the SayHelloFromNuplane activity loaded from a Nuplane package.",
      state: {
        variables: [],
        rootActivity: {
          nodeId: "say-hello-from-nuplane",
          activityVersionId: "{{sayHelloFromNuplaneActivityVersionId}}",
          inputs: [
            {
              referenceKey: "Recipient",
              value: {
                value: "World",
                expressionType: "Literal"
              },
              autoEvaluate: null,
              evaluatorType: null,
              storageDriverType: null,
              isSensitive: null
            }
          ],
          outputs: [],
          childSlots: null
        },
        inputs: [],
        outputs: [],
        workflowActivityOptions: null,
        strategyOptions: null
      }
    }
  }
};

const steps = [
  { key: "save", label: "Save workflow", icon: Save },
  { key: "publish", label: "Publish", icon: Rocket },
  { key: "execute", label: "Execute", icon: Play },
  { key: "upload", label: "Upload package", icon: PackagePlus },
  { key: "feature", label: "Enable feature", icon: PlugZap },
  { key: "reload", label: "Reload shells", icon: RefreshCw },
  { key: "newActivity", label: "Run new activity", icon: Activity }
];

const initialWorkflow = JSON.stringify(sampleWorkflows.hello.value, null, 2);

export function App() {
  const [selectedSample, setSelectedSample] = useState("hello");
  const [workflowJson, setWorkflowJson] = useState(initialWorkflow);
  const [workflowVersionId, setWorkflowVersionId] = useState("");
  const [artifactId, setArtifactId] = useState("");
  const [executionId, setExecutionId] = useState("");
  const [status, setStatus] = useState("Ready");
  const [busy, setBusy] = useState("");
  const [completed, setCompleted] = useState(new Set());
  const [packages, setPackages] = useState([]);
  const [events, setEvents] = useState([]);
  const [shellJson, setShellJson] = useState("");
  const [features, setFeatures] = useState([]);
  const [featureName, setFeatureName] = useState("SampleNuplaneActivities");
  const [featureConfig, setFeatureConfig] = useState("{}");
  const [packageAlert, setPackageAlert] = useState(false);
  const [consoleLines, setConsoleLines] = useState([]);
  const [consoleConnected, setConsoleConnected] = useState(false);
  const [dropFolder, setDropFolder] = useState("");
  const lastEventSequence = useRef(0);
  const activityVersionCache = useRef(new Map());

  const currentStep = useMemo(() => {
    const firstOpen = steps.find((step) => !completed.has(step.key));
    return firstOpen?.key ?? "newActivity";
  }, [completed]);

  const addConsoleLine = useCallback((stream, text) => {
    setConsoleLines((current) => [
      ...current.slice(-249),
      {
        id: `${Date.now()}-${Math.random()}`,
        timestamp: new Date().toISOString(),
        stream,
        text
      }
    ]);
  }, []);

  const markComplete = useCallback((key) => {
    setCompleted((current) => new Set([...current, key]));
  }, []);

  const request = useCallback(async (url, options = {}) => {
    const hasJsonBody = options.body && !(options.body instanceof FormData);
    const response = await fetch(url, {
      ...options,
      headers: {
        ...(hasJsonBody ? { "Content-Type": "application/json" } : {}),
        ...(options.headers ?? {})
      }
    });

    if (!response.ok) {
      let errorText = `${response.status} ${response.statusText}`;
      try {
        const body = await response.json();
        errorText = body.error ?? errorText;
      } catch {
        errorText = await response.text();
      }
      throw new Error(errorText);
    }

    if (response.status === 204)
      return null;

    return await response.json();
  }, []);

  const refreshState = useCallback(async () => {
    const [state, shell] = await Promise.all([
      request("/_demo/state"),
      request("/_demo/shells/default")
    ]);

    setDropFolder(state.packageDropFolder);
    setPackages(state.packages ?? []);
    setEvents(state.packageEvents ?? []);
    setShellJson(shell.json);
    setFeatures(shell.features ?? []);
    const maxSequence = Math.max(0, ...(state.packageEvents ?? []).map((event) => event.sequence));
    lastEventSequence.current = Math.max(lastEventSequence.current, maxSequence);
  }, [request]);

  useEffect(() => {
    refreshState().catch((error) => addConsoleLine("stderr", `State refresh failed: ${error.message}`));
  }, [addConsoleLine, refreshState]);

  useEffect(() => {
    const interval = window.setInterval(async () => {
      try {
        const newEvents = await request(`/_demo/packages/events?afterSequence=${lastEventSequence.current}`);
        if (newEvents.length === 0)
          return;

        setEvents((current) => [...current, ...newEvents].slice(-80));
        lastEventSequence.current = Math.max(...newEvents.map((event) => event.sequence));

        if (newEvents.some((event) => event.kind === "changed" && event.hasPackageChanges)) {
          setPackageAlert(true);
          markComplete("upload");
        }

        newEvents.forEach((event) => addConsoleLine("stdout", `[nuplane] ${event.message}`));
        const latestPackages = newEvents.at(-1)?.activePackages;
        if (latestPackages)
          setPackages(latestPackages);
      } catch (error) {
        addConsoleLine("stderr", `Package event poll failed: ${error.message}`);
      }
    }, 2500);

    return () => window.clearInterval(interval);
  }, [addConsoleLine, markComplete, request]);

  useEffect(() => {
    let cancelled = false;
    const connection = new signalR.HubConnectionBuilder()
      .withUrl("/diagnostics/console-logs/hub")
      .withAutomaticReconnect()
      .build();

    connection.onreconnecting(() => setConsoleConnected(false));
    connection.onreconnected(() => setConsoleConnected(true));
    connection.onclose(() => setConsoleConnected(false));

    async function connect() {
      try {
        await connection.start();
        if (cancelled)
          return;

        setConsoleConnected(true);
        const stream = connection.stream("StreamAsync", { limit: 200 });
        stream.subscribe({
          next: (item) => {
            if (item?.line) {
              addConsoleLine(item.line.stream === 1 ? "stderr" : "stdout", item.line.text);
            } else if (item?.droppedLines) {
              addConsoleLine("stderr", `${item.droppedLines.count} console lines were dropped.`);
            }
          },
          error: (error) => addConsoleLine("stderr", `Console stream failed: ${error.message}`),
          complete: () => addConsoleLine("stdout", "Console stream completed.")
        });
      } catch (error) {
        setConsoleConnected(false);
        addConsoleLine("stderr", `Console stream connection failed: ${error.message}`);
      }
    }

    connect();
    return () => {
      cancelled = true;
      connection.stop();
    };
  }, [addConsoleLine]);

  function selectSample(key) {
    setSelectedSample(key);
    setWorkflowJson(JSON.stringify(sampleWorkflows[key].value, null, 2));
    setWorkflowVersionId("");
    setArtifactId("");
    setExecutionId("");
  }

  async function resolveActivityVersion(searchTerm) {
    if (activityVersionCache.current.has(searchTerm))
      return activityVersionCache.current.get(searchTerm);

    const definitions = await request(`/default/design/activities/definitions?searchTerm=${encodeURIComponent(searchTerm)}`);
    if (!Array.isArray(definitions) || definitions.length === 0)
      throw new Error(`Activity '${searchTerm}' is not available in the catalog.`);

    const versions = await request(`/default/design/activities/definitions/${definitions[0].id}/versions`);
    if (!Array.isArray(versions) || versions.length === 0)
      throw new Error(`Activity '${searchTerm}' has no versions.`);

    activityVersionCache.current.set(searchTerm, versions[0].id);
    return versions[0].id;
  }

  async function materializeWorkflowJson() {
    let text = workflowJson;
    const knownTokens = {
      "{{writeLineActivityVersionId}}": "WriteLine",
      "{{sequenceActivityVersionId}}": "Sequence",
      "{{sayHelloFromNuplaneActivityVersionId}}": "SayHelloFromNuplane",
      ...(sampleWorkflows[selectedSample]?.activityTokens ?? {})
    };

    for (const [token, searchTerm] of Object.entries(knownTokens)) {
      if (text.includes(token)) {
        const versionId = await resolveActivityVersion(searchTerm);
        text = text.replaceAll(token, versionId);
      }
    }

    return JSON.parse(text);
  }

  async function runAction(stepKey, message, action) {
    setBusy(stepKey);
    setStatus(message);
    addConsoleLine("stdout", message);
    try {
      const result = await action();
      markComplete(stepKey);
      setStatus("Ready");
      return result;
    } catch (error) {
      setStatus(error.message);
      addConsoleLine("stderr", error.message);
      throw error;
    } finally {
      setBusy("");
    }
  }

  async function saveWorkflow() {
    await runAction("save", "Submitting workflow definition...", async () => {
      const body = await materializeWorkflowJson();
      const response = await request("/default/design/workflows/definitions/submit", {
        method: "POST",
        body: JSON.stringify(body)
      });
      setWorkflowVersionId(response.version.id);
      setArtifactId("");
      setExecutionId("");
      addConsoleLine("stdout", `Workflow saved: version ${response.version.id}`);
    });
  }

  async function publishWorkflow() {
    if (!workflowVersionId)
      throw new Error("Save a workflow before publishing.");

    await runAction("publish", "Publishing workflow executable...", async () => {
      const response = await request(`/default/publishing/workflows/${workflowVersionId}/publish`, {
        method: "POST",
        body: "{}"
      });
      setArtifactId(response.artifactId);
      addConsoleLine("stdout", `Published artifact: ${response.artifactId}`);
    });
  }

  async function executeWorkflow(stepKey = "execute") {
    if (!artifactId)
      throw new Error("Publish the workflow before executing.");

    await runAction(stepKey, "Executing workflow artifact...", async () => {
      const response = await request(`/default/runtime/workflows/${artifactId}/execute`, {
        method: "POST",
        body: "{}"
      });
      setExecutionId(response.workflowExecutionId ?? "");
      addConsoleLine("stdout", `Execution started: ${response.workflowExecutionId ?? "accepted"}`);
    });
  }

  async function uploadPackage(event) {
    const file = event.target.files?.[0];
    if (!file)
      return;

    await runAction("upload", `Uploading ${file.name}...`, async () => {
      const body = new FormData();
      body.append("package", file);
      const response = await request("/_demo/packages/upload", {
        method: "POST",
        body
      });
      addConsoleLine("stdout", `Package saved: ${response.path}`);

      const reconcile = await request("/_demo/packages/reconcile", {
        method: "POST",
        body: "{}"
      });
      addConsoleLine("stdout", `Reconcile ${reconcile.outcome}: ${reconcile.correlationId}`);
      await refreshState();
    });
  }

  async function enableFeature() {
    await runAction("feature", `Enabling feature ${featureName}...`, async () => {
      const configuration = JSON.parse(featureConfig || "{}");
      const response = await request(`/_demo/shells/default/features/${encodeURIComponent(featureName)}`, {
        method: "PUT",
        body: JSON.stringify({ enabled: true, configuration })
      });
      setShellJson(response.json);
      await refreshState();
      addConsoleLine("stdout", `Feature enabled: ${featureName}`);
    });
  }

  async function saveShellJson() {
    await runAction("feature", "Saving shells.json...", async () => {
      const parsed = JSON.parse(shellJson);
      const response = await request("/_demo/shells/default", {
        method: "PUT",
        body: JSON.stringify(parsed)
      });
      setShellJson(response.json);
      await refreshState();
      addConsoleLine("stdout", "shells.json saved.");
    });
  }

  async function reloadShells() {
    await runAction("reload", "Reloading all shells...", async () => {
      await request("/_admin/shells/reload-all", {
        method: "POST",
        body: "{}"
      });
      setPackageAlert(false);
      activityVersionCache.current.clear();
      await refreshState();
      addConsoleLine("stdout", "Shells reloaded.");
    });
  }

  return (
    <div className="app-shell">
      <header className="topbar">
        <div className="brand">
          <div className="brand-mark"><Boxes size={19} /></div>
          <div>
            <h1>Elsa Dynamic Runtime Demo</h1>
            <p>{status}</p>
          </div>
        </div>
        <div className="backend-status">
          <span className={consoleConnected ? "status-dot online" : "status-dot"} />
          <span>{consoleConnected ? "Console connected" : "Console offline"}</span>
          <span className="divider" />
          <Server size={16} />
          <span>/default</span>
        </div>
      </header>

      <main className="workspace">
        <StepRail completed={completed} currentStep={currentStep} />

        <section className="editor-surface">
          <div className="panel-titlebar">
            <div>
              <h2><FileJson size={18} /> Workflow Definition</h2>
              <p>Submit, publish, and execute against the mounted default shell.</p>
            </div>
            <select value={selectedSample} onChange={(event) => selectSample(event.target.value)}>
              {Object.entries(sampleWorkflows).map(([key, sample]) => (
                <option key={key} value={key}>{sample.label}</option>
              ))}
            </select>
          </div>

          <div className="toolbar">
            <ActionButton icon={Save} busy={busy === "save"} onClick={saveWorkflow}>Save</ActionButton>
            <ActionButton icon={Rocket} busy={busy === "publish"} onClick={publishWorkflow} disabled={!workflowVersionId}>Publish</ActionButton>
            <ActionButton icon={Play} busy={busy === "execute"} onClick={() => executeWorkflow("execute")} disabled={!artifactId}>Execute</ActionButton>
          </div>

          <textarea
            className="code-editor"
            value={workflowJson}
            spellCheck={false}
            onChange={(event) => setWorkflowJson(event.target.value)}
          />

          <div className="artifact-strip">
            <Artifact label="Version" value={workflowVersionId || "Not saved"} />
            <Artifact label="Artifact" value={artifactId || "Not published"} />
            <Artifact label="Execution" value={executionId || "Not executed"} />
          </div>
        </section>

        <aside className="ops-panel">
          {packageAlert && (
            <div className="reload-alert">
              <Radio size={18} />
              <div>
                <strong>New package available.</strong>
                <span>Reload shells after enabling its feature.</span>
              </div>
              <button type="button" onClick={reloadShells}>Reload shells</button>
            </div>
          )}

          <section className="ops-section">
            <div className="section-heading">
              <h2><PackagePlus size={17} /> Package & Shell</h2>
              <button type="button" className="icon-button" onClick={refreshState} title="Refresh status">
                <RefreshCw size={16} />
              </button>
            </div>

            <label className="upload-zone">
              <CloudUpload size={24} />
              <span>Drop folder upload</span>
              <strong>Choose .nupkg</strong>
              <input type="file" accept=".nupkg" onChange={uploadPackage} />
            </label>
            <p className="path-hint">{dropFolder || "packages"}</p>
          </section>

          <section className="ops-section compact">
            <h3>Active Packages</h3>
            <div className="package-list">
              {packages.length === 0 && <p className="muted">No active packages reported yet.</p>}
              {packages.map((pkg) => (
                <div className="package-row" key={`${pkg.id}-${pkg.version}`}>
                  <PackagePlus size={15} />
                  <div>
                    <strong>{pkg.id}</strong>
                    <span>{pkg.version}</span>
                  </div>
                </div>
              ))}
            </div>
          </section>

          <section className="ops-section">
            <h3>Enable Feature</h3>
            <input className="text-input" value={featureName} onChange={(event) => setFeatureName(event.target.value)} />
            <textarea
              className="mini-code"
              value={featureConfig}
              spellCheck={false}
              onChange={(event) => setFeatureConfig(event.target.value)}
            />
            <div className="split-actions">
              <ActionButton icon={PlugZap} busy={busy === "feature"} onClick={enableFeature}>Enable</ActionButton>
              <ActionButton icon={RefreshCw} busy={busy === "reload"} onClick={reloadShells}>Reload</ActionButton>
            </div>
          </section>

          <section className="ops-section shell-json">
            <div className="section-heading">
              <h3>shells.json</h3>
              <button type="button" className="small-button" onClick={saveShellJson}>Save JSON</button>
            </div>
            <textarea value={shellJson} spellCheck={false} onChange={(event) => setShellJson(event.target.value)} />
            <div className="feature-chips">
              {features.slice(0, 6).map((feature) => <span key={feature.name}>{feature.name}</span>)}
            </div>
          </section>
        </aside>
      </main>

      <ConsolePanel lines={consoleLines} connected={consoleConnected} />
    </div>
  );
}

function StepRail({ completed, currentStep }) {
  return (
    <nav className="step-rail">
      {steps.map((step, index) => {
        const Icon = step.icon;
        const state = completed.has(step.key) ? "complete" : currentStep === step.key ? "current" : "pending";
        return (
          <div className={`step ${state}`} key={step.key}>
            <div className="step-icon">
              {state === "complete" ? <Check size={16} /> : <Icon size={16} />}
            </div>
            <div>
              <span>{String(index + 1).padStart(2, "0")}</span>
              <strong>{step.label}</strong>
            </div>
            {state === "current" ? <ChevronRight size={16} /> : <Circle size={8} />}
          </div>
        );
      })}
    </nav>
  );
}

function ActionButton({ icon: Icon, busy, children, ...props }) {
  return (
    <button type="button" className="action-button" {...props}>
      {busy ? <RefreshCw className="spin" size={16} /> : <Icon size={16} />}
      {children}
    </button>
  );
}

function Artifact({ label, value }) {
  return (
    <div className="artifact">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function ConsolePanel({ lines, connected }) {
  return (
    <section className="console-panel">
      <div className="console-header">
        <h2><Terminal size={17} /> Backend console</h2>
        <div className="console-tools">
          <span className={connected ? "status-dot online" : "status-dot"} />
          <span>{connected ? "live" : "waiting"}</span>
          <span>stdout</span>
          <span>stderr</span>
        </div>
      </div>
      <div className="console-lines">
        {lines.length === 0 && (
          <div className="console-line stdout">
            <span>{new Date().toLocaleTimeString()}</span>
            <code>Console stream is ready.</code>
          </div>
        )}
        {lines.map((line) => (
          <div className={`console-line ${line.stream}`} key={line.id}>
            <span>{new Date(line.timestamp).toLocaleTimeString()}</span>
            <code>{line.text}</code>
          </div>
        ))}
      </div>
    </section>
  );
}
