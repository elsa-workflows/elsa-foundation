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
export function choosePortSide(rect: PortNodeRect, peers: readonly PortNodeRect[], fallback: Position, previous?: Position): Position {
  if (peers.length === 0) return fallback;
  const score = (side: Position) => {
    const x = rect.x + (side === Position.Left ? 0 : side === Position.Right ? rect.width : rect.width / 2);
    const y = rect.y + (side === Position.Top ? 0 : side === Position.Bottom ? rect.height : rect.height / 2);
    return peers.reduce((sum, peer) => sum + Math.hypot(peer.x + peer.width / 2 - x, peer.y + peer.height / 2 - y), 0) / peers.length;
  };
  let best = fallback;
  for (const side of sides) {
    if (score(side) < score(best)) best = side;
  }
  return previous && score(previous) - score(best) < sideChangeMargin ? previous : best;
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
