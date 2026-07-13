import React from "react";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { loadAttention, type AttentionSnapshot } from "./attentionApi";
import { AttentionWidget } from "./AttentionWidget";
import "./styles.css";
export function register(api: ElsaStudioModuleApi) { api.dashboardWidgets.add({ id: "attention.queue", moduleId: "Elsa.Studio.Attention", title: "Attention", description: "Conditions that may need investigation.", order: 0, defaultVisible: true, defaultSize: "wide", supportedSizes: ["wide", "full"], minimumRefreshIntervalMs: 60_000, load: ({ signal }) => loadAttention(api, signal), component: ({ snapshot }) => <AttentionWidget api={api} snapshot={snapshot as AttentionSnapshot | undefined} settings={undefined} /> }); }
