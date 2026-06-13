import { memo, useCallback, useEffect, useMemo, useRef, useState } from "react";
import * as signalR from "@microsoft/signalr";
import ReactFlow, {
  Background,
  Controls,
  Handle,
  MiniMap,
  Position
} from "reactflow";
import "reactflow/dist/style.css";
import {
  Activity,
  ArrowDownToLine,
  Boxes,
  Check,
  ChevronRight,
  Circle,
  CloudUpload,
  ExternalLink,
  FileJson,
  GitBranch,
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
      "{{writeLineActivityVersionId}}": "WriteLine",
      "{{sequenceActivityVersionId}}": "Sequence",
      "{{sayHelloFromNuplaneActivityVersionId}}": "SayHelloFromNuplane"
    },
    value: {
      name: "Nuplane Sequence",
      description: "Runs WriteLine, SayHelloFromNuplane, then a final WriteLine inside a Sequence root activity.",
      state: {
        variables: [],
        rootActivity: {
          nodeId: "nuplane-sequence-root",
          activityVersionId: "{{sequenceActivityVersionId}}",
          inputs: [],
          outputs: [],
          childSlots: [
            {
              name: "Sequence.Activities",
              activities: [
                {
                  nodeId: "write-before-nuplane",
                  activityVersionId: "{{writeLineActivityVersionId}}",
                  inputs: [
                    {
                      referenceKey: "Text",
                      value: {
                        value: "Before Nuplane activity",
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
                {
                  nodeId: "write-after-nuplane",
                  activityVersionId: "{{writeLineActivityVersionId}}",
                  inputs: [
                    {
                      referenceKey: "Text",
                      value: {
                        value: "After Nuplane activity",
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
  }
};

const activityTokenLabels = {
  "{{writeLineActivityVersionId}}": "WriteLine",
  "{{sequenceActivityVersionId}}": "Sequence",
  "{{sayHelloFromNuplaneActivityVersionId}}": "SayHelloFromNuplane"
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
const themeModeStorageKey = "elsa-demo-theme-mode";
const themeFamilies = [
  {
    id: "studio",
    label: "Studio",
    colors: {
      light: ["#eef2f4", "#ffffff", "#0f8a83"],
      dark: ["#101719", "#172124", "#20b9ad"]
    }
  },
  {
    id: "harbor",
    label: "Harbor",
    colors: {
      light: ["#eaf3f4", "#fbfefe", "#188d9a"],
      dark: ["#0d171b", "#152329", "#35bfd0"]
    }
  },
  {
    id: "ember",
    label: "Ember",
    colors: {
      light: ["#f5efe8", "#fffaf4", "#c45a2a"],
      dark: ["#1b1310", "#261a15", "#ef8b55"]
    }
  },
  {
    id: "forest",
    label: "Forest",
    colors: {
      light: ["#edf2ec", "#fbfdf8", "#2c7a4b"],
      dark: ["#101811", "#18231a", "#5fcf85"]
    }
  },
  {
    id: "orchid",
    label: "Orchid",
    colors: {
      light: ["#f2eef7", "#ffffff", "#7c4ac9"],
      dark: ["#17121e", "#221a2c", "#b888ff"]
    }
  },
  {
    id: "slate",
    label: "Slate",
    colors: {
      light: ["#e8edf2", "#fbfdff", "#315f8f"],
      dark: ["#10161d", "#182331", "#70a9df"]
    }
  },
  {
    id: "elsa",
    label: "Beacon",
    colors: {
      light: ["#eee8df", "#fffaf1", "#d88b28"],
      dark: ["#12110e", "#1c1a15", "#f0a83e"]
    }
  }
];
const demoStackPackages = [
  {
    name: "Elsa Workflows",
    url: "https://www.elsaworkflows.io",
    imageUrl: "https://www.elsaworkflows.io/assets/elsa-logo-CsMVb5ff.png",
    imageAlt: "Elsa Workflows logo",
    imageMode: "logo",
    role: "Workflow engine",
    description: "Provides the workflow definition, publishing, execution, activity catalog, and runtime APIs used by the demo.",
    details: ["Workflow definitions", "Publishing", "Execution"]
  },
  {
    name: "CShells",
    url: "https://www.cshells.io",
    imageUrl: "https://pub-bb2e103a32db4e198524a2e9ed8f35b4.r2.dev/a83ce4f2-5732-459a-904a-1c5573548edc/id-preview-744ebe73--89fe6b5d-3d2f-47a7-b058-2139fcd37836.lovable.app-1772287578314.png",
    imageAlt: "CShells website preview",
    role: "Dynamic shell composition",
    description: "Hosts the default shell, reads shell feature configuration, and reloads shell composition without restarting the host.",
    details: ["Feature composition", "Shell reload", "Runtime catalog"]
  },
  {
    name: "Nuplane",
    url: "https://www.nuplane.io",
    imageUrl: "https://pub-bb2e103a32db4e198524a2e9ed8f35b4.r2.dev/29b2b93f-9fca-472a-8a1a-d139071a465b/id-preview-7da9d3f9--6d0124ec-ad1c-46ef-9162-e801fb8223de.lovable.app-1772455899178.png",
    imageAlt: "Nuplane website preview",
    role: "Package loading",
    description: "Observes the package drop folder, reconciles NuGet packages, and makes package-provided assemblies available to the running app.",
    details: ["Drop folder", "Reconcile", "Package observer"]
  },
  {
    name: "ConsoleLogStreaming",
    url: "https://www.consolelogstreaming.dev/",
    imageUrl: "https://storage.googleapis.com/gpt-engineer-file-uploads/attachments/og-images/1465100f-0b0a-450b-b0b3-6d8d47c3642b",
    imageAlt: "ConsoleLogStreaming website preview",
    role: "Live backend console",
    description: "Captures stdout and stderr from the backend and streams console lines into the demo UI in real time.",
    details: ["Console capture", "SignalR streaming", "ANSI styling"]
  }
];
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

function getInitialThemeFamily() {
  if (typeof window === "undefined")
    return "studio";

  const storedTheme = window.localStorage.getItem(themeStorageKey);
  if (themeFamilies.some((family) => family.id === storedTheme))
    return storedTheme;

  if (storedTheme === "midnight")
    return "harbor";

  return "studio";
}

function getInitialThemeMode() {
  if (typeof window === "undefined")
    return "light";

  const storedMode = window.localStorage.getItem(themeModeStorageKey);
  if (storedMode === "light" || storedMode === "dark")
    return storedMode;

  const storedTheme = window.localStorage.getItem(themeStorageKey);
  if (storedTheme === "dark" || storedTheme === "midnight")
    return "dark";

  if (storedTheme === "light")
    return "light";

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

function formatDateTime(value) {
  if (!value)
    return "Not set";

  return new Date(value).toLocaleString();
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

function parseWorkflowDesignerJson(workflowJson) {
  try {
    const workflow = JSON.parse(workflowJson);
    if (!workflow?.state?.rootActivity)
      return { workflow: null, error: "Workflow JSON must contain state.rootActivity." };

    return { workflow, error: "" };
  } catch (error) {
    return { workflow: null, error: error.message };
  }
}

function getActivityDisplayName(activity) {
  const versionId = activity?.activityVersionId ?? "";
  if (activityTokenLabels[versionId])
    return activityTokenLabels[versionId];

  if (versionId.includes("WriteLine"))
    return "WriteLine";

  if (versionId.includes("Sequence"))
    return "Sequence";

  if (versionId.includes("Flowchart"))
    return "Flowchart";

  if (versionId.includes("SayHelloFromNuplane"))
    return "SayHelloFromNuplane";

  const nodeId = activity?.nodeId ?? "";
  if (nodeId.includes("write"))
    return "WriteLine";

  if (nodeId.includes("sequence"))
    return "Sequence";

  if (nodeId.includes("flowchart"))
    return "Flowchart";

  return versionId || nodeId || "Activity";
}

function getActivityCatalogDisplayName(activity) {
  const displayName = activity?.displayName?.trim();
  if (displayName)
    return displayName;

  const typeName = activity?.activityTypeKey?.split(".").filter(Boolean).at(-1) ?? "";
  return humanizeIdentifier(typeName) || activity?.activityTypeKey || activity?.id || "Activity";
}

function humanizeIdentifier(value) {
  return value
    .replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2")
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .trim();
}

function getPrimaryActivityInput(activity) {
  const input = activity?.inputs?.find((item) => item?.value?.expressionType === "Literal" && item?.value?.value != null);
  return input ? `${input.referenceKey}: ${input.value.value}` : "";
}

function getActivityChildSlots(activity) {
  return Array.isArray(activity?.childSlots) ? activity.childSlots : [];
}

function getSlotActivities(slot) {
  return Array.isArray(slot?.activities) ? slot.activities : [];
}

function getActivityContainerKind(activity) {
  if (!activity)
    return null;

  const activityText = [
    getActivityDisplayName(activity),
    activity.activityVersionId,
    activity.nodeId,
    ...getActivityChildSlots(activity).map((slot) => slot?.name)
  ].filter(Boolean).join(" ").toLowerCase();

  if (activityText.includes("flowchart"))
    return "flowchart";

  if (activityText.includes("sequence"))
    return "sequence";

  return null;
}

function getDesignerMode(rootActivity) {
  const containerKind = getActivityContainerKind(rootActivity);
  if (containerKind)
    return containerKind;

  const childSlots = getActivityChildSlots(rootActivity);
  const childCount = childSlots.reduce((count, slot) => count + getSlotActivities(slot).length, 0);
  if (childCount === 0)
    return "single";

  return "sequence";
}

function buildWorkflowDesignerModel(workflow) {
  const rootActivity = workflow?.state?.rootActivity;
  if (!rootActivity)
    return { mode: "single", containerTitle: "", nodes: [], edges: [], activityPaths: [] };

  const mode = getDesignerMode(rootActivity);
  const containerKind = getActivityContainerKind(rootActivity);
  const nodes = [];
  const edges = [];
  const activityPaths = [];

  function addActivityNode(activity, path, position, role = "") {
    activityPaths.push(path);
    nodes.push({
      id: path,
      type: "activity",
      position,
      data: {
        title: getActivityDisplayName(activity),
        nodeId: activity.nodeId || "unnamed",
        subtitle: getPrimaryActivityInput(activity),
        role
      }
    });
  }

  if (!containerKind) {
    addActivityNode(rootActivity, "root", { x: 260, y: 160 }, "Root");
    return {
      mode,
      containerTitle: "Root activity",
      nodes,
      edges,
      activityPaths
    };
  }

  const childSlots = getActivityChildSlots(rootActivity);
  const children = childSlots.flatMap((slot, slotIndex) =>
    getSlotActivities(slot).map((activity, activityIndex) => ({
      activity,
      slotName: slot.name || `Slot ${slotIndex + 1}`,
      path: `root.slot${slotIndex}.activity${activityIndex}`
    })));

  if (mode === "sequence") {
    children.forEach((child, index) => {
      addActivityNode(child.activity, child.path, { x: 280, y: 80 + index * 140 }, child.slotName);
      if (index > 0) {
        edges.push({
          id: `${children[index - 1].path}-to-${child.path}`,
          source: children[index - 1].path,
          target: child.path,
          type: "smoothstep"
        });
      }
    });
  } else if (mode === "flowchart") {
    children.forEach((child, index) => {
      addActivityNode(child.activity, child.path, {
        x: 80 + (index % 3) * 260,
        y: 90 + Math.floor(index / 3) * 150
      }, child.slotName);
    });
  }

  return {
    mode,
    containerTitle: getActivityDisplayName(rootActivity),
    containerNodeId: rootActivity.nodeId || "",
    nodes,
    edges,
    activityPaths
  };
}

function findActivityByDesignerPath(rootActivity, path) {
  if (!rootActivity || path === "root")
    return rootActivity ?? null;

  const match = path.match(/^root\.slot(\d+)\.activity(\d+)$/);
  if (!match)
    return null;

  const slot = getActivityChildSlots(rootActivity)[Number(match[1])];
  return getSlotActivities(slot)[Number(match[2])] ?? null;
}

function updateActivityByDesignerPath(workflow, path, update) {
  const activity = findActivityByDesignerPath(workflow?.state?.rootActivity, path);
  if (!activity)
    return false;

  update(activity);
  return true;
}

function getActivityLiteralInputs(activity) {
  return (activity?.inputs ?? []).filter((input) => input?.value?.expressionType === "Literal");
}

function setActivityLiteralInput(activity, referenceKey, value) {
  const input = (activity.inputs ?? []).find((item) => item.referenceKey === referenceKey);
  if (!input)
    return;

  input.value = {
    ...(input.value ?? {}),
    value,
    expressionType: input.value?.expressionType ?? "Literal"
  };
}

export function App() {
  const [themeFamily, setThemeFamily] = useState(getInitialThemeFamily);
  const [themeMode, setThemeMode] = useState(getInitialThemeMode);
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
  const [selectedDesignerPath, setSelectedDesignerPath] = useState("root");
  const [activities, setActivities] = useState([]);
  const [activitySearch, setActivitySearch] = useState("");
  const [activitiesLoading, setActivitiesLoading] = useState(false);
  const [featureCatalogItems, setFeatureCatalogItems] = useState([]);
  const [featureSearch, setFeatureSearch] = useState("");
  const [featuresLoading, setFeaturesLoading] = useState(false);
  const [togglingFeatures, setTogglingFeatures] = useState(new Set());
  const [savingFeatureSettings, setSavingFeatureSettings] = useState(new Set());
  const [executables, setExecutables] = useState([]);
  const [executablesLoading, setExecutablesLoading] = useState(false);
  const lastEventSequence = useRef(0);
  const activityVersionCache = useRef(new Map());
  const seenConsoleLineIds = useRef(new Set());
  const packageUploadInputRef = useRef(null);

  useEffect(() => {
    document.documentElement.dataset.theme = `${themeFamily}-${themeMode}`;
    document.documentElement.dataset.themeFamily = themeFamily;
    document.documentElement.dataset.themeMode = themeMode;
    window.localStorage.setItem(themeStorageKey, themeFamily);
    window.localStorage.setItem(themeModeStorageKey, themeMode);
  }, [themeFamily, themeMode]);

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
      getActivityCatalogDisplayName(activity),
      activity.displayName,
      activity.activityTypeKey,
      activity.category,
      activity.description,
      activity.id
    ].some((value) => value?.toLowerCase().includes(query)));
  }, [activities, activitySearch]);

  const filteredFeatureCatalogItems = useMemo(() => {
    const query = featureSearch.trim().toLowerCase();
    if (!query)
      return featureCatalogItems;

    return featureCatalogItems.filter((feature) => [
      feature.id,
      feature.displayName,
      feature.description,
      feature.sourceKind,
      feature.packageId,
      feature.packageVersion,
      ...(feature.categories ?? [])
    ].some((value) => value?.toLowerCase().includes(query)));
  }, [featureCatalogItems, featureSearch]);

  const designerParseResult = useMemo(() => parseWorkflowDesignerJson(workflowJson), [workflowJson]);
  const designerModel = useMemo(() =>
    designerParseResult.workflow
      ? buildWorkflowDesignerModel(designerParseResult.workflow)
      : { mode: "single", containerTitle: "", nodes: [], edges: [], activityPaths: [] },
  [designerParseResult.workflow]);
  const selectedDesignerActivity = useMemo(() =>
    designerModel.activityPaths.includes(selectedDesignerPath)
      ? findActivityByDesignerPath(designerParseResult.workflow?.state?.rootActivity, selectedDesignerPath)
      : null,
  [designerModel.activityPaths, designerParseResult.workflow, selectedDesignerPath]);

  useEffect(() => {
    if (designerModel.activityPaths.length === 0) {
      if (selectedDesignerPath)
        setSelectedDesignerPath("");

      return;
    }

    if (!designerModel.activityPaths.includes(selectedDesignerPath))
      setSelectedDesignerPath(designerModel.activityPaths[0]);
  }, [designerModel.activityPaths, selectedDesignerPath]);

  const visibleConsoleLines = consolePaused ? pausedConsoleLines ?? consoleLines : consoleLines;
  const queuedConsoleLineCount = consolePaused
    ? Math.max(0, consoleLineCount - pausedConsoleLineCount)
    : 0;
  const mainViewMeta = useMemo(() => {
    if (mainView === "workflow") {
      return {
        icon: FileJson,
        title: "Workflow Definition",
        description: "Submit, publish, and execute against the mounted default shell."
      };
    }

    if (mainView === "designer") {
      return {
        icon: GitBranch,
        title: "Workflow Designer",
        description: designerParseResult.error || `${designerModel.mode === "single" ? "Single activity" : `${designerModel.mode} graph`} synced with workflow JSON.`
      };
    }

    if (mainView === "activities") {
      return {
        icon: Activity,
        title: "Activity Catalog",
        description: `${activities.length} activit${activities.length === 1 ? "y" : "ies"} available in /default.`
      };
    }

    if (mainView === "features") {
      const enabledCount = featureCatalogItems.filter((feature) => feature.enabled).length;
      return {
        icon: PlugZap,
        title: "Feature Catalog",
        description: `${enabledCount} enabled of ${featureCatalogItems.length} available shell feature${featureCatalogItems.length === 1 ? "" : "s"}.`
      };
    }

    if (mainView === "stack") {
      return {
        icon: Boxes,
        title: "Demo Package Stack",
        description: "Reference cards for the packages and platforms powering this demo."
      };
    }

    return {
      icon: Rocket,
      title: "Executable Artifacts",
      description: `${executables.length} published artifact${executables.length === 1 ? "" : "s"} in the runtime store.`
    };
  }, [activities.length, designerModel.mode, designerParseResult.error, executables.length, featureCatalogItems, mainView]);

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
        getActivityCatalogDisplayName(left).localeCompare(getActivityCatalogDisplayName(right)));
      setActivities(ordered);
    } finally {
      setActivitiesLoading(false);
    }
  }, [request]);

  useEffect(() => {
    refreshActivities().catch((error) => addConsoleLine("stderr", `Activity catalog refresh failed: ${error.message}`));
  }, [addConsoleLine, refreshActivities]);

  const refreshFeatures = useCallback(async () => {
    setFeaturesLoading(true);
    try {
      const catalog = await request("/_demo/features");
      setFeatureCatalogItems(catalog ?? []);
    } finally {
      setFeaturesLoading(false);
    }
  }, [request]);

  useEffect(() => {
    refreshFeatures().catch((error) => addConsoleLine("stderr", `Feature catalog refresh failed: ${error.message}`));
  }, [addConsoleLine, refreshFeatures]);

  const refreshExecutables = useCallback(async () => {
    setExecutablesLoading(true);
    try {
      const artifacts = await request("/_demo/workflows/executables");
      setExecutables(artifacts ?? []);
    } finally {
      setExecutablesLoading(false);
    }
  }, [request]);

  useEffect(() => {
    refreshExecutables().catch((error) => addConsoleLine("stderr", `Executable artifact refresh failed: ${error.message}`));
  }, [addConsoleLine, refreshExecutables]);

  const showPackageNotification = useCallback((events) => {
    const changedPackages = events
      .flatMap((event) => [...(event.added ?? []), ...(event.updated ?? [])])
      .map((pkg) => `${pkg.id} ${pkg.version}`.trim());
    const packageText = changedPackages.length > 0
      ? changedPackages.join(", ")
      : "A package change was reconciled.";

    setPackageNotification({
      title: "New package detected",
      message: `${packageText} is ready. Enable its feature if needed, then reload shells.`,
      actionLabel: "Reload shells",
      actionKind: "reload-shells"
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

  function applyDesignerActivityUpdate(path, update) {
    const workflow = JSON.parse(workflowJson);
    const updated = updateActivityByDesignerPath(workflow, path, update);
    if (!updated)
      return;

    setWorkflowJson(JSON.stringify(workflow, null, 2));
    setWorkflowVersionId("");
    setArtifactId("");
    setExecutionId("");
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
      await refreshExecutables();
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
    const input = event.currentTarget;
    const file = event.target.files?.[0];
    if (!file)
      return;

    try {
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
        await refreshFeatures();
      });
    } finally {
      input.value = "";
    }
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
      if (packageUploadInputRef.current)
        packageUploadInputRef.current.value = "";
      activityVersionCache.current.clear();
      addConsoleLine(
        "stdout",
        `Reset complete: ${workflowRows} workflow row(s), ${activityRows} activity row(s), ${packageFiles} package file(s), ${packageDirectories} package folder(s).`
      );
      if (response.reconcile?.outcome) {
        addConsoleLine("stdout", `Reconcile ${response.reconcile.outcome}: ${response.reconcile.correlationId}`);
      }
      if (response.shells?.path) {
        addConsoleLine("stdout", `shells.json restored from baseline: ${response.shells.path}`);
      }
      if (response.reload?.reloadedShellCount != null) {
        addConsoleLine("stdout", `Reloaded ${response.reload.reloadedShellCount} active shell(s).`);
      }
      await refreshState();
      await refreshActivities();
      await refreshFeatures();
      await refreshExecutables();
    });
    setCompleted(new Set());
  }

  async function enableFeature() {
    await runAction("feature", `Enabling feature ${featureName} and reloading shells...`, async () => {
      const configuration = JSON.parse(featureConfig || "{}");
      const response = await request(`/_demo/shells/default/features/${encodeURIComponent(featureName)}`, {
        method: "PUT",
        body: JSON.stringify({ enabled: true, configuration })
      });
      setShellJson(response.json);
      addConsoleLine("stdout", `Feature enabled and shells.json saved: ${featureName}`);
      const reload = await reloadShellsCore();
      markComplete("reload");
      addConsoleLine("stdout", `Shells reloaded after refreshing ${reload.featureDescriptorCount} feature descriptor(s).`);
    });
  }

  async function toggleFeature(feature) {
    const nextEnabled = !feature.enabled;
    setTogglingFeatures((current) => new Set([...current, feature.id]));
    setFeatureCatalogItems((current) => current.map((item) =>
      item.id === feature.id ? { ...item, enabled: nextEnabled } : item));
    setStatus(`${nextEnabled ? "Enabling" : "Disabling"} feature ${feature.id}...`);
    addConsoleLine("stdout", `${nextEnabled ? "Enabling" : "Disabling"} feature ${feature.id}...`);

    try {
      const configuration = nextEnabled ? JSON.parse(feature.configurationJson || "{}") : {};
      const response = await request(`/_demo/shells/default/features/${encodeURIComponent(feature.id)}`, {
        method: "PUT",
        body: JSON.stringify({ enabled: nextEnabled, configuration })
      });
      setShellJson(response.json);
      await refreshState();
      await refreshFeatures();
      setStatus("Ready");
      setPackageNotification({
        title: "Feature changes pending",
        message: `${feature.id} was ${nextEnabled ? "enabled" : "disabled"} in shells.json. Apply changes to reload the default shell.`,
        actionLabel: "Apply changes",
        actionKind: "apply-feature-changes"
      });
      addConsoleLine("stdout", `Feature ${nextEnabled ? "enabled" : "disabled"} in shells.json: ${feature.id}`);
    } catch (error) {
      setStatus(error.message);
      addConsoleLine("stderr", error.message);
      await refreshFeatures().catch(() => {});
    } finally {
      setTogglingFeatures((current) => {
        const next = new Set(current);
        next.delete(feature.id);
        return next;
      });
    }
  }

  async function updateFeatureConfiguration(feature, configuration) {
    setSavingFeatureSettings((current) => new Set([...current, feature.id]));
    setStatus(`Saving settings for ${feature.id}...`);
    addConsoleLine("stdout", `Saving settings for ${feature.id}...`);

    try {
      const response = await request(`/_demo/shells/default/features/${encodeURIComponent(feature.id)}`, {
        method: "PUT",
        body: JSON.stringify({ enabled: true, configuration })
      });
      setShellJson(response.json);
      await refreshState();
      await refreshFeatures();
      setStatus("Ready");
      setPackageNotification({
        title: "Feature changes pending",
        message: `Settings for ${feature.id} were saved to shells.json. Apply changes to reload the default shell.`,
        actionLabel: "Apply changes",
        actionKind: "apply-feature-changes"
      });
      addConsoleLine("stdout", `Feature settings saved in shells.json: ${feature.id}`);
    } catch (error) {
      setStatus(error.message);
      addConsoleLine("stderr", error.message);
      await refreshFeatures().catch(() => {});
    } finally {
      setSavingFeatureSettings((current) => {
        const next = new Set(current);
        next.delete(feature.id);
        return next;
      });
    }
  }

  async function applyNotificationAction() {
    const actionKind = packageNotification?.actionKind;
    if (!actionKind)
      return;

    if (actionKind === "apply-feature-changes") {
      await runAction("feature", "Applying feature changes and reloading shells...", async () => {
        const response = await reloadShellsCore();
        markComplete("feature");
        markComplete("reload");
        addConsoleLine("stdout", `Feature changes applied after refreshing ${response.featureDescriptorCount} feature descriptor(s).`);
      });
      return;
    }

    if (actionKind === "reload-shells") {
      await reloadShells();
    }
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
      await refreshFeatures();
      addConsoleLine("stdout", "shells.json saved.");
    });
  }

  async function reloadShellsCore() {
    const response = await request("/_demo/shells/reload", {
      method: "POST",
      body: "{}"
    });
    setPackageNotification(null);
    activityVersionCache.current.clear();
    await refreshState();
    await refreshActivities();
    await refreshFeatures();
    await refreshExecutables();
    return response;
  }

  async function reloadShells() {
    await runAction("reload", "Reloading all shells...", async () => {
      const response = await reloadShellsCore();
      addConsoleLine("stdout", `Shells reloaded after refreshing ${response.featureDescriptorCount} feature descriptor(s).`);
    });
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

  const MainViewIcon = mainViewMeta.icon;

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
          <div className="theme-picker" role="group" aria-label="Theme controls">
            <span className="theme-preview" aria-hidden="true">
              {(themeFamilies.find((family) => family.id === themeFamily)?.colors[themeMode] ?? themeFamilies[0].colors.light).map((color) => (
                <span key={color} style={{ background: color }} />
              ))}
            </span>
            <select
              value={themeFamily}
              onChange={(event) => setThemeFamily(event.target.value)}
              aria-label="Theme"
              title="Theme"
            >
              {themeFamilies.map((family) => (
                <option key={family.id} value={family.id}>{family.label}</option>
              ))}
            </select>
            <button
              type="button"
              className="theme-mode-toggle"
              onClick={() => setThemeMode((current) => current === "dark" ? "light" : "dark")}
              title={themeMode === "dark" ? "Switch to light mode" : "Switch to dark mode"}
              aria-label={themeMode === "dark" ? "Switch to light mode" : "Switch to dark mode"}
              aria-pressed={themeMode === "dark"}
            >
              {themeMode === "dark" ? <Sun size={15} /> : <Moon size={15} />}
              <span>{themeMode === "dark" ? "Light" : "Dark"}</span>
            </button>
          </div>
        </div>
      </header>

      {packageNotification && (
        <div className="package-notification" role="alert">
          <Radio size={18} />
          <div>
            <strong>{packageNotification.title}</strong>
            <span>{packageNotification.message}</span>
          </div>
          {packageNotification.actionLabel && (
            <button type="button" className="small-button" onClick={applyNotificationAction} disabled={Boolean(busy)}>
              {packageNotification.actionLabel}
            </button>
          )}
        </div>
      )}

      <main className="workspace">
        <StepRail completed={completed} currentStep={currentStep} />

        <section className="editor-surface">
          <div className="panel-titlebar">
            <div>
              <h2>
                <MainViewIcon size={18} />
                {mainViewMeta.title}
              </h2>
              <p>{mainViewMeta.description}</p>
            </div>
            {mainView === "workflow" || mainView === "designer" ? (
              <select value={selectedSample} onChange={(event) => selectSample(event.target.value)}>
                {Object.entries(sampleWorkflows).map(([key, sample]) => (
                  <option key={key} value={key}>{sample.label}</option>
                ))}
              </select>
            ) : mainView === "activities" ? (
              <button type="button" className="small-button" onClick={refreshActivities} disabled={activitiesLoading}>
                {activitiesLoading ? "Refreshing" : "Refresh"}
              </button>
            ) : mainView === "features" ? (
              <button type="button" className="small-button" onClick={refreshFeatures} disabled={featuresLoading}>
                {featuresLoading ? "Refreshing" : "Refresh"}
              </button>
            ) : mainView === "stack" ? (
              <div className="artifact-toolbar-summary">
                <Boxes size={15} />
                <span>{demoStackPackages.length} references</span>
              </div>
            ) : (
              <button type="button" className="small-button" onClick={refreshExecutables} disabled={executablesLoading}>
                {executablesLoading ? "Refreshing" : "Refresh"}
              </button>
            )}
          </div>

          <div className="toolbar">
            <div className="view-tabs">
              <button type="button" className={mainView === "workflow" ? "active" : ""} onClick={() => setMainView("workflow")}>
                <FileJson size={15} />
                Workflow
              </button>
              <button type="button" className={mainView === "designer" ? "active" : ""} onClick={() => setMainView("designer")}>
                <GitBranch size={15} />
                Designer
              </button>
              <button type="button" className={mainView === "activities" ? "active" : ""} onClick={() => setMainView("activities")}>
                <Activity size={15} />
                Activities
              </button>
              <button type="button" className={mainView === "features" ? "active" : ""} onClick={() => setMainView("features")}>
                <PlugZap size={15} />
                Features
              </button>
              <button type="button" className={mainView === "artifacts" ? "active" : ""} onClick={() => setMainView("artifacts")}>
                <Rocket size={15} />
                Artifacts
              </button>
              <button type="button" className={mainView === "stack" ? "active" : ""} onClick={() => setMainView("stack")}>
                <Boxes size={15} />
                Stack
              </button>
            </div>
            {mainView === "workflow" || mainView === "designer" ? (
              <>
                <ActionButton icon={Save} busy={busy === "save"} onClick={saveWorkflow}>Save</ActionButton>
                <ActionButton icon={Rocket} busy={busy === "publish"} onClick={publishWorkflow} disabled={!workflowVersionId}>Publish</ActionButton>
                <ActionButton icon={Play} busy={busy === "execute"} onClick={() => executeWorkflow("execute")} disabled={!artifactId}>Execute</ActionButton>
              </>
            ) : mainView === "activities" ? (
              <div className="activity-search">
                <Search size={15} />
                <input value={activitySearch} onChange={(event) => setActivitySearch(event.target.value)} placeholder="Filter activities" />
              </div>
            ) : mainView === "features" ? (
              <div className="activity-search">
                <Search size={15} />
                <input value={featureSearch} onChange={(event) => setFeatureSearch(event.target.value)} placeholder="Filter features" />
              </div>
            ) : mainView === "artifacts" ? (
              <div className="artifact-toolbar-summary">
                <Rocket size={15} />
                <span>{executables.length} artifact{executables.length === 1 ? "" : "s"}</span>
              </div>
            ) : mainView === "stack" ? (
              <div className="artifact-toolbar-summary">
                <Boxes size={15} />
                <span>{demoStackPackages.length} packages</span>
              </div>
            ) : (
              null
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
          ) : mainView === "designer" ? (
            <WorkflowDesigner
              parseError={designerParseResult.error}
              model={designerModel}
              selectedPath={selectedDesignerPath}
              selectedActivity={selectedDesignerActivity}
              onSelectPath={setSelectedDesignerPath}
              onUpdateActivity={applyDesignerActivityUpdate}
            />
          ) : mainView === "activities" ? (
            <ActivityCatalog activities={filteredActivities} totalCount={activities.length} loading={activitiesLoading} />
          ) : mainView === "features" ? (
            <FeatureCatalog
              features={filteredFeatureCatalogItems}
              totalCount={featureCatalogItems.length}
              loading={featuresLoading}
              togglingFeatures={togglingFeatures}
              savingFeatureSettings={savingFeatureSettings}
              onToggle={toggleFeature}
              onUpdateConfiguration={updateFeatureConfiguration}
            />
          ) : mainView === "stack" ? (
            <DemoPackageStack packages={demoStackPackages} />
          ) : (
            <WorkflowExecutableArtifacts artifacts={executables} loading={executablesLoading} />
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
              <input ref={packageUploadInputRef} type="file" accept=".nupkg" onChange={uploadPackage} />
            </label>
            <p className="path-hint">{dropFolder || "packages"}</p>
            <div className="split-actions package-actions">
              <ActionButton icon={RotateCcw} busy={busy === "reset"} onClick={resetDemo}>Reset</ActionButton>
            </div>
          </section>

          <section className="ops-section compact">
            <h3>Installed Packages</h3>
            <div className="package-list">
              {packages.length === 0 && <p className="muted">No installed packages reported yet.</p>}
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
              <ActionButton icon={PlugZap} busy={busy === "feature"} onClick={enableFeature}>Enable & reload</ActionButton>
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

function WorkflowDesigner({
  parseError,
  model,
  selectedPath,
  selectedActivity,
  onSelectPath,
  onUpdateActivity
}) {
  const nodes = useMemo(() => model.nodes.map((node) => ({
    ...node,
    selected: node.id === selectedPath
  })), [model.nodes, selectedPath]);

  const literalInputs = getActivityLiteralInputs(selectedActivity);

  if (parseError) {
    return (
      <div className="designer-empty">
        <GitBranch size={28} />
        <strong>Designer paused</strong>
        <span>{parseError}</span>
      </div>
    );
  }

  return (
    <div className="workflow-designer">
      <div className="designer-canvas">
        <div className="designer-surface-label">
          <span>{model.mode}</span>
          <strong>{model.mode === "single" ? "Root activity" : `${model.containerTitle} surface`}</strong>
        </div>
        {model.nodes.length === 0 && (
          <div className="designer-surface-empty">
            <GitBranch size={24} />
            <strong>No activities in this {model.mode}</strong>
            <span>Add child activities in the JSON editor to populate this surface.</span>
          </div>
        )}
        <ReactFlow
          nodes={nodes}
          edges={model.edges}
          nodeTypes={designerNodeTypes}
          fitView
          minZoom={0.45}
          maxZoom={1.4}
          nodesDraggable
          nodesConnectable={false}
          elementsSelectable
          onNodeClick={(_, node) => onSelectPath(node.id)}
        >
          <MiniMap pannable zoomable />
          <Controls />
          <Background gap={18} size={1} />
        </ReactFlow>
      </div>
      <aside className="designer-properties">
        <div className="designer-properties-heading">
          <span>{model.mode === "single" ? "Activity" : `${model.containerTitle} surface`}</span>
          <strong>{selectedActivity ? getActivityDisplayName(selectedActivity) : "No activity selected"}</strong>
        </div>
        {!selectedActivity ? (
          <p className="muted">Select an activity node to edit its properties.</p>
        ) : (
          <>
            <label className="designer-field">
              <span>Node ID</span>
              <input
                value={selectedActivity.nodeId ?? ""}
                onChange={(event) => onUpdateActivity(selectedPath, (activity) => {
                  activity.nodeId = event.target.value;
                })}
              />
            </label>
            <label className="designer-field">
              <span>Activity version ID</span>
              <input
                value={selectedActivity.activityVersionId ?? ""}
                onChange={(event) => onUpdateActivity(selectedPath, (activity) => {
                  activity.activityVersionId = event.target.value;
                })}
              />
            </label>
            <div className="designer-inputs">
              <h3>Inputs</h3>
              {literalInputs.length === 0 && <p className="muted">This activity has no literal inputs to edit.</p>}
              {literalInputs.map((input) => (
                <label className="designer-field" key={input.referenceKey}>
                  <span>{input.referenceKey}</span>
                  <input
                    value={input.value?.value ?? ""}
                    onChange={(event) => onUpdateActivity(selectedPath, (activity) => {
                      setActivityLiteralInput(activity, input.referenceKey, event.target.value);
                    })}
                  />
                </label>
              ))}
            </div>
          </>
        )}
      </aside>
    </div>
  );
}

const ActivityDesignerNode = memo(function ActivityDesignerNode({ data, selected }) {
  return (
    <div className={`designer-node ${selected ? "selected" : ""}`}>
      <Handle type="target" position={Position.Top} className="designer-node-handle" />
      <div className="designer-node-icon"><Activity size={16} /></div>
      <div>
        <span>{data.role}</span>
        <strong>{data.title}</strong>
        <code>{data.nodeId}</code>
        {data.subtitle && <p>{data.subtitle}</p>}
      </div>
      <Handle type="source" position={Position.Bottom} className="designer-node-handle" />
    </div>
  );
});

const designerNodeTypes = {
  activity: ActivityDesignerNode
};

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
              <strong>{getActivityCatalogDisplayName(activity)}</strong>
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

function DemoPackageStack({ packages }) {
  return (
    <div className="stack-page">
      <div className="stack-grid">
        {packages.map((item) => (
          <article className="stack-card" key={item.name}>
            <a className={`stack-visual ${item.imageMode === "logo" ? "logo" : "preview"}`} href={item.url} target="_blank" rel="noreferrer" aria-label={`Open ${item.name}`}>
              {item.imageMode === "logo" ? (
                <span className="stack-logo-plate">
                  <img src={item.imageUrl} alt={item.imageAlt} loading="lazy" />
                </span>
              ) : (
                <img src={item.imageUrl} alt={item.imageAlt} loading="lazy" />
              )}
            </a>
            <div className="stack-card-body">
              <div className="stack-card-heading">
                <div>
                  <span>{item.role}</span>
                  <h3>{item.name}</h3>
                </div>
                <a className="stack-link" href={item.url} target="_blank" rel="noreferrer" aria-label={`Open ${item.name} website`}>
                  <ExternalLink size={15} />
                </a>
              </div>
              <p>{item.description}</p>
              <div className="stack-tags">
                {item.details.map((detail) => <span key={detail}>{detail}</span>)}
              </div>
            </div>
          </article>
        ))}
      </div>
    </div>
  );
}

function FeatureCatalog({ features, totalCount, loading, togglingFeatures, savingFeatureSettings, onToggle, onUpdateConfiguration }) {
  const enabledCount = features.filter((feature) => feature.enabled).length;

  return (
    <div className="feature-catalog">
      <div className="activity-summary">
        <strong>{enabledCount}</strong>
        <span>{features.length === totalCount ? "enabled shown" : `enabled shown of ${totalCount}`}</span>
      </div>
      <div className="feature-list">
        {loading && features.length === 0 && <p className="muted">Loading feature catalog...</p>}
        {!loading && features.length === 0 && <p className="muted">No features match the current filter.</p>}
        {features.map((feature) => {
          const toggling = togglingFeatures.has(feature.id);
          const savingSettings = savingFeatureSettings.has(feature.id);
          const disabled = Boolean(feature.readError) || toggling || savingSettings;
          return (
            <div className={`feature-row ${feature.enabled ? "enabled" : ""} ${feature.readError ? "warning" : ""}`} key={feature.id}>
              <button
                type="button"
                className="feature-toggle"
                role="switch"
                aria-checked={feature.enabled}
                disabled={disabled}
                title={`${feature.enabled ? "Disable" : "Enable"} ${feature.id}`}
                onClick={() => onToggle(feature)}
              >
                <span />
              </button>
              <div className="feature-row-main">
                <div className="feature-title-line">
                  <strong>{feature.displayName || feature.id}</strong>
                  {feature.advanced && <span className="feature-flag">Advanced</span>}
                  {feature.experimental && <span className="feature-flag warning">Experimental</span>}
                </div>
                <span>{feature.id}</span>
                {feature.description && <p>{feature.description}</p>}
                <div className="feature-detail-list">
                  {feature.packageId && <span>Package: <strong>{feature.packageId}</strong>{feature.packageVersion ? ` ${feature.packageVersion}` : ""}</span>}
                  {feature.manifestPath && <span>Manifest: <strong>{feature.manifestPath}</strong></span>}
                  {feature.manifestHash && <span>Hash: <code>{feature.manifestHash.slice(0, 12)}</code></span>}
                </div>
                {feature.readError && (
                  <div className="feature-warning-panel">
                    <strong>Package manifest could not be read</strong>
                    <span>Issue: {feature.readError}</span>
                    <span>Action: add an <code>elsa-package.json</code> file at the package root, or <code>build/elsa-package.json</code>, then upload or reconcile the package again.</span>
                    <span>Tip: the runtime feature may still appear separately if the assembly registered one; use that runtime feature row for toggling.</span>
                  </div>
                )}
                {(feature.categories?.length ?? 0) > 0 && (
                  <div className="feature-category-list">
                    {feature.categories.map((category) => <span key={category}>{category}</span>)}
                  </div>
                )}
                <FeatureSettings
                  feature={feature}
                  disabled={disabled || !feature.enabled}
                  saving={savingSettings}
                  onUpdateConfiguration={onUpdateConfiguration}
                />
              </div>
              <div className="feature-row-meta">
                <span>{getFeatureSourceLabel(feature)}</span>
                <code>{feature.packageId ? `${feature.packageId} ${feature.packageVersion ?? ""}`.trim() : feature.sourceKind}</code>
                {feature.manifestPath && <small>{feature.manifestPath}</small>}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

function FeatureSettings({ feature, disabled, saving, onUpdateConfiguration }) {
  const settings = feature.settings ?? [];
  const initialConfiguration = useMemo(
    () => parseFeatureConfiguration(feature.configurationJson),
    [feature.configurationJson]
  );
  const [draft, setDraft] = useState(initialConfiguration);
  const [error, setError] = useState("");

  useEffect(() => {
    setDraft(initialConfiguration);
    setError("");
  }, [feature.id, initialConfiguration]);

  if (settings.length === 0) {
    if (feature.sourceKind === "manifest" && !feature.readError)
      return <div className="feature-settings-empty">No configurable settings exposed by this manifest.</div>;

    return null;
  }

  const handleSave = () => {
    const next = { ...initialConfiguration };
    try {
      for (const setting of settings) {
        const value = draft[setting.name];
        if (isEmptySettingValue(value) && !setting.required) {
          delete next[setting.name];
          continue;
        }

        next[setting.name] = normalizeSettingValue(setting, value);
      }
    } catch (saveError) {
      setError(saveError.message);
      return;
    }

    setError("");
    onUpdateConfiguration(feature, next);
  };

  return (
    <div className="feature-settings-panel">
      <div className="feature-settings-header">
        <span>{settings.length} setting{settings.length === 1 ? "" : "s"}</span>
        <button type="button" className="small-button" onClick={handleSave} disabled={disabled || saving}>
          {saving ? "Saving" : "Save settings"}
        </button>
      </div>
      <div className="feature-settings-grid">
        {settings.map((setting) => (
          <FeatureSettingInput
            key={setting.name}
            setting={setting}
            value={getDraftSettingValue(draft, setting)}
            disabled={disabled || saving}
            onChange={(value) => setDraft((current) => ({ ...current, [setting.name]: value }))}
          />
        ))}
      </div>
      {error && <p className="feature-settings-error">{error}</p>}
    </div>
  );
}

function FeatureSettingInput({ setting, value, disabled, onChange }) {
  const inputKind = getFeatureSettingInputKind(setting);
  const inputId = `feature-setting-${setting.name}`;
  const label = setting.displayName || setting.name;

  return (
    <label className={`feature-setting-field ${inputKind === "checkbox" ? "checkbox" : ""}`} htmlFor={inputId}>
      <span>
        <strong>{label}</strong>
        {setting.required && <em>Required</em>}
        {setting.restartRequired && <em>Reload</em>}
        {setting.advanced && <em>Advanced</em>}
      </span>
      {setting.description && <small>{setting.description}</small>}
      {renderFeatureSettingInput(inputKind, inputId, setting, value, disabled, onChange)}
      <code>{setting.name}</code>
    </label>
  );
}

function renderFeatureSettingInput(inputKind, inputId, setting, value, disabled, onChange) {
  if (inputKind === "checkbox") {
    return (
      <input
        id={inputId}
        type="checkbox"
        checked={Boolean(value)}
        disabled={disabled}
        onChange={(event) => onChange(event.target.checked)}
      />
    );
  }

  if (inputKind === "select") {
    const selected = JSON.stringify(value ?? "");
    return (
      <select
        id={inputId}
        value={selected}
        disabled={disabled}
        onChange={(event) => {
          const option = (setting.options ?? []).find((candidate) => JSON.stringify(candidate.value) === event.target.value);
          onChange(option?.value ?? "");
        }}
      >
        {!setting.required && <option value={JSON.stringify("")}>Empty</option>}
        {(setting.options ?? []).map((option) => (
          <option key={`${setting.name}-${JSON.stringify(option.value)}`} value={JSON.stringify(option.value)}>
            {option.label}
          </option>
        ))}
      </select>
    );
  }

  if (inputKind === "textarea") {
    return (
      <textarea
        id={inputId}
        value={formatDraftSettingValue(value)}
        disabled={disabled}
        rows={4}
        onChange={(event) => onChange(event.target.value)}
      />
    );
  }

  return (
    <input
      id={inputId}
      type={inputKind}
      value={formatDraftSettingValue(value)}
      disabled={disabled}
      placeholder={setting.uiHint === "duration" ? "00:05:00" : undefined}
      onChange={(event) => onChange(event.target.value)}
    />
  );
}

function parseFeatureConfiguration(configurationJson) {
  try {
    const parsed = JSON.parse(configurationJson || "{}");
    return parsed && typeof parsed === "object" && !Array.isArray(parsed) ? parsed : {};
  } catch {
    return {};
  }
}

function getDraftSettingValue(draft, setting) {
  if (Object.prototype.hasOwnProperty.call(draft, setting.name))
    return draft[setting.name];

  if (setting.defaultValue !== undefined && setting.defaultValue !== null)
    return setting.defaultValue;

  return setting.jsonType === "boolean" ? false : "";
}

function getFeatureSettingInputKind(setting) {
  const hint = (setting.uiHint || "").toLowerCase();
  const jsonType = (setting.jsonType || "").toLowerCase();

  if ((setting.options?.length ?? 0) > 0 || hint.includes("select"))
    return "select";

  if (jsonType === "boolean")
    return "checkbox";

  if (setting.secret || setting.sensitive)
    return "password";

  if (jsonType === "integer" || jsonType === "number")
    return "number";

  if (jsonType === "object" || jsonType === "array" || hint.includes("json") || hint.includes("textarea"))
    return "textarea";

  return "text";
}

function normalizeSettingValue(setting, value) {
  const jsonType = (setting.jsonType || "").toLowerCase();

  if (jsonType === "integer") {
    const number = Number.parseInt(value, 10);
    return Number.isNaN(number) ? 0 : number;
  }

  if (jsonType === "number") {
    const number = Number.parseFloat(value);
    return Number.isNaN(number) ? 0 : number;
  }

  if (jsonType === "boolean")
    return Boolean(value);

  if (jsonType === "object" || jsonType === "array") {
    if (typeof value !== "string")
      return value;

    return JSON.parse(value || (jsonType === "array" ? "[]" : "{}"));
  }

  return value;
}

function formatDraftSettingValue(value) {
  if (value === null || value === undefined)
    return "";

  if (typeof value === "object")
    return JSON.stringify(value, null, 2);

  return String(value);
}

function isEmptySettingValue(value) {
  return value === undefined || value === null || value === "";
}

function getFeatureSourceLabel(feature) {
  if (feature.sourceKind === "manifest")
    return "Package manifest";

  if (feature.sourceKind === "manifest-error")
    return "Package warning";

  if (feature.sourceKind === "runtime")
    return "Runtime catalog";

  return "shells.json";
}

function WorkflowExecutableArtifacts({ artifacts, loading }) {
  return (
    <div className="executable-artifacts">
      <div className="activity-summary">
        <strong>{artifacts.length}</strong>
        <span>published</span>
      </div>
      <div className="executable-list">
        {loading && artifacts.length === 0 && <p className="muted">Loading executable artifacts...</p>}
        {!loading && artifacts.length === 0 && <p className="muted">No workflow executable artifacts have been published yet.</p>}
        {artifacts.map((artifact) => (
          <div className="executable-row" key={artifact.artifactId}>
            <div className="activity-row-icon"><Rocket size={15} /></div>
            <div className="executable-row-main">
              <strong>{artifact.artifactId}</strong>
              <span>{artifact.definitionId} / {artifact.definitionVersionId}</span>
              <p>
                {artifact.rootActivityType} {artifact.rootActivityVersion}
                {" · "}
                {artifact.nodeCount} node{artifact.nodeCount === 1 ? "" : "s"}
                {" · "}
                {artifact.resumeTargetCount} resume target{artifact.resumeTargetCount === 1 ? "" : "s"}
              </p>
            </div>
            <div className="executable-row-meta">
              <span>{formatDateTime(artifact.publishedAt ?? artifact.createdAt)}</span>
              <code>{artifact.artifactVersion} / {artifact.artifactHash}</code>
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
