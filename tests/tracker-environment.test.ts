import {expect, test} from "bun:test";
import {trackerEnvironment} from "../src/runtime/tracker-environment";

test("the auxiliary tracker excludes both Steam overlay architectures and preserves other injected tools", () => {
  const game = {WINEPREFIX:"/game/prefix", LD_PRELOAD:"/steam/steamrt32/gameoverlayrenderer.so:/tools/libMangoHud.so /steam/steamrt64/gameoverlayrenderer.so\t/tools/custom.so", EXTRA:"unchanged"};
  const original = {...game};
  expect(trackerEnvironment(game)).toEqual({...game, LD_PRELOAD:"/tools/libMangoHud.so:/tools/custom.so"});
  expect(game).toEqual(original);
});

test("removing the only preload leaves the loader variable absent", () => {
  expect(trackerEnvironment({LD_PRELOAD:"gameoverlayrenderer.so"})).toEqual({});
  expect(trackerEnvironment({WINEPREFIX:"/prefix"})).toEqual({WINEPREFIX:"/prefix"});
});

test("similarly named libraries survive the exact Steam overlay filter", () => {
  expect(trackerEnvironment({LD_PRELOAD:"/tools/gameoverlayrenderer.so.backup:/tools/mygameoverlayrenderer.so"}).LD_PRELOAD)
    .toBe("/tools/gameoverlayrenderer.so.backup:/tools/mygameoverlayrenderer.so");
});
