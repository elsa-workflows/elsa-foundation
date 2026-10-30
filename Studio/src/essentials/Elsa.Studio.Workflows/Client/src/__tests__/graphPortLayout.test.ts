import { describe, expect, it } from "vitest";
import { Position } from "@xyflow/react";
import { choosePortSide, layoutGraphPorts, portPlacementStyle, type PortNodeRect } from "../graph-authoring/graphPortLayout";

const rect = { x: 100, y: 100, width: 240, height: 64 };
const peer = (x: number, y: number): PortNodeRect => ({ ...rect, x, y });

describe("adaptive graph ports", () => {
  it.each([
    [100, 400, Position.Bottom], [100, -200, Position.Top],
    [500, 100, Position.Right], [-300, 100, Position.Left]
  ] as const)("faces a neighbour at %s,%s", (x, y, side) => {
    expect(choosePortSide(rect, [peer(x, y)], Position.Right)).toBe(side);
  });

  it("uses measured dimensions rather than assuming square nodes", () => {
    expect(choosePortSide(rect, [peer(180, 350)], Position.Right)).toBe(Position.Bottom);
    expect(choosePortSide({ ...rect, width: 64, height: 240 }, [{ x: 350, y: 180, width: 64, height: 240 }], Position.Bottom)).toBe(Position.Right);
  });

  it("keeps a side near a diagonal, then switches after a meaningful improvement", () => {
    const square = { x: 0, y: 0, width: 100, height: 100 };
    const nearDiagonal = [{ ...square, x: 200, y: 220 }];
    expect(choosePortSide(square, nearDiagonal, Position.Right)).toBe(Position.Bottom);
    expect(choosePortSide(square, nearDiagonal, Position.Right, Position.Right)).toBe(Position.Right);
    expect(choosePortSide(square, [{ ...square, x: 200, y: 500 }], Position.Right, Position.Right)).toBe(Position.Bottom);
  });

  it("combines fan-in and fan-out without depending on connection order", () => {
    const peers = [peer(100, 400), peer(100, 600), peer(130, 500)];
    expect(choosePortSide(rect, peers, Position.Right)).toBe(Position.Bottom);
    expect(choosePortSide(rect, [...peers].reverse(), Position.Right)).toBe(Position.Bottom);
  });

  it("restores defaults on disconnect, even after moving a port", () => {
    expect(choosePortSide(rect, [], Position.Right, Position.Bottom)).toBe(Position.Right);
    expect(choosePortSide(rect, [], Position.Left, Position.Top)).toBe(Position.Left);
  });

  it("chooses independent sides for exact outcome identities", () => {
    const layout = layoutGraphPorts({ rect, acceptsInbound: true, sourceIds: ["True", "False", "a:b/long outcome"],
      incoming: [peer(100, -200)], outgoing: new Map([["True", [peer(100, 400)]], ["False", [peer(500, 100)]]]) });
    expect(layout.target?.side).toBe(Position.Top);
    expect(layout.sources.map(port => [port.id, port.side])).toEqual([
      ["True", Position.Bottom], ["False", Position.Right], ["a:b/long outcome", Position.Right]
    ]);
  });

  it("spaces inbound and outcomes together when they share a side", () => {
    const layout = layoutGraphPorts({ rect, acceptsInbound: true, sourceIds: ["True", "False"], incoming: [peer(100, 400)],
      outgoing: new Map([["True", [peer(100, 400)]], ["False", [peer(100, 500)]]]) });
    expect([layout.target?.offset, ...layout.sources.map(port => port.offset)]).toEqual([25, 50, 75]);
    expect(layout.sources.every(port => port.side === Position.Bottom && port.labelWidth === 48)).toBe(true);
    expect(portPlacementStyle(layout.sources[0])).toEqual({ left: "50%" });
  });

  it("gives many lateral outcomes sufficient height without changing their names", () => {
    const ids = ["A", "B", "C", "D", "E"];
    const layout = layoutGraphPorts({ rect, acceptsInbound: true, sourceIds: ids, incoming: [], outgoing: new Map() });
    expect(layout.minHeight).toBe(168);
    expect(layout.sources.map(port => port.id)).toEqual(ids);
    expect(portPlacementStyle(layout.sources[0])).toEqual({ top: `${100 / 6}%` });
  });

  it("renders default handles before measurement and suppresses Sequence ports", () => {
    const layout = layoutGraphPorts({ acceptsInbound: true, sourceIds: ["Done"], incoming: [peer(100, 400)], outgoing: new Map() });
    expect(layout.target?.side).toBe(Position.Left);
    expect(layout.sources[0].side).toBe(Position.Right);
    expect(layoutGraphPorts({ rect, acceptsInbound: false, sourceIds: [], incoming: [], outgoing: new Map() })).toEqual({
      target: undefined, sources: [], minHeight: undefined
    });
  });
});
