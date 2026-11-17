import assert from "node:assert/strict";
import { writeFile } from "node:fs/promises";
import { parseArgs } from "node:util";
import { chromium } from "playwright";

const { values } = parseArgs({
  options: {
    url: { type: "string" },
    route: { type: "string" },
    channel: { type: "string" },
    ticks: { type: "string", default: "1200" },
    output: { type: "string" },
  },
});
assert.ok(values.url && ["cpu", "webgpu"].includes(values.route),
  "Usage: node tools/web/check-ga-parity.mjs --url <demo URL> --route <cpu|webgpu> [--channel msedge] [--ticks 1200] [--output replay.json]");
const ticks = Number(values.ticks);
assert.ok(Number.isSafeInteger(ticks) && ticks > 0, "--ticks must be a positive integer");

const url = new URL(values.url);
url.searchParams.set("test", "1");
url.searchParams.set("framework", "skecs-ga");
const frameworks = ["skecs", "lite", "classic", "morpeh", "static", "skecs-ga"];
const profiles = ["low", "nominal", "high"];
const runs = [];
const args = values.route === "webgpu" ? ["--enable-unsafe-webgpu", "--disable-software-rasterizer"] : [];
const browser = await chromium.launch({ headless: true, channel: values.channel, args });
try {
  const page = await browser.newPage();
  const pageErrors = [];
  page.on("pageerror", error => pageErrors.push(String(error)));
  if (values.route === "cpu") {
    await page.addInitScript(() => Object.defineProperty(navigator, "gpu", { value: undefined }));
  }

  await page.goto(url.href);
  await page.waitForFunction(() => window.spaceBattle?.ready, undefined, { timeout: 180000 });
  assert.equal(await page.locator("#framework").inputValue(), "skecs-ga", "GA demo deep link");

  for (const profile of profiles) {
    await page.selectOption("#profile", profile);
    let reference;
    for (const framework of frameworks) {
      const label = `${framework}/${profile}`;
      await page.selectOption("#framework", framework);
      const gaRoute = framework === "skecs-ga" ? values.route : "off";
      await page.waitForFunction(({ framework, profile }) => {
        const state = JSON.parse(window.render_game_to_text());
        if (state.diagnostics.error) throw new Error(JSON.stringify(state.diagnostics.error));
        return state.ready && state.framework === framework && state.profile === profile
          && state.tick === 0;
      }, { framework, profile }, { timeout: 30000 });

      const replay = await page.evaluate(async ({ ticks, gaRoute }) => {
        const state = () => JSON.parse(window.render_game_to_text());
        const initial = state();
        const hashes = [initial.checksum];
        for (let tick = 1; tick <= ticks; tick++) {
          await window.advanceTime(1000 / 60);
          const frame = state();
          if (frame.diagnostics.error) throw new Error(JSON.stringify(frame.diagnostics.error));
          if (frame.tick !== tick) throw new Error(`Expected tick ${tick}, observed ${frame.tick}`);
          if (frame.ga.status !== gaRoute) {
            throw new Error(`Tick ${tick}: expected executor ${gaRoute}, observed ${frame.ga.status}`);
          }
          hashes.push(frame.checksum);
        }
        const final = state();
        return { seed: initial.seed, hashes, ga: final.ga, runtimeMode: final.runtimeMode };
      }, { ticks, gaRoute });

      assert.equal(replay.seed, 7331, `${label} seed`);
      assert.equal(replay.ga.status, gaRoute, `${label} executor`);
      if (gaRoute === "webgpu") assert.ok(replay.ga.deviceRows > 0, `${label} applied GPU rows`);
      else assert.equal(replay.ga.deviceRows, 0, `${label} applied GPU rows`);
      reference ??= replay.hashes;
      for (const [tick, hash] of replay.hashes.entries()) {
        assert.match(hash, /^[0-9a-f]{16}$/i, `${label} tick ${tick} checksum`);
        assert.equal(hash, reference[tick], `${label} tick ${tick}`);
      }
      runs.push({ framework, profile, ...replay });
      console.log(`PASS ${label}: ticks 0..${ticks}, executor ${replay.ga.status}, ${replay.ga.deviceRows} GPU rows`);
    }
  }
  assert.deepEqual(pageErrors, []);
  if (values.output) {
    await writeFile(values.output, JSON.stringify({ url: url.href, route: values.route, ticks, runs }, null, 2) + "\n");
  }
} finally {
  await browser.close();
}
