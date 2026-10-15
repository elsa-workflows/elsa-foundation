# Your first workflow

Start the [local Workbench and Studio demo](quickstart.md) and sign in to Studio.

You will publish a workflow containing one Write Line activity, run that published artifact, and inspect its result:

![Create a definition, publish an artifact, run it, then inspect its execution](../assets/first-workflow.svg)

> **Tested with** the published-image pair (Studio revision `7cb2f2c2`, Write Line `4.0.5-preview` and Flowchart `4.0.3-preview`; screenshots show definition `14J7GEWeguo` and run `14J7GJS3fOg`). The steps below use the labels from current Studio source on Foundation `main`. The screenshots are from that recorded pair and can still show the older **Workflow Definitions** sidebar item.

## Create and publish

1. In the main sidebar, open **Workflows**. The page heading is **Definitions**. Choose **Create** (or **Create workflow** on an empty list). In the **Create Workflow** dialog, keep the default **Flowchart** root activity, enter a display name, and choose **Create**.
2. On the empty canvas, choose **Add activity**, search for `WriteLine` (or `Write Line`), and select **Write Line** (singular).
3. Set **Text** to a recognizable message, such as `Hello from my first Elsa workflow`.
4. Choose **Review & publish**. In the **Review and publish** dialog, choose **Publish**. Note the published artifact identifier. After success you can choose **Open published executable**, or continue to Executables below.

<img src="../assets/elsa4/elsa4-03-workflow-definitions_studio-7cb2f2c2_act-4.0.5-preview.png" alt="Studio Definitions list showing one draft workflow named Hello first workflow" width="800">

*The Definitions list with the new draft. The recorded screenshot may still show the older **Workflow Definitions** sidebar label.*

<img src="../assets/elsa4/elsa4-04-designer_studio-7cb2f2c2_act-4.0.5-preview.png" alt="Studio designer with a Flowchart containing a single Write Line activity marked as the start" width="800">

*The designer with one Write Line activity.*

<img src="../assets/elsa4/elsa4-05-review-and-publish_studio-7cb2f2c2_act-4.0.5-preview.png" alt="Review and publish dialog showing the default publication channel, version and captured changes" width="800">

*The Review and publish dialog. The proposed version depends on what is already published.*

## Run the published workflow

Open **More tools**, then **Executables**. Choose **Run** on the artifact you just published. The editor toolbar's **Run** action starts a draft test run; use Executables for this published-workflow journey.

<img src="../assets/elsa4/elsa4-06-executables_studio-7cb2f2c2_act-4.0.5-preview.png" alt="Studio Executables list showing the published artifact with Inspect, Run and Export actions" width="800">

*The published artifact in Executables, with **Inspect**, **Run** and **Export**.*

After it starts, choose **Open Run** (with the execution id) in the status line, or open **Runs** in the main sidebar and select the new run. Check that it belongs to the published artifact and has status **Completed**.

The run page heading is **Run**. Select the Write Line activity on the canvas to open the **Run details** panel. Its tabs are **Timeline**, **Activity**, **Issues** and **Details**. They render in uppercase (ACTIVITY, ISSUES) and can clip at typical 1024 px widths — hover a tab or widen the window if the label is truncated. The timeline should contain exactly one completed Write Line activity. Open **Activity** and **Issues**; the run should have zero incidents and an empty Issues view.

<img src="../assets/elsa4/elsa4-07-run-timeline_studio-7cb2f2c2_act-4.0.5-preview.png" alt="Completed run with the Write Line activity marked Completed and a timeline listing Flowchart and Write Line as OK" width="800">

*The completed run and its timeline.*

In your demo terminal, inspect the Workbench output:

```bash
docker compose -p elsa-first-workflow -f docker-compose.images.yml logs --no-color elsa-workbench
```

Use the `-p` project name you chose in the quickstart (that flag overrides the compose file's `name: elsa-stack`). Your unique message should appear once for this run. If you ran the workflow again, account for those additional runs when reading the log.

## Understand the result

You created a definition, published an artifact, and executed that artifact. The [How a workflow executes](https://github.com/elsa-workflows/elsa-foundation/blob/main/docs/how-a-workflow-executes.md) guide explains the runtime path in more detail. Use the [local demo cleanup steps](quickstart.md#stop-the-demo) when you finish.

> *Good to know:* the [recorded browser verification](https://github.com/elsa-workflows/elsa-foundation/blob/69f067104c1a977d897911db7ecf16037cfbe542/docs/contributing/published-image-pair.md#recorded-image-provenance-and-hosted-result) covers a specific image pair and Linux/amd64 environment. It is automated technical evidence, rather than a human newcomer trial or proof for every later preview.

---

[Edit this page on GitHub](https://github.com/elsa-workflows/elsa-foundation/edit/main/website/docs/essentials/first-workflow.md).
