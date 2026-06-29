import React from "react";

export function SummaryItem({ label, value }: { label: string; value: React.ReactNode }) {
  return <div className="modules-summary-item"><strong>{value}</strong><span>{label}</span></div>;
}
