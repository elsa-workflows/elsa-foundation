# Try a direct OpenAI agent in Studio

Run a Workbench containing `Elsa.Agent.OpenAI` and enable `OpenAIAgents`.
The stock Workbench includes that provider; a custom host also adds its assembly to feature discovery.
For connections that survive restart, select `ConnectionsEntityFrameworkCore` with the host's database;
Workbench makes that feature available without selecting it by default. Use Secrets persistence and a
stable local encryption key, and keep credentials out of the activity definition.

This recipe uses the current activity-backed Agent editor and publication path. The newer
Agent-owned source API stores unpublished working copies and snapshots; T066/T068 still own
its publication and Studio integration. See the [source delivery boundary](../plan.md#3790-source-slice-placement-and-transition).

## Connection

In Connections, create a connection with these values. The provider's capability declarations follow
[the GPT-4.1 mini documentation](https://developers.openai.com/api/docs/models/gpt-4.1-mini).

| Field | Value |
|---|---|
| Name | `openai-demo` |
| Display name | OpenAI demo — GPT-4.1 mini |
| Type | `agent-openai` |
| Authentication | `api-key` |
| HTTPS endpoint | `https://api.openai.com` |
| Allowed hosts | `api.openai.com` |
| Model or deployment | `gpt-4.1-mini` |
| Maximum output tokens | `32768` |
| Structured JSON results supported | Yes |
| Temperature setting supported | Yes |
| API key | Enter through the secret field; never put it in ordinary settings |

The connection test checks configuration and secret availability. It does not perform inference.

## Agent

Create **Product Copywriter**, select the connection above, and enter these instructions:

> You are a product copywriter. Read the product brief from the Brief input. Return a clear, factual Headline of at most 8 words and a Blurb of at most 45 words. Use only facts in the brief; do not invent claims. Return only the JSON object required by the result schema.

Set temperature to `0.2`, maximum output tokens to `300`, maximum model calls to `1` and timeout
to `60` seconds. Fresh invocation is the default. In **Inputs & outputs**, add:

| Direction | Name | Display name | Type | Required | Nullable |
|---|---|---|---|---|---|
| Input | `Brief` | Product brief | String, single value | Yes | No |
| Output | `Headline` | Headline | String, single value | Yes | No |
| Output | `Blurb` | Short blurb | String, single value | Yes | No |

Keep the required `Done` outcome and durable JSON storage. The public contract generates the model's
input/result schemas; there is no separate output schema to keep synchronized.

## Test and publish

Save, click **Test Run**, set **Product brief presence** to **Present value**, and enter:

> A reusable 750 ml stainless steel water bottle with double-wall insulation, a leak-resistant screw lid, and a carrying loop. Available in forest green. Designed for everyday commuting.

Click **Dispatch Test Run**. Expect **Completed** with no incidents. Open the run and select the
outer agent activity; its captured outputs contain `headline` and `blurb`. A qualified live run on
3 November 2026 returned “750ml Insulated Stainless Steel Water Bottle” and a short factual blurb.
Generated wording can vary. **Review & publish** creates an immutable version; creating another draft
keeps a safe place for further experiments. This bounded example does not establish broader Program acceptance.
