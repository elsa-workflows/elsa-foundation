import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import * as signalR from "@microsoft/signalr";
import {
  Activity,
  ArrowDownToLine,
  Boxes,
  Check,
  ChevronRight,
  Circle,
  CloudUpload,
  FileJson,
  Moon,
  PackagePlus,
  Pause,
  Play,
  PlugZap,
  Radio,
  RefreshCw,
  RotateCcw,
  Rocket,
  Save,
  Search,
  Server,
  Sun,
  Terminal,
  Trash2,
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
const themeStorageKey = "elsa-demo-theme";
const consoleHeightStorageKey = "elsa-demo-console-height";
const consoleAutoScrollStorageKey = "elsa-demo-console-autoscroll";
const defaultConsoleHeight = 232;
const minConsoleHeight = 140;
const maxConsoleHeight = 560;
const minWorkspaceHeight = 260;
const consoleReplayLimit = 2_000;
const maxConsoleLines = 2_000;
const ansiEscapePattern = /\x1b\[([0-9;]*)m/g;
const ansiForegroundClasses = {
  30: "console-ansi-fg-black",
  31: "console-ansi-fg-red",
  32: "console-ansi-fg-green",
  33: "console-ansi-fg-yellow",
  34: "console-ansi-fg-blue",
  35: "console-ansi-fg-magenta",
  36: "console-ansi-fg-cyan",
  37: "console-ansi-fg-white",
  90: "console-ansi-fg-bright-black",
  91: "console-ansi-fg-bright-red",
  92: "console-ansi-fg-bright-green",
  93: "console-ansi-fg-bright-yellow",
  94: "console-ansi-fg-bright-blue",
  95: "console-ansi-fg-bright-magenta",
  96: "console-ansi-fg-bright-cyan",
  97: "console-ansi-fg-bright-white"
};
const ansiBackgroundClasses = {
  40: "console-ansi-bg-black",
  41: "console-ansi-bg-red",
  42: "console-ansi-bg-green",
  43: "console-ansi-bg-yellow",
  44: "console-ansi-bg-blue",
  45: "console-ansi-bg-magenta",
  46: "console-ansi-bg-cyan",
  47: "console-ansi-bg-white",
  100: "console-ansi-bg-bright-black",
  101: "console-ansi-bg-bright-red",
  102: "console-ansi-bg-bright-green",
  103: "console-ansi-bg-bright-yellow",
  104: "console-ansi-bg-bright-blue",
  105: "console-ansi-bg-bright-magenta",
  106: "console-ansi-bg-bright-cyan",
  107: "console-ansi-bg-bright-white"
};

function getInitialTheme() {
  if (typeof window === "undefined")
    return "light";

  const storedTheme = window.localStorage.getItem(themeStorageKey);
  if (storedTheme === "light" || storedTheme === "dark")
    return storedTheme;

  return window.matchMedia?.("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

function getMaxConsoleHeight() {
  if (typeof window === "undefined")
    return maxConsoleHeight;

  return Math.max(minConsoleHeight, Math.min(maxConsoleHeight, window.innerHeight - minWorkspaceHeight));
}

function clampConsoleHeight(value) {
  const number = Number(value);
  if (!Number.isFinite(number))
    return defaultConsoleHeight;

  return Math.min(getMaxConsoleHeight(), Math.max(minConsoleHeight, Math.round(number)));
}

function getInitialConsoleHeight() {
  if (typeof window === "undefined")
    return defaultConsoleHeight;

  return clampConsoleHeight(window.localStorage.getItem(consoleHeightStorageKey) ?? defaultConsoleHeight);
}

function getInitialConsoleAutoScroll() {
  if (typeof window === "undefined")
    return true;

  return window.localStorage.getItem(consoleAutoScrollStorageKey) !== "false";
}

function createAnsiState() {
  return {
    bold: false,
    dim: false,
    foreground: "",
    background: ""
  };
}

function updateAnsiState(state, codes) {
  for (const code of codes) {
    if (code === 0) {
      Object.assign(state, createAnsiState());
    } else if (code === 1) {
      state.bold = true;
      state.dim = false;
    } else if (code === 2) {
      state.dim = true;
      state.bold = false;
    } else if (code === 22) {
      state.bold = false;
      state.dim = false;
    } else if (code === 39) {
      state.foreground = "";
    } else if (code === 49) {
      state.background = "";
    } else if (ansiForegroundClasses[code]) {
      state.foreground = ansiForegroundClasses[code];
    } else if (ansiBackgroundClasses[code]) {
      state.background = ansiBackgroundClasses[code];
    }
  }
}

function getAnsiClassName(state) {
  return [
    state.bold ? "console-ansi-bold" : "",
    state.dim ? "console-ansi-dim" : "",
    state.foreground,
    state.background
  ].filter(Boolean).join(" ");
}

function renderConsoleText(text) {
  if (!text.includes("\x1b["))
    return text;

  const segments = [];
  const state = createAnsiState();
  let lastIndex = 0;
  let match;

  ansiEscapePattern.lastIndex = 0;
  while ((match = ansiEscapePattern.exec(text)) !== null) {
    if (match.index > lastIndex)
      segments.push({ text: text.slice(lastIndex, match.index), className: getAnsiClassName(state) });

    const codes = match[1] === "" ? [0] : match[1].split(";").map((value) => Number(value || 0));
    updateAnsiState(state, codes);
    lastIndex = ansiEscapePattern.lastIndex;
  }

  if (lastIndex < text.length)
    segments.push({ text: text.slice(lastIndex), className: getAnsiClassName(state) });

  return segments.map((segment, index) => segment.className
    ? <span className={segment.className} key={index}>{segment.text}</span>
    : segment.text);
}

function getConsoleStreamName(stream) {
  return stream === 1 || stream === "stderr" || stream === "Stderr" ? "stderr" : "stdout";
}

function compareConsoleEntries(left, right) {
  const timestampDelta = Date.parse(left.timestamp) - Date.parse(right.timestamp);
  if (timestampDelta !== 0)
    return timestampDelta;

  return (left.sequence ?? 0) - (right.sequence ?? 0);
}

function createConsoleEntry(stream, text) {
  return {
    id: `${Date.now()}-${Math.random()}`,
    timestamp: new Date().toISOString(),
    sequence: null,
    stream,
    text
  };
}

function createConsoleEntryFromLine(line) {
  return {
    id: line.id ?? `${line.sequence ?? Date.now()}-${Math.random()}`,
    timestamp: line.timestamp ?? line.receivedAt ?? new Date().toISOString(),
    sequence: line.sequence ?? null,
    stream: getConsoleStreamName(line.stream),
    text: line.text ?? ""
  };
}

export function App() {
  const [theme, setTheme] = useState(getInitialTheme);
  const [consoleHeight, setConsoleHeight] = useState(getInitialConsoleHeight);
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
  const [packageNotification, setPackageNotification] = useState(null);
  const [consoleLines, setConsoleLines] = useState([]);
  const [consoleLineCount, setConsoleLineCount] = useState(0);
  const [consoleConnected, setConsoleConnected] = useState(false);
  const [consolePaused, setConsolePaused] = useState(false);
  const [consoleAutoScroll, setConsoleAutoScroll] = useState(getInitialConsoleAutoScroll);
  const [pausedConsoleLines, setPausedConsoleLines] = useState(null);
  const [pausedConsoleLineCount, setPausedConsoleLineCount] = useState(0);
  const [dropFolder, setDropFolder] = useState("");
  const [mainView, setMainView] = useState("workflow");
  const [activities, setActivities] = useState([]);
  const [activitySearch, setActivitySearch] = useState("");
  const [activitiesLoading, setActivitiesLoading] = useState(false);
  const lastEventSequence = useRef(0);
  const activityVersionCache = useRef(new Map());
  const seenConsoleLineIds = useRef(new Set());

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    window.localStorage.setItem(themeStorageKey, theme);
  }, [theme]);

  useEffect(() => {
    window.localStorage.setItem(consoleHeightStorageKey, String(consoleHeight));
  }, [consoleHeight]);

  useEffect(() => {
    window.localStorage.setItem(consoleAutoScrollStorageKey, String(consoleAutoScroll));
  }, [consoleAutoScroll]);

  useEffect(() => {
    const handleResize = () => setConsoleHeight((current) => clampConsoleHeight(current));
    window.addEventListener("resize", handleResize);
    return () => window.removeEventListener("resize", handleResize);
  }, []);

  const currentStep = useMemo(() => {
    const firstOpen = steps.find((step) => !completed.has(step.key));
    return firstOpen?.key ?? "newActivity";
  }, [completed]);

  const filteredActivities = useMemo(() => {
    const query = activitySearch.trim().toLowerCase();
    if (!query)
      return activities;

    return activities.filter((activity) => [
      activity.displayName,
      activity.activityTypeKey,
      activity.category,
      activity.description,
      activity.id
    ].some((value) => value?.toLowerCase().includes(query)));
  }, [activities, activitySearch]);

  const visibleConsoleLines = consolePaused ? pausedConsoleLines ?? consoleLines : consoleLines;
  const queuedConsoleLineCount = consolePaused
    ? Math.max(0, consoleLineCount - pausedConsoleLineCount)
    : 0;

  const addConsoleEntries = useCallback((entries) => {
    if (entries.length === 0)
      return;

    const uniqueEntries = entries.filter((entry) => {
      if (seenConsoleLineIds.current.has(entry.id))
        return false;

      seenConsoleLineIds.current.add(entry.id);
      return true;
    });

    if (uniqueEntries.length === 0)
      return;

    setConsoleLineCount((current) => current + uniqueEntries.length);
    setConsoleLines((current) => [...current, ...uniqueEntries].sort(compareConsoleEntries).slice(-maxConsoleLines));
  }, []);

  const addConsoleLine = useCallback((stream, text) => {
    addConsoleEntries([createConsoleEntry(stream, text)]);
  }, [addConsoleEntries]);

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

  const refreshActivities = useCallback(async () => {
    setActivitiesLoading(true);
    try {
      const definitions = await request("/default/design/activities/definitions");
      const ordered = [...(definitions ?? [])].sort((left, right) =>
        (left.displayName ?? left.activityTypeKey).localeCompare(right.displayName ?? right.activityTypeKey));
      setActivities(ordered);
    } finally {
      setActivitiesLoading(false);
    }
  }, [request]);

  useEffect(() => {
    refreshActivities().catch((error) => addConsoleLine("stderr", `Activity catalog refresh failed: ${error.message}`));
  }, [addConsoleLine, refreshActivities]);

  const showPackageNotification = useCallback((events) => {
    const changedPackages = events
      .flatMap((event) => [...(event.added ?? []), ...(event.updated ?? [])])
      .map((pkg) => `${pkg.id} ${pkg.version}`.trim());
    const packageText = changedPackages.length > 0
      ? changedPackages.join(", ")
      : "A package change was reconciled.";

    setPackageNotification({
      title: "New package detected",
      message: `${packageText} is ready. Enable its feature if needed, then reload shells.`
    });
  }, []);

  const loadRecentConsoleLines = useCallback(async () => {
    const result = await request(`/diagnostics/console-logs/recent?limit=${consoleReplayLimit}`);
    const lines = result.items ?? result.lines ?? [];
    addConsoleEntries(lines.map(createConsoleEntryFromLine));
  }, [addConsoleEntries, request]);

  useEffect(() => {
    const interval = window.setInterval(async () => {
      try {
        const newEvents = await request(`/_demo/packages/events?afterSequence=${lastEventSequence.current}`);
        if (newEvents.length === 0)
          return;

        setEvents((current) => [...current, ...newEvents].slice(-80));
        lastEventSequence.current = Math.max(...newEvents.map((event) => event.sequence));

        const packageChangeEvents = newEvents.filter((event) =>
          event.kind === "changed" && ((event.added?.length ?? 0) > 0 || (event.updated?.length ?? 0) > 0));
        if (packageChangeEvents.length > 0) {
          showPackageNotification(packageChangeEvents);
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
  }, [addConsoleLine, markComplete, request, showPackageNotification]);

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
        const stream = connection.stream("StreamAsync", { limit: consoleReplayLimit });
        stream.subscribe({
          next: (item) => {
            if (item?.line) {
              addConsoleEntries([createConsoleEntryFromLine(item.line)]);
            } else if (item?.droppedLines || item?.dropped) {
              const dropped = item.droppedLines ?? item.dropped;
              addConsoleLine("stderr", `${dropped.count} console lines were dropped.`);
            }
          },
          error: (error) => addConsoleLine("stderr", `Console stream failed: ${error.message}`),
          complete: () => addConsoleLine("stdout", "Console stream completed.")
        });

        await loadRecentConsoleLines();
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
  }, [addConsoleEntries, addConsoleLine, loadRecentConsoleLines]);

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
      await refreshActivities();
    });
  }

  async function resetDemo() {
    await runAction("reset", "Resetting demo database and package drop folder...", async () => {
      const response = await request("/_demo/reset", {
        method: "POST",
        body: "{}"
      });
      const workflowRows = response.workflows?.totalDeleted ?? 0;
      const activityRows = response.activities?.totalDeleted ?? 0;
      const packageFiles = response.packages?.deletedFiles ?? 0;
      const packageDirectories = response.packages?.deletedDirectories ?? 0;

      setWorkflowVersionId("");
      setArtifactId("");
      setExecutionId("");
      setPackageNotification(null);
      activityVersionCache.current.clear();
      addConsoleLine(
        "stdout",
        `Reset complete: ${workflowRows} workflow row(s), ${activityRows} activity row(s), ${packageFiles} package file(s), ${packageDirectories} package folder(s).`
      );
      if (response.reconcile?.outcome) {
        addConsoleLine("stdout", `Reconcile ${response.reconcile.outcome}: ${response.reconcile.correlationId}`);
      }
      await refreshState();
      await refreshActivities();
    });
    setCompleted(new Set());
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
      const response = await request("/_demo/shells/reload", {
        method: "POST",
        body: "{}"
      });
      setPackageNotification(null);
      activityVersionCache.current.clear();
      await refreshState();
      await refreshActivities();
      addConsoleLine("stdout", `Shells reloaded after refreshing ${response.featureDescriptorCount} feature descriptor(s).`);
    });
  }

  function toggleTheme() {
    setTheme((current) => current === "dark" ? "light" : "dark");
  }

  function toggleConsolePaused() {
    if (consolePaused) {
      setConsolePaused(false);
      setPausedConsoleLines(null);
      return;
    }

    setPausedConsoleLines(consoleLines);
    setPausedConsoleLineCount(consoleLineCount);
    setConsolePaused(true);
  }

  function clearConsole() {
    setConsoleLines([]);
    setConsoleLineCount(0);
    setPausedConsoleLines(consolePaused ? [] : null);
    setPausedConsoleLineCount(0);
  }

  function resizeConsole(delta) {
    setConsoleHeight((current) => clampConsoleHeight(current + delta));
  }

  function startConsoleResize(event) {
    event.preventDefault();

    const startY = event.clientY;
    const startHeight = consoleHeight;

    function handlePointerMove(moveEvent) {
      setConsoleHeight(clampConsoleHeight(startHeight + startY - moveEvent.clientY));
    }

    function stopResize() {
      document.body.classList.remove("console-resizing");
      window.removeEventListener("pointermove", handlePointerMove);
      window.removeEventListener("pointerup", stopResize);
      window.removeEventListener("pointercancel", stopResize);
    }

    document.body.classList.add("console-resizing");
    window.addEventListener("pointermove", handlePointerMove);
    window.addEventListener("pointerup", stopResize);
    window.addEventListener("pointercancel", stopResize);
  }

  function handleConsoleResizeKeyDown(event) {
    const keyDeltas = {
      ArrowUp: 24,
      ArrowDown: -24,
      PageUp: 80,
      PageDown: -80
    };

    if (event.key in keyDeltas) {
      event.preventDefault();
      resizeConsole(keyDeltas[event.key]);
      return;
    }

    if (event.key === "Home") {
      event.preventDefault();
      setConsoleHeight(minConsoleHeight);
      return;
    }

    if (event.key === "End") {
      event.preventDefault();
      setConsoleHeight(getMaxConsoleHeight());
    }
  }

  return (
    <div className="app-shell" style={{ "--console-height": `${consoleHeight}px` }}>
      <header className="topbar">
        <div className="brand">
          <div className="brand-mark"><Boxes size={19} /></div>
          <div>
            <h1>Elsa Dynamic Runtime Demo</h1>
            <p>{status}</p>
          </div>
        </div>
        <div className="topbar-actions">
          <div className="backend-status">
            <span className={consoleConnected ? "status-dot online" : "status-dot"} />
            <span>{consoleConnected ? "Console connected" : "Console offline"}</span>
            <span className="divider" />
            <Server size={16} />
            <span>/default</span>
          </div>
          <button
            type="button"
            className="theme-toggle"
            onClick={toggleTheme}
            title={theme === "dark" ? "Switch to light mode" : "Switch to dark mode"}
            aria-label={theme === "dark" ? "Switch to light mode" : "Switch to dark mode"}
          >
            {theme === "dark" ? <Sun size={16} /> : <Moon size={16} />}
            <span>{theme === "dark" ? "Light" : "Dark"}</span>
          </button>
        </div>
      </header>

      {packageNotification && (
        <div className="package-notification" role="alert">
          <Radio size={18} />
          <div>
            <strong>{packageNotification.title}</strong>
            <span>{packageNotification.message}</span>
          </div>
        </div>
      )}

      <main className="workspace">
        <StepRail completed={completed} currentStep={currentStep} />

        <section className="editor-surface">
          <div className="panel-titlebar">
            <div>
              <h2>
                {mainView === "workflow" ? <FileJson size={18} /> : <Activity size={18} />}
                {mainView === "workflow" ? "Workflow Definition" : "Activity Catalog"}
              </h2>
              <p>{mainView === "workflow"
                ? "Submit, publish, and execute against the mounted default shell."
                : `${activities.length} activit${activities.length === 1 ? "y" : "ies"} available in /default.`}</p>
            </div>
            {mainView === "workflow" ? (
              <select value={selectedSample} onChange={(event) => selectSample(event.target.value)}>
                {Object.entries(sampleWorkflows).map(([key, sample]) => (
                  <option key={key} value={key}>{sample.label}</option>
                ))}
              </select>
            ) : (
              <button type="button" className="small-button" onClick={refreshActivities} disabled={activitiesLoading}>
                {activitiesLoading ? "Refreshing" : "Refresh"}
              </button>
            )}
          </div>

          <div className="toolbar">
            <div className="view-tabs">
              <button type="button" className={mainView === "workflow" ? "active" : ""} onClick={() => setMainView("workflow")}>
                <FileJson size={15} />
                Workflow
              </button>
              <button type="button" className={mainView === "activities" ? "active" : ""} onClick={() => setMainView("activities")}>
                <Activity size={15} />
                Activities
              </button>
            </div>
            {mainView === "workflow" ? (
              <>
                <ActionButton icon={Save} busy={busy === "save"} onClick={saveWorkflow}>Save</ActionButton>
                <ActionButton icon={Rocket} busy={busy === "publish"} onClick={publishWorkflow} disabled={!workflowVersionId}>Publish</ActionButton>
                <ActionButton icon={Play} busy={busy === "execute"} onClick={() => executeWorkflow("execute")} disabled={!artifactId}>Execute</ActionButton>
              </>
            ) : (
              <div className="activity-search">
                <Search size={15} />
                <input value={activitySearch} onChange={(event) => setActivitySearch(event.target.value)} placeholder="Filter activities" />
              </div>
            )}
          </div>

          {mainView === "workflow" ? (
            <>
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
            </>
          ) : (
            <ActivityCatalog activities={filteredActivities} totalCount={activities.length} loading={activitiesLoading} />
          )}
        </section>

        <aside className="ops-panel">
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
            <div className="split-actions package-actions">
              <ActionButton icon={RotateCcw} busy={busy === "reset"} onClick={resetDemo}>Reset</ActionButton>
            </div>
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
              <ActionButton icon={RefreshCw} busy={busy === "reload"} onClick={reloadShells}>Reload shells</ActionButton>
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

      <ConsolePanel
        lines={visibleConsoleLines}
        connected={consoleConnected}
        paused={consolePaused}
        autoScroll={consoleAutoScroll}
        queuedLineCount={queuedConsoleLineCount}
        height={consoleHeight}
        onTogglePause={toggleConsolePaused}
        onToggleAutoScroll={() => setConsoleAutoScroll((current) => !current)}
        onClear={clearConsole}
        onResizeStart={startConsoleResize}
        onResizeKeyDown={handleConsoleResizeKeyDown}
      />
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

function ActivityCatalog({ activities, totalCount, loading }) {
  return (
    <div className="activity-catalog">
      <div className="activity-summary">
        <strong>{activities.length}</strong>
        <span>{activities.length === totalCount ? "shown" : `shown of ${totalCount}`}</span>
      </div>
      <div className="activity-list">
        {loading && activities.length === 0 && <p className="muted">Loading activity catalog...</p>}
        {!loading && activities.length === 0 && <p className="muted">No activities match the current filter.</p>}
        {activities.map((activity) => (
          <div className="activity-row" key={activity.id}>
            <div className="activity-row-icon"><Activity size={15} /></div>
            <div className="activity-row-main">
              <strong>{activity.displayName || activity.activityTypeKey}</strong>
              <span>{activity.activityTypeKey}</span>
              {activity.description && <p>{activity.description}</p>}
            </div>
            <div className="activity-row-meta">
              <span>{activity.category || "Uncategorized"}</span>
              <code>{activity.id}</code>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

function ConsolePanel({
  lines,
  connected,
  paused,
  autoScroll,
  queuedLineCount,
  height,
  onTogglePause,
  onToggleAutoScroll,
  onClear,
  onResizeStart,
  onResizeKeyDown
}) {
  const linesRef = useRef(null);

  useEffect(() => {
    if (!autoScroll || paused)
      return;

    const container = linesRef.current;
    if (container)
      container.scrollTop = container.scrollHeight;
  }, [autoScroll, lines, paused]);

  return (
    <section className="console-panel">
      <div
        className="console-resize-handle"
        role="separator"
        aria-label="Resize console panel"
        aria-orientation="horizontal"
        aria-valuemin={minConsoleHeight}
        aria-valuemax={getMaxConsoleHeight()}
        aria-valuenow={height}
        tabIndex={0}
        title="Drag to resize console"
        onPointerDown={onResizeStart}
        onKeyDown={onResizeKeyDown}
      />
      <div className="console-header">
        <div>
          <h2><Terminal size={17} /> Backend console</h2>
          {queuedLineCount > 0 && <p>{queuedLineCount} buffered while paused</p>}
        </div>
        <div className="console-tools">
          <span className={connected ? "status-dot online" : "status-dot"} />
          <span>{connected ? "live" : "waiting"}</span>
          <span>stdout</span>
          <span>stderr</span>
          <button
            type="button"
            className={paused ? "console-tool-button active" : "console-tool-button"}
            onClick={onTogglePause}
            aria-pressed={paused}
            title={paused ? "Resume console stream" : "Pause console stream"}
          >
            {paused ? <Play size={14} /> : <Pause size={14} />}
            <span>{paused ? "Resume" : "Pause"}</span>
          </button>
          <button
            type="button"
            className={autoScroll ? "console-tool-button active" : "console-tool-button"}
            onClick={onToggleAutoScroll}
            aria-pressed={autoScroll}
            title={autoScroll ? "Disable autoscroll" : "Enable autoscroll"}
          >
            <ArrowDownToLine size={14} />
            <span>Autoscroll</span>
          </button>
          <button
            type="button"
            className="console-tool-button"
            onClick={onClear}
            title="Clear console"
          >
            <Trash2 size={14} />
            <span>Clear</span>
          </button>
        </div>
      </div>
      <div className="console-lines" ref={linesRef}>
        {lines.length === 0 && (
          <div className="console-line stdout">
            <span>{new Date().toLocaleTimeString()}</span>
            <code>Console stream is ready.</code>
          </div>
        )}
        {lines.map((line) => (
          <div className={`console-line ${line.stream}`} key={line.id}>
            <span>{new Date(line.timestamp).toLocaleTimeString()}</span>
            <code>{renderConsoleText(line.text)}</code>
          </div>
        ))}
      </div>
    </section>
  );
}
