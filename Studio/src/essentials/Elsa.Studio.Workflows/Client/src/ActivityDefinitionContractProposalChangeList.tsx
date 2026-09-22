import { Check, X } from "lucide-react";
import {
  contractMemberFacts,
  safeProposalChangeId,
  type ActivityContractProposalChangeReview
} from "./activityContractProposalModel";

export function ActivityDefinitionContractProposalChangeList({
  reviews,
  decisions,
  onDecision
}: {
  reviews: ActivityContractProposalChangeReview[];
  decisions: Record<string, boolean>;
  onDecision(changeId: string, selected: boolean): void;
}) {
  return <div className="ad-publication-impact" role="list" aria-label="Provider-inferred contract changes">
    {reviews.map((review, index) => <ProposalChange
      key={safeProposalChangeId(review.changeId, index)}
      review={review}
      selected={Boolean(decisions[review.changeId])}
      onDecision={selected => onDecision(review.changeId, selected)}
    />)}
  </div>;
}

function ProposalChange({
  review,
  selected,
  onDecision
}: {
  review: ActivityContractProposalChangeReview;
  selected: boolean;
  onDecision(selected: boolean): void;
}) {
  const title = review.supported
    ? `${review.operation} ${review.memberKind} ${review.referenceKey}`
    : "Unsupported provider proposal change";
  const impactClass = review.impact === "Compatible" ? "nonbehavioral" :
    review.impact === "Unknown" ? "warning" :
      review.impact.toLocaleLowerCase("en-US");
  return <article className={`ad-impact-group is-${impactClass}`} role="listitem">
    <header>
      <div><h4>{title}</h4><h5>{review.impact} compatibility impact</h5></div>
      <span className={`ad-badge ${selected ? "is-design" : ""}`}>{selected ? "Selected" : "Not selected"}</span>
    </header>
    <p>{review.impactReasons.join(" ")}</p>
    {review.supported ? <dl className="ad-impact-facts" aria-label={`${title} structured comparison`}>
      <ContractFacts title="Current" facts={contractMemberFacts(review.current)} />
      <ContractFacts title="Proposed" facts={contractMemberFacts(review.proposed)} />
    </dl> : <div className="ad-unavailable-note"><strong>Unknown structured kind</strong><span>The opaque provider payload is intentionally not rendered. This change cannot be selected.</span></div>}
    <div className="ad-publication-actions" role="group" aria-label={`Review ${title}`}>
      <button type="button" aria-pressed={selected} onClick={() => onDecision(true)} disabled={!review.supported}><Check size={15} aria-hidden /> Accept</button>
      <button type="button" aria-pressed={!selected} onClick={() => onDecision(false)}><X size={15} aria-hidden /> Reject</button>
    </div>
  </article>;
}

function ContractFacts({ title, facts }: { title: string; facts: string[] }) {
  return <div><dt>{title}</dt>{facts.map(fact => <dd key={fact}>{fact}</dd>)}</div>;
}
