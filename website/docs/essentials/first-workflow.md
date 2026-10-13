# Your first workflow

Start the [local Workbench and Studio demo](quickstart.md) and sign in to Studio. This walkthrough follows the published-image pair's recorded UI journey. Later preview versions can change labels or behavior.

You will publish a workflow containing one Write Line activity, run that published artifact, and inspect its result:

![Create a definition, publish an artifact, run it, then inspect its execution](../assets/first-workflow.svg)

## Create and publish

1. Open **Workflows → Definitions** and create a workflow using the default Flowchart.
2. Choose **Add activity**, search for `WriteLine`, and select **Write Line** (singular).
3. Set **Text** to a recognizable message, such as `Hello from my first Elsa workflow`.
4. Choose **Review & publish**, then **Publish** in the dialog. Note the published artifact identifier.

<img src="../assets/elsa4/elsa4-03-workflow-definitions_studio-7cb2f2c2_act-4.0.5-preview.png" alt="Studio Workflow Definitions list showing one draft workflow named Hello first workflow" width="800">

*The Workflow Definitions list with the new draft, definition `14J7GEWeguo` (Studio rev `7cb2f2c2`; Write Line `4.0.5-preview`, Flowchart `4.0.3-preview`.)*

<img src="../assets/elsa4/elsa4-04-designer_studio-7cb2f2c2_act-4.0.5-preview.png" alt="Studio designer with a Flowchart containing a single Write Line activity marked as the start" width="800">

*The designer with one Write Line activity (Studio rev `7cb2f2c2`; Write Line `4.0.5-preview`, Flowchart `4.0.3-preview`.)*

<img src="../assets/elsa4/elsa4-05-review-and-publish_studio-7cb2f2c2_act-4.0.5-preview.png" alt="Review and publish dialog showing the default publication channel, version and captured changes" width="800">

*The Review and publish dialog. The proposed version depends on what is already published (Studio rev `7cb2f2c2`; Write Line `4.0.5-preview`, Flowchart `4.0.3-preview`.)*

## Run the published workflow

Open **Workflows → Executables** and run the artifact you just published. The editor's Run action starts a draft test run; use Executables to follow this published-workflow journey.

<img src="../assets/elsa4/elsa4-06-executables_studio-7cb2f2c2_act-4.0.5-preview.png" alt="Studio Executables list showing the published artifact with Inspect, Run and Export actions" width="800">

*The published artifact in Executables (Studio rev `7cb2f2c2`; Write Line `4.0.5-preview`, Flowchart `4.0.3-preview`.)*

Open the resulting run in history. Check that it belongs to the published artifact and has status **Completed**. Its timeline should contain exactly one completed Write Line activity. Open the activity's **Activity** tab and the **Issues** tab; the run should have zero incidents and an empty Issues view.

<img src="../assets/elsa4/elsa4-07-run-timeline_studio-7cb2f2c2_act-4.0.5-preview.png" alt="Completed run with the Write Line activity marked Completed and a timeline listing Flowchart and Write Line as OK" width="800">

*Completed run `14J7GJS3fOg` of definition `14J7GEWeguo` and its timeline (Studio rev `7cb2f2c2`; Write Line `4.0.5-preview`, Flowchart `4.0.3-preview`.)*

In your demo terminal, inspect the Workbench output:

```bash
docker compose -p elsa-first-workflow -f docker-compose.images.yml logs --no-color elsa-workbench
```

Use the project name you chose in the quickstart. Your unique message should appear once for this run. If you ran the workflow again, account for those additional runs when reading the log.

## Understand the result

You created a definition, published an artifact, and executed that artifact. The [workflow execution guide](https://github.com/elsa-workflows/elsa-foundation/blob/main/docs/how-a-workflow-executes.md) explains the runtime path in more detail. Use the [local demo cleanup steps](quickstart.md#stop-the-demo) when you finish.

The [recorded browser verification](https://github.com/elsa-workflows/elsa-foundation/blob/69f067104c1a977d897911db7ecf16037cfbe542/docs/contributing/published-image-pair.md#recorded-image-provenance-and-hosted-result) covers a specific image pair and Linux/amd64 environment. It is automated technical evidence, rather than a human newcomer trial or proof for every later preview.

---

[Edit this page on GitHub](https://github.com/elsa-workflows/elsa-foundation/edit/main/website/docs/essentials/first-workflow.md).
