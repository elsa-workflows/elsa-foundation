import React from "react";

/**
 * Inline sparkline. Maps `points` into a normalized polyline over a fixed viewBox and
 * strokes it with `--chart-1`; the path scales to its container via `preserveAspectRatio`.
 * A single flat point (or fewer than two) renders a centred baseline. Purely decorative —
 * the parent `StatTile` carries the accessible value/label.
 */
export function StudioSparkline({
  points,
  className,
  width = 96,
  height = 28
}: {
  points: number[];
  className?: string;
  width?: number;
  height?: number;
}) {
  const path = React.useMemo(() => {
    if (points.length < 2) return `M0 ${height / 2} L${width} ${height / 2}`;
    const min = Math.min(...points);
    const max = Math.max(...points);
    const span = max - min || 1;
    const step = width / (points.length - 1);
    return points
      .map((value, index) => {
        const x = index * step;
        // Invert Y so higher values sit toward the top; inset 2px so the stroke isn't clipped.
        const y = height - 2 - ((value - min) / span) * (height - 4);
        return `${index === 0 ? "M" : "L"}${x.toFixed(2)} ${y.toFixed(2)}`;
      })
      .join(" ");
  }, [points, width, height]);

  return (
    <svg
      className={className ? `studio-sparkline ${className}` : "studio-sparkline"}
      viewBox={`0 0 ${width} ${height}`}
      preserveAspectRatio="none"
      aria-hidden="true"
      focusable="false"
    >
      <path d={path} fill="none" stroke="var(--chart-1)" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

/**
 * KPI / stat slab: a label, a large value, an optional signed delta (tinted success for a
 * rise, danger for a drop), and an optional inline sparkline. The container uses the card
 * surface so material themes render it as a raised slab automatically — no per-tile
 * material override needed.
 */
export function StudioStatTile({
  label,
  value,
  delta,
  deltaTone,
  trend,
  className
}: {
  label: React.ReactNode;
  value: React.ReactNode;
  delta?: React.ReactNode;
  deltaTone?: "success" | "danger" | "neutral";
  trend?: number[];
  className?: string;
}) {
  return (
    <div className={className ? `studio-stat-tile ${className}` : "studio-stat-tile"}>
      <span className="studio-stat-tile-label">{label}</span>
      <span className="studio-stat-tile-value">{value}</span>
      {delta !== undefined ? (
        <span className="studio-stat-tile-delta" data-tone={deltaTone ?? "neutral"}>{delta}</span>
      ) : null}
      {trend && trend.length > 0 ? <StudioSparkline points={trend} className="studio-stat-tile-trend" /> : null}
    </div>
  );
}
