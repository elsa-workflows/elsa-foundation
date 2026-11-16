# Named blocks

A block groups related steps under a name, such as **Prepare shipment**, without creating a separate activity definition. It is an ordinary Sequence, Flowchart or BPMN subprocess embedded where the steps were. The steps keep their identities, settings and access to the surrounding variables, inputs and results. The Workflow editor and the Activity Definition graph editor offer the same commands.

## Create, extract, open and ungroup

The block commands appear above the canvas of a Sequence, Flowchart or BPMN body.

- **Create Sequence block** / **Create Flowchart block** / **Create BPMN subprocess** adds an empty named block next to the selection. Name it in the dialog; you can fill it later.
- **Extract block…** moves the selected steps into a new block. In a Sequence, select a contiguous range; in a Flowchart, a region with one entry and one normal exit; in BPMN, eligible tasks in the same lane. Studio previews the complete change and validates the whole resource before anything changes.
- **Open *name* (*n* steps)** enters the block. The breadcrumb shows where you are and returns you to the surrounding flow.
- **Ungroup…** puts a block's steps back in the surrounding flow.

Extraction keeps what the steps did:

- A result that a later step reads from inside the block reaches it through a generated result-transfer step and variable, listed in the preview.
- **Break** still ends its nearest loop, and **Complete** still ends its nearest reusable activity.
- In BPMN, the block is a real subprocess. Element and flow identities, supported task bindings and diagram layout survive saving and BPMN export and import.

When a selection cannot be grouped safely, the preview names the reason and its location, and nothing changes. Examples: a gap in a Sequence range, a Flowchart region with several exits, or a gateway, boundary event or event-defined start in BPMN.

Each command is one history step: a single Undo reverses the whole extraction, and Redo reapplies it. Renaming a block or a step inside it is a separate step. Published versions, captured run snapshots and other read-only documents can be opened and inspected, but the commands are disabled there.

## Runs

A block runs as its native container. Waiting inside a block survives a host restart and resumes once. Cancelling a run cancels the waiting steps, and a late event cannot continue it. Run inspection shows the block and each occurrence of its steps, and older versions keep the names they were published with.

## Promote a block to a reusable activity

**Promote to reusable activity…** turns a selected block into an ordinary reusable activity and replaces the block with a call to it. Both editors offer it when the server supports reusable graph activities.

1. **Review.** Studio proposes the reusable activity: its public inputs (and where each value comes from today), outputs and outcomes, the step count, and the call that will replace the block. Nothing changes yet. If the block depends on something a reusable activity cannot capture safely, the review names the reason and where it is. Examples: a script that reads workflow state outside its parameters, a write to a variable outside the block, or a Break or Complete whose target would change.
2. **Create.** **Create reusable activity draft** creates an ordinary draft in Activity Definitions. If Studio cannot confirm whether the draft was created, it says so and does not create it again; check Activity Definitions before retrying.
3. **Publish.** The usual publication review runs: preflight, version and publish. The block is still unchanged.
4. **Replace.** **Replace block with this version** checks again before changing anything. The block must still match the reviewed proposal, and the published version must be active, in the activity catalog, and contain exactly the reviewed implementation and public interface. The whole resource must also pass its normal validation. The block is then replaced by a call to that exact version in one edit.

To finish later, open the dialog again and choose **Use a published version**, then pick the version you published. The same checks apply.

Undo restores the block in one step and never deletes the reusable activity, its draft or its versions. Closing the dialog before replacing keeps whatever was already created or published.

The Activity Definition editor refuses a promotion that would change that activity's own public contract; change the contract in the definition editor first.

## Related

- [Activity definition authoring and composition](reference/activity-definition-authoring-composition.md): reusable activity contracts, graph bodies and their diagnostics.
- [Spec 1204](../specs/1204-named-embedded-blocks/spec.md): the program's requirements and decisions.
