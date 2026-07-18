import { useEffect, useMemo, useState } from "react";
import { RefreshCw } from "lucide-react";
import {
  type StudioActivityDiagnostic,
  type StudioActivityDiagnosticFocusResult,
  type StudioEndpointContext
} from "@elsa-workflows/studio-sdk";
import type { ActivityContractProposalView, ActivityDefinitionDraftView } from "./activityDefinitionTypes";
import {
  applyActivityDefinitionContractProposal,
  proposeActivityDefinitionContract
} from "./api/activityDesign";
import { ActivityDiagnosticList } from "./ActivityDefinitionDiagnosticsPanel";
import { ActivityDefinitionContractProposalChangeList } from "./ActivityDefinitionContractProposalChangeList";
import {
  activityContractProposalBinding,
  proposalMatchesBinding,
  proposalRequest,
  type ActivityContractProposalBinding
} from "./activityContractProposalBinding";
import { reviewProposalChange } from "./activityContractProposalModel";
import {
  ActivityDefinitionContractProposalStatus,
  type ActivityContractProposalPhase
} from "./ActivityDefinitionContractProposalStatus";
import {
  isProposalAbort,
  isStaleProposal,
  proposalApplyFailure,
  proposalFailure
} from "./activityContractProposalErrors";

const proposalDebounceMs = 900;

export function ActivityDefinitionContractProposalReview({
  context,
  draft,
  enabled,
  onApplyingChange,
  onApplied,
  onFocusDiagnostic
}: {
  context: StudioEndpointContext;
  draft: ActivityDefinitionDraftView;
  enabled: boolean;
  onApplyingChange(applying: boolean): void;
  onApplied(draft: ActivityDefinitionDraftView): void;
  onFocusDiagnostic(diagnostic: StudioActivityDiagnostic, trigger: HTMLButtonElement): Promise<StudioActivityDiagnosticFocusResult>;
}) {
  const binding = useMemo<ActivityContractProposalBinding>(() => activityContractProposalBinding(draft), [draft]);
  const [phase, setPhase] = useState<ActivityContractProposalPhase>(enabled ? "debouncing" : "waiting");
  const [proposal, setProposal] = useState<ActivityContractProposalView | null>(null);
  const [decisions, setDecisions] = useState<Record<string, boolean>>({});
  const [failure, setFailure] = useState<string | null>(null);
  const [recompute, setRecompute] = useState(0);

  useEffect(() => {
    setProposal(null);
    setDecisions({});
    setFailure(null);
    if (!enabled) {
      setPhase("waiting");
      return;
    }

    const controller = new AbortController();
    setPhase("debouncing");
    const timer = window.setTimeout(() => {
      setPhase("loading");
      void proposeActivityDefinitionContract(context, binding.draftId, proposalRequest(binding), controller.signal)
        .then(next => {
          if (!proposalMatchesBinding(next, binding)) {
            setPhase("stale");
            setFailure("The provider returned a proposal for different draft material. Studio discarded it without exposing or applying its changes.");
            return;
          }
          const reviews = next.changes.map(change => reviewProposalChange(draft.contract, change));
          setProposal(next);
          setDecisions(Object.fromEntries(reviews.map(review => [review.changeId, review.supported && !review.destructive])));
          setPhase("ready");
        })
        .catch(error => {
          if (isProposalAbort(error)) return;
          setProposal(null);
          setDecisions({});
          const result = proposalFailure(error);
          setPhase(result.phase);
          setFailure(result.message);
        });
    }, proposalDebounceMs);

    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [binding, context, draft.contract, enabled, recompute]);

  const reviews = useMemo(
    () => proposal?.changes.map(change => reviewProposalChange(draft.contract, change)) ?? [],
    [draft.contract, proposal]
  );
  const selectedChangeIds = reviews
    .filter(review => review.supported && decisions[review.changeId])
    .map(review => review.changeId);
  const hasErrors = proposal?.diagnostics.some(diagnostic => diagnostic.severity === "Error") ?? false;

  const apply = async () => {
    if (!proposal || phase !== "ready" || !proposalMatchesBinding(proposal, binding) || selectedChangeIds.length === 0 || hasErrors) return;
    setPhase("applying");
    setFailure(null);
    onApplyingChange(true);
    try {
      const saved = await applyActivityDefinitionContractProposal(context, draft.draftId, {
        ...proposalRequest(binding),
        proposalFingerprint: proposal.proposalFingerprint,
        selectedChangeIds
      });
      if (
        saved.draftId !== draft.draftId ||
        saved.definitionId !== draft.definitionId ||
        saved.revision !== draft.revision + 1 ||
        saved.provider.providerKey !== binding.providerKey ||
        saved.provider.schemaVersion !== binding.providerSchemaVersion ||
        saved.provider.manifestFingerprint !== binding.manifestFingerprint
      ) {
        setPhase("failed");
        setFailure("The server response did not identify the exact updated draft. Local content is unchanged.");
        return;
      }
      setProposal(null);
      setDecisions({});
      onApplied(saved);
    } catch (error) {
      if (isStaleProposal(error)) {
        setProposal(null);
        setDecisions({});
        setPhase("stale");
        setFailure("The proposal is stale. No local content changed; recompute it against the latest exact saved revision.");
      } else {
        setPhase("failed");
        setFailure(proposalApplyFailure(error));
      }
    } finally {
      onApplyingChange(false);
    }
  };

  const requestRecompute = () => {
    if (!enabled || phase === "loading" || phase === "applying") return;
    setRecompute(value => value + 1);
  };

  return <section className="ad-publication-review" aria-labelledby="activity-contract-proposals-title">
    <header className="ad-publication-header">
      <div>
        <span className="ad-kicker">Provider-inferred · explicit review</span>
        <h2 id="activity-contract-proposals-title">Contract proposals</h2>
        <p>Inference runs in the background for the exact saved provider revision. It never changes the draft until selected changes are applied.</p>
      </div>
      <button type="button" className="ad-primary-action" onClick={requestRecompute} disabled={!enabled || phase === "loading" || phase === "applying"}>
        <RefreshCw size={16} aria-hidden /> Recompute
      </button>
    </header>
    <ActivityDefinitionContractProposalStatus phase={phase} revision={draft.revision} failure={failure} />
    {proposal && phase !== "stale" ? <>
      <dl className="ad-publication-summary" aria-label="Exact proposal binding">
        <div><dt>Draft revision</dt><dd>{proposal.revision}</dd></div>
        <div><dt>Provider</dt><dd>{proposal.providerKey}</dd></div>
        <div><dt>Provider schema</dt><dd>{proposal.providerSchemaVersion}</dd></div>
        <div><dt>Implementation fingerprint</dt><dd><code>{proposal.manifestFingerprint}</code></dd></div>
      </dl>
      {reviews.length ? <ActivityDefinitionContractProposalChangeList
        reviews={reviews}
        decisions={decisions}
        onDecision={(changeId, selected) => setDecisions(current => ({ ...current, [changeId]: selected }))}
      /> : <p className="ad-publication-empty">The provider proposed no public-contract changes for this exact implementation.</p>}
      {proposal.diagnostics.length ? <section className="ad-publication-diagnostics" aria-labelledby="activity-proposal-diagnostics-title">
        <h3 id="activity-proposal-diagnostics-title">Proposal diagnostics</h3>
        <ActivityDiagnosticList diagnostics={proposal.diagnostics} onFocus={onFocusDiagnostic} label="Proposal diagnostics" />
      </section> : null}
      <footer className="ad-publication-actions">
        <span>{selectedChangeIds.length} of {reviews.length} changes selected. Destructive and unsupported changes start unselected.</span>
        <button type="button" className="ad-primary-action" onClick={() => void apply()} disabled={phase !== "ready" || selectedChangeIds.length === 0 || hasErrors}>
          {phase === "applying" ? "Applying exact proposal…" : "Apply selected changes"}
        </button>
      </footer>
    </> : null}
  </section>;
}
