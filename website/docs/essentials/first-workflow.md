# Your first workflow

Start the [local Workbench and Studio demo](quickstart.md) and sign in to Studio. This walkthrough follows the published-image pair's recorded UI journey. Later preview versions can change labels or behavior.

You will publish a workflow containing one Write Line activity, run that published artifact, and inspect its result:

![Create a definition, publish an artifact, run it, then inspect its execution](../assets/first-workflow.svg)

## Create and publish

1. Open **Workflows → Definitions** and create a workflow using the default Flowchart.
2. Choose **Add activity**, search for `WriteLine`, and select **Write Line** (singular).
3. Set **Text** to a recognizable message, such as `Hello from my first Elsa workflow`.
4. Choose **Review & publish**, then **Publish** in the dialog. Note the published artifact identifier.

## Run the published workflow

Open **Workflows → Executables** and run the artifact you just published. The editor's Run action starts a draft test run; use Executables to follow this published-workflow journey.

Open the resulting run in history. Check that it belongs to the published artifact and has status **Completed**. Its timeline should contain exactly one completed Write Line activity. Open the activity's **Activity** tab and the **Issues** tab; the run should have zero incidents and an empty Issues view.

In your demo terminal, inspect the Workbench output:

```bash
docker compose -p elsa-first-workflow -f docker-compose.images.yml logs --no-color elsa-workbench
```

Use the project name you chose in the quickstart. Your unique message should appear once for this run. If you ran the workflow again, account for those additional runs when reading the log.

## Understand the result

You created a definition, published an artifact, and executed that artifact. The [workflow execution guide](https://github.com/elsa-workflows/elsa-foundation/blob/main/docs/how-a-workflow-executes.md) explains the runtime path in more detail. Use the [local demo cleanup steps](quickstart.md#stop-the-demo) when you finish.

The [recorded browser verification](https://github.com/elsa-workflows/elsa-foundation/blob/69f067104c1a977d897911db7ecf16037cfbe542/docs/contributing/published-image-pair.md#recorded-image-provenance-and-hosted-result) covers a specific image pair and Linux/amd64 environment. It is automated technical evidence, rather than a human newcomer trial or proof for every later preview.
