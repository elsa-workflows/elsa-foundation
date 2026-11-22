import React from "react";
import { Package } from "lucide-react";
import { normalizeType } from "./featureModel";

export function FeatureBadge({ tone = "neutral", title, children }: { tone?: string; title?: string; children: React.ReactNode }) {
  return <span className={`feature-management-badge tone-${tone}`} title={title}>{children}</span>;
}

export function SourceBadge({ sourceKind }: { sourceKind: string }) {
  const source = getSourceMeta(sourceKind);
  return <FeatureBadge tone={source.tone} title={source.title}>{source.label}</FeatureBadge>;
}

export function CategoryChips({ categories, max }: { categories: string[]; max?: number }) {
  if (categories.length === 0) {
    return <span className="feature-management-muted">Uncategorized</span>;
  }

  const shown = max ? categories.slice(0, max) : categories;
  const overflow = max ? categories.length - shown.length : 0;

  return (
    <span className="feature-management-chips">
      {shown.map(category => <span key={category} className="feature-management-chip">{category}</span>)}
      {overflow > 0 ? <span className="feature-management-chip feature-management-chip-overflow" title={categories.slice(shown.length).join(", ")}>+{overflow}</span> : null}
    </span>
  );
}

function getSourceMeta(sourceKind: string): { label: string; tone: string; title: string } {
  switch (normalizeType(sourceKind)) {
    case "shell":
      return { label: "Built-in", tone: "neutral", title: "Compiled into the host shell." };
    case "runtime":
      return { label: "Runtime", tone: "info", title: "Discovered at runtime from loaded assemblies." };
    case "manifest":
      return { label: "Package", tone: "violet", title: "Provided by an installed package manifest." };
    case "manifest-error":
      return { label: "Package error", tone: "danger", title: "The package manifest could not be read." };
    default:
      return { label: sourceKind || "Unknown", tone: "neutral", title: sourceKind };
  }
}
