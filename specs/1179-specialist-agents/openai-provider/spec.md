# Direct OpenAI specialist model connections

**Status**: Implemented — takes effect when the implementing PR merges
**Issue**: #3773, part of #3519
**Authority**: Sipke authorized adding direct OpenAI support and finishing a real Studio Agent example on 2026-11-03.

A user with an ordinary OpenAI project API key can create a specialist model connection, choose it in Agents, publish a typed specialist and execute it through the normal workflow runtime. Azure OpenAI remains an independent provider.

Acceptance: register `openai` / `specialist-openai`; reuse existing Connections/Secrets and specialist capability/client ports; send to `/v1/chat/completions` using Bearer authentication with the exact configured model; preserve existing scope/revision/host/schema/request-budget gates and disable SDK retries. Refusal, truncation, cancellation and streaming retain the existing acceptance rules. Qualify real execution separately from deterministic transport tests. Weaver is unchanged.

Review: this is an additive vendor adapter using the approved spec 1179 D3 boundary; it introduces no runtime/artifact contract or new architecture decision. A separate package earns its boundary by owning the external OpenAI SDK integration independently of Azure.
