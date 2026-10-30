import { Position } from "@xyflow/react";

export interface PortNodeRect {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface PortPlacement {
  side: Position;
  offset: number;
  labelWidth?: number;
}

export interface GraphPortLayout {
  target?: PortPlacement;
  sources: (PortPlacement & { id: string })[];
  minHeight?: number;
}

const sides = [Position.Right, Position.Bottom, Position.Left, Position.Top];
const sideChangeMargin = 16;

/** One logical handle can serve several edges; minimise their mean distance together. */
export function choosePortSide(rect: PortNodeRect, peers: readonly PortNodeRect[], fallback: Position, previous?: Position, available: readonly Position[] = sides): Position {
  let best = available.includes(fallback) ? fallback : available[0];
  if (peers.length === 0) return best;
  const score = (side: Position) => {
    const x = rect.x + (side === Position.Left ? 0 : side === Position.Right ? rect.width : rect.width / 2);
    const y = rect.y + (side === Position.Top ? 0 : side === Position.Bottom ? rect.height : rect.height / 2);
    return peers.reduce((sum, peer) => sum + Math.hypot(peer.x + peer.width / 2 - x, peer.y + peer.height / 2 - y), 0) / peers.length;
  };
  for (const side of available) {
    if (score(side) < score(best)) best = side;
  }
  return previous && available.includes(previous) && score(previous) - score(best) < sideChangeMargin ? previous : best;
}

export function layoutGraphPorts({ rect, sourceIds, acceptsInbound, incoming, outgoing, previous }: {
  rect?: PortNodeRect;
  sourceIds: readonly string[];
  acceptsInbound: boolean;
  incoming: readonly PortNodeRect[];
  outgoing: ReadonlyMap<string, readonly PortNodeRect[]>;
  previous?: GraphPortLayout;
}): GraphPortLayout {
  const target = acceptsInbound ? {
    side: rect ? choosePortSide(rect, incoming, Position.Left, previous?.target?.side) : Position.Left,
    offset: 50
  } : undefined;
  const sources = sourceIds.map(id => ({
    id,
    side: rect ? choosePortSide(rect, outgoing.get(id) ?? [], Position.Right, previous?.sources.find(port => port.id === id)?.side) : Position.Right,
    offset: 50,
    labelWidth: undefined as number | undefined
  }));
  // A wide outcome list cannot fit along the fixed card width. Keep a readable horizontal
  // slot per port; overflowing outcomes take the next closest available side in catalog order.
  const horizontalCapacity = Math.max(1, Math.floor((rect?.width ?? 246) / 48) - 1);
  const counts = new Map<Position, number>();
  const handles = [
    ...(target ? [{ port: target as PortPlacement, peers: incoming, fallback: Position.Left }] : []),
    ...sources.map(port => ({ port, peers: outgoing.get(port.id) ?? [], fallback: Position.Right }))
  ];
  for (const { port, peers, fallback } of handles) {
    const available = sides.filter(side => side === Position.Left || side === Position.Right || (counts.get(side) ?? 0) < horizontalCapacity);
    if (rect && !available.includes(port.side)) port.side = choosePortSide(rect, peers, fallback, undefined, available);
    counts.set(port.side, (counts.get(port.side) ?? 0) + 1);
  }
  // Reserve enough space for any grouping. Changing height with the chosen side would feed
  // back into side selection and could make diagonal branches oscillate between two layouts.
  const handleCount = sources.length + (target ? 1 : 0);
  const minHeight = handleCount > 2 ? (handleCount + 1) * 24 : undefined;
  for (const side of sides) {
    const ports: PortPlacement[] = [...(target?.side === side ? [target] : []), ...sources.filter(port => port.side === side)];
    ports.forEach((port, index) => {
      port.offset = (index + 1) * 100 / (ports.length + 1);
      if (side === Position.Top || side === Position.Bottom) {
        port.labelWidth = Math.max(12, (rect?.width ?? 246) / (ports.length + 1) - 12);
      }
    });
  }
  return { target, sources, minHeight };
}

export function portPlacementStyle(port: PortPlacement) {
  return port.side === Position.Top || port.side === Position.Bottom
    ? { left: `${port.offset}%` }
    : { top: `${port.offset}%` };
}
