# Elsa Studio

Elsa Studio is the visual client you use with an Elsa server: you design workflows, publish them, run them and inspect what happened.

To set it up next to a server, follow the Foundation [Docker Compose quick start](https://github.com/elsa-workflows/elsa-foundation/blob/main/docker/compose/README.md#quick-start--published-images-no-clone-or-build). It documents the published Workbench and Studio reference pair and the settings that connect them. Use matching server and Studio versions.

Studio's source lives in the [`Studio/` folder](https://github.com/elsa-workflows/elsa-foundation/tree/main/Studio) of the Foundation repository. Older issues remain in the former [Elsa Foundation Studio repository](https://github.com/elsa-workflows/elsa-foundation-studio).

## Run and inspect in the editor

Use **Test Run** in a workflow definition to execute the captured draft in the connected environment. The run appears on the editor's canvas, where you can select an activity and inspect its recorded inputs, outputs, outcomes and incidents. Test Runs are temporary executions; their activities can still produce real side effects.

If an activity ran more than once, choose the exact occurrence in its execution browser. Keep that occurrence selected while following later updates, or pause following to inspect the last confirmed evidence. **Pause following** pauses observation; the workflow keeps running.

The **Runtime** panel offers the supported waits and Stop control for the attached Test Run. Delivering an event targets the selected active wait. A Stop receipt remains pending until Runtime confirms the execution's terminal state. When the connection is unavailable, Studio labels retained evidence and disables commands that cannot be verified.

Choose **Return to editing** to resume the separate draft. Later edits do not change the executed snapshot. **Inspect captured snapshot** opens that snapshot read only and retains your draft for your return. After a page reload, **Restore Test Run** rechecks the saved design, access and run expiry. If the design no longer matches, open the existing run instead.

## Follow an existing run

Open a workflow instance from **Runs** to inspect its current status and follow subsequent execution on the same canvas, including when the instance was already running before you opened it. Activity selection, nested scope and viewport remain stable during updates and connection recovery.

Both views show brief visual cues for newly recorded activity changes. Sequence connectors and supported Flowchart connections animate only when the recorded evidence establishes that route. Updates can arrive together, and missing evidence does not imply that an activity or connection never executed. Other structures retain their supported activity-state cues. Reduced-motion preferences suppress the new motion; status text and inspection remain available.

Captured values depend on the server's capture policy and your access. Missing, unavailable or redacted values stay explicitly identified. Following a run does not enable additional data capture or provide engine pause, rewind or recorded playback.

## What Studio looks like

<img src="../assets/elsa4/elsa4-04-designer_studio-7cb2f2c2_act-4.0.5-preview.png" alt="Studio designer with activity palette, Flowchart canvas and inspector panel" width="800">

*The workflow designer.*

<img src="../assets/elsa4/elsa4-07-run-timeline_studio-7cb2f2c2_act-4.0.5-preview.png" alt="Studio run view with a completed Write Line activity and execution timeline" width="800">

*Inspecting a completed run.*

Screenshots taken with Studio rev `7cb2f2c2`, Write Line `4.0.5-preview` and Flowchart `4.0.3-preview` (run `14J7GJS3fOg`).

---

[Edit this page on GitHub](https://github.com/elsa-workflows/elsa-foundation/edit/main/website/docs/studio/README.md).
