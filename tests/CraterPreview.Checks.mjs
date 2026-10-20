// Offline checks of the production preview helper; no browser, API or provider calls.
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

const source = await readFile(new URL("../FlyerMonkey/FlyerMonkey.Shared/wwwroot/craterTiming.js", import.meta.url), "utf8");
const { afterPaint, reveal } = await import(`data:text/javascript;base64,${Buffer.from(source).toString("base64")}`);
const frames = [];
globalThis.requestAnimationFrame = callback => frames.push(callback);
let reducedMotion = false;
globalThis.matchMedia = () => ({ matches: reducedMotion });
const calls = [];
const image = {
    isConnected: true, complete: true, naturalWidth: 1024,
    getAttribute: () => "saved-jpeg",
    scrollIntoView: options => calls.push(options)
};
let decoded;
image.decode = () => new Promise(resolve => { decoded = resolve; });
const painted = afterPaint(image);
assert.equal(frames.length, 0, "No paint wait before decode completes");
decoded();
await Promise.resolve();
assert.equal(frames.length, 1);
frames.shift()();
assert.equal(frames.length, 1, "Wait for the second animation frame");
frames.shift()();
await painted;
reveal(image, "saved-jpeg");
assert.deepEqual(calls.pop(), { behavior: "smooth", block: "center", inline: "nearest" });

for (const state of [
    { isConnected: false }, { complete: false }, { naturalWidth: 0 },
    { getAttribute: () => "another-jpeg" }
]) {
    reveal({ ...image, ...state }, "saved-jpeg");
    assert.equal(calls.length, 0, "Detached, incomplete, failed or replaced images cannot scroll");
}
reducedMotion = true;
reveal(image, "saved-jpeg");
assert.equal(calls.pop().behavior, "instant");
await assert.rejects(afterPaint({ decode: async () => { throw new Error("Decode failed"); } }));
assert.equal(calls.length, 0, "Decode failure cannot scroll");
console.log("PASS: decode/paint ordering, smooth centered reveal, stale-image guards, reduced motion and decode failure. Offline only.");
