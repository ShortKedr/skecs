import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import { chromium } from "../tools/web/node_modules/playwright/index.mjs";

const option = (name, fallback) => {
  const at = process.argv.indexOf(name);
  return at < 0 ? fallback : process.argv[at + 1];
};
const url = option("--url", "http://localhost:8000/");
const ticks = Number(option("--ticks", "120"));
const requiredMode = option("--require-mode", null);
assert.ok(Number.isInteger(ticks) && ticks > 0 && ticks <= 3600);
const output = path.resolve(option("--output", "artifacts/tooling/tmp/web-framework-checks"));
await fs.mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true });
const errors = [], results = [];
try {
  const page = await browser.newPage({ viewport: { width: 1440, height: 900 } });
  page.on("pageerror", error => errors.push(String(error)));
  page.on("console", message => { if (message.type() === "error") errors.push(message.text()); });
  const target = new URL(url);
  target.searchParams.set("test", "1");
  await page.goto(target.href);
  await page.waitForFunction(() => window.spaceBattle?.ready, undefined, { timeout: 180000 });
  const frameworks = ["skecs", "lite", "classic", "morpeh", "static"];
  for (const profile of ["low", "nominal", "high"]) {
    let reference;
    await page.selectOption("#profile", profile);
    for (const framework of frameworks) {
      await page.selectOption("#framework", framework);
      const status = await page.evaluate(() => ({ ready: window.spaceBattle.ready, error: JSON.parse(window.render_game_to_text()).diagnostics.error }));
      assert.equal(status.ready, true, `${profile}/${framework}: ${JSON.stringify(status.error)}`);
      console.log(`Checking ${framework}/${profile} through tick ${ticks}`);
      const replay = await page.evaluate(async ticks => {
        const hashes = [JSON.parse(window.render_game_to_text()).checksum];
        for (let i = 0; i < ticks; i++) {
          try { await window.advanceTime(1000 / 60); }
          catch (error) { throw new Error(`Tick ${i + 1}: ${error.stack || error}`); }
          hashes.push(JSON.parse(window.render_game_to_text()).checksum);
        }
        window.spaceBattle.showColliders(true);
        const colliders = window.spaceBattle.colliders();
        const state = JSON.parse(window.render_game_to_text());
        return { hashes, state, colliderCount: colliders.rows.length, validated: window.spaceBattle.validate() };
      }, ticks);
      if (requiredMode) assert.equal(replay.state.runtimeMode, requiredMode);
      assert.equal(replay.state.framework, framework);
      assert.equal(replay.state.profile, profile);
      assert.equal(replay.state.tick, ticks);
      assert.equal(replay.validated, replay.hashes.at(-1));
      assert.ok(replay.state.renderCounts.ships > 0);
      assert.ok(replay.colliderCount > 0);
      if (!reference) reference = replay.hashes;
      replay.hashes.forEach((hash, tick) => assert.equal(hash, reference[tick], `${profile}/${framework}, tick ${tick}`));
      if (profile === "nominal") await page.screenshot({ path: path.join(output, `${framework}.png`) });
      results.push({ framework, profile, ticks, runtimeMode: replay.state.runtimeMode, checksum: replay.hashes.at(-1), colliderCount: replay.colliderCount });
      console.log(`Verified ${framework}/${profile}: ticks 0..${ticks}, ${replay.hashes.at(-1)}`);
    }
  }
  // Return to SKECS after all other cores have run, exercising release/recreation.
  await page.selectOption("#framework", "skecs");
  assert.equal(await page.evaluate(() => JSON.parse(window.render_game_to_text()).tick), 0);
  await page.setViewportSize({ width: 375, height: 812 });
  const controls = await page.locator(".controls").boundingBox();
  for (const id of ["framework", "profile", "seed"]) {
    const box = await page.locator(`#${id}`).boundingBox();
    assert.ok(box.width >= 60 && box.x >= 0 && box.x + box.width <= 375, `${id} fits mobile viewport`);
    assert.ok(box.y >= controls.y && box.y + box.height <= controls.y + controls.height, `${id} remains in toolbar`);
  }
  await page.screenshot({ path: path.join(output, "mobile.png") });
  for (const [requested, expected] of Object.entries({ lite: "lite", classic: "classic", morpeh: "morpeh", static: "static", leoecslite: "lite", leoecs: "classic", staticecs: "static" })) {
    const selectedUrl = new URL(target);
    selectedUrl.searchParams.set("framework", requested);
    await page.goto(selectedUrl.href);
    await page.waitForFunction(() => window.spaceBattle?.ready, undefined, { timeout: 180000 });
    assert.equal(await page.evaluate(() => JSON.parse(window.render_game_to_text()).framework), expected);
  }
  assert.deepEqual(errors, []);
  await fs.writeFile(path.join(output, "results.json"), JSON.stringify({ results, errors }, null, 2));
  console.log(`PASS: ${results.length} framework/profile runs, every tick 0..${ticks} matched SKECS; desktop/mobile controls verified.`);
} finally {
  await browser.close();
}
