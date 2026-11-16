import assert from "node:assert/strict";
import fs from "node:fs/promises";
import path from "node:path";
import http from "node:http";
import { chromium } from "../tools/web/node_modules/playwright/index.mjs";

const option = (name, fallback) => {
  const at = process.argv.indexOf(name);
  return at < 0 ? fallback : process.argv[at + 1];
};
const site = option("--site", null);
const base = option("--base", "/");
const remote = option("--url", site ? null : "http://localhost:8000/");
assert.match(base, /^\/([a-zA-Z0-9_-]+\/)*$/);
const ticks = Number(option("--ticks", "120"));
const requiredMode = option("--require-mode", null);
const requireGpu = process.argv.includes("--require-gpu");
const disableGpu = process.argv.includes("--disable-gpu");
const checkGa = process.argv.includes("--ga") || requireGpu || disableGpu;
const channel = option("--channel", undefined);
assert.ok(!(requireGpu && disableGpu), "Choose either --require-gpu or --disable-gpu");
assert.ok(Number.isInteger(ticks) && ticks > 0 && ticks <= 3600);
const output = path.resolve(option("--output", "artifacts/tooling/tmp/web-framework-checks"));
await fs.mkdir(output, { recursive: true });
let browser, context, server, gpu;
const errors = [], results = [];

async function withGaStartupPending(page, target, check) {
  const live = new URL(target);
  live.searchParams.delete("test");
  live.searchParams.set("framework", "skecs-ga");
  const module = /\/ga-webgpu\.js(?:\?|$)/;
  let release;
  const gate = new Promise(resolve => { release = resolve; });
  const hold = async route => { await gate; await route.continue(); };
  await page.route(module, hold);
  try {
    await Promise.all([page.waitForRequest(module, { timeout: 180000 }), page.goto(live.href)]);
    const loading = await page.evaluate(() => JSON.parse(window.render_game_to_text()));
    assert.equal(loading.ga.status, "initializing");
    assert.equal(loading.tick, 0);
    await check(release);
  } finally {
    release();
    await page.unroute(module, hold);
  }
}

async function requireFrozen(page) {
  const state = await page.evaluate(async () => {
    for (let frame = 0; frame < 3; frame++) await new Promise(requestAnimationFrame);
    return JSON.parse(window.render_game_to_text());
  });
  assert.equal(state.tick, 0);
}

try {
  if (!remote) {
    const root = path.resolve(site);
    const mime = { ".html": "text/html", ".js": "text/javascript", ".css": "text/css", ".json": "application/json", ".wasm": "application/wasm", ".png": "image/png" };
    server = http.createServer(async (request, response) => {
      try {
        const pathname = decodeURIComponent(new URL(request.url, "http://local").pathname);
        if (!pathname.startsWith(base)) { response.writeHead(404).end(); return; }
        const file = path.resolve(root, pathname.slice(base.length) || "index.html");
        if (!file.startsWith(root + path.sep)) { response.writeHead(403).end(); return; }
        const bytes = await fs.readFile(file);
        response.writeHead(200, { "Content-Type": mime[path.extname(file)] || "application/octet-stream" }).end(bytes);
      } catch { response.writeHead(404).end(); }
    });
    await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
  }
  const target = new URL(remote || `http://127.0.0.1:${server.address().port}${base}`);
  const viewport = { width: 1440, height: 900 };
  let page;
  if (checkGa && !remote) {
    const scratch = await fs.mkdtemp(path.join(output, "browser-"));
    const env = { ...process.env, APPDATA: scratch, LOCALAPPDATA: scratch, TEMP: scratch, TMP: scratch };
    context = await chromium.launchPersistentContext(path.join(scratch, "profile"), {
      headless: true, channel, viewport, env, chromiumSandbox: requireGpu || process.platform === "win32",
      ignoreDefaultArgs: ["--enable-unsafe-swiftshader"],
      proxy: { server: "http://127.0.0.1:9", bypass: "127.0.0.1" },
      args: ["--host-resolver-rules=MAP * ~NOTFOUND, EXCLUDE 127.0.0.1"],
    });
    await context.route("**/*", route => route.request().url().startsWith(target.origin + "/") ? route.continue() : route.abort());
    page = context.pages()[0] ?? await context.newPage();
  } else {
    browser = await chromium.launch({ headless: true, channel });
    page = await browser.newPage({ viewport });
  }
  if (disableGpu) await page.addInitScript(() => Object.defineProperty(navigator, "gpu", { value: undefined }));
  page.on("pageerror", error => errors.push(String(error)));
  page.on("console", message => { if (message.type() === "error") errors.push(message.text()); });
  target.searchParams.set("test", "1");
  if (checkGa) target.searchParams.set("lang", "en");
  await page.goto(target.href);
  await page.waitForFunction(() => window.spaceBattle?.ready, undefined, { timeout: 180000 });
  const frameworks = ["skecs", "lite", "classic", "morpeh", "static"];
  if (checkGa) frameworks.push("skecs-ga");
  let gaDeviceRows = 0;
  for (const profile of ["low", "nominal", "high"]) {
    let reference;
    await page.selectOption("#profile", profile);
    for (const framework of frameworks) {
      await page.selectOption("#framework", framework);
      const status = await page.evaluate(() => ({ ready: window.spaceBattle.ready, error: JSON.parse(window.render_game_to_text()).diagnostics.error }));
      assert.equal(status.ready, true, `${profile}/${framework}: ${JSON.stringify(status.error)}`);
      console.log(`Checking ${framework}/${profile} through tick ${ticks}`);
      const replay = await page.evaluate(async ({ ticks, checkGa }) => {
        const initial = JSON.parse(window.render_game_to_text());
        const hashes = [initial.checksum];
        const gaStates = new Set();
        for (let i = 0; i < ticks; i++) {
          try { await window.advanceTime(1000 / 60); }
          catch (error) { throw new Error(`Tick ${i + 1}: ${error.stack || error}`); }
          const step = JSON.parse(window.render_game_to_text());
          if (step.tick !== i + 1) throw new Error(`Expected tick ${i + 1}, received ${step.tick}`);
          hashes.push(step.checksum);
          if (checkGa) gaStates.add(step.ga.status);
        }
        window.spaceBattle.showColliders(true);
        const colliders = window.spaceBattle.colliders();
        const state = JSON.parse(window.render_game_to_text());
        return { hashes, state, initial, gaStates: [...gaStates], colliderCount: colliders.rows.length, validated: window.spaceBattle.validate() };
      }, { ticks, checkGa });
      if (requiredMode) assert.equal(replay.state.runtimeMode, requiredMode);
      assert.equal(replay.state.framework, framework);
      assert.equal(replay.state.profile, profile);
      assert.equal(replay.state.tick, ticks);
      assert.equal(replay.initial.tick, 0);
      assert.equal(replay.state.seed, replay.initial.seed);
      assert.equal(replay.validated, replay.hashes.at(-1));
      assert.ok(replay.state.renderCounts.ships > 0);
      assert.ok(replay.colliderCount > 0);
      if (!reference) reference = { hashes: replay.hashes, seed: replay.initial.seed };
      assert.equal(replay.initial.seed, reference.seed, `${profile}/${framework} must start with the reference seed`);
      assert.equal(replay.initial.profile, profile);
      replay.hashes.forEach((hash, tick) => assert.equal(hash, reference.hashes[tick], `${profile}/${framework}, tick ${tick}`));
      if (checkGa) {
        const gaLabel = page.locator("#ga-status");
        assert.deepEqual(replay.initial.ga, { status: "off", deviceRows: 0 });
        if (framework === "skecs-ga") {
          assert.ok(replay.gaStates.every(status => status === "webgpu" || status === "cpu"), replay.gaStates.join(", "));
          if (requireGpu) assert.deepEqual(replay.gaStates, ["webgpu"]);
          if (disableGpu) {
            assert.deepEqual(replay.gaStates, ["cpu"]);
            assert.equal(replay.state.ga.deviceRows, 0);
          }
          gaDeviceRows += replay.state.ga.deviceRows;
          assert.ok(await gaLabel.isVisible());
          assert.equal(await gaLabel.getAttribute("data-status"), replay.state.ga.status);
          assert.match(await gaLabel.textContent(), /Particle expiry.*(?:WebGPU|CPU)/);
        } else {
          assert.deepEqual(replay.gaStates, ["off"]);
          assert.deepEqual(replay.state.ga, { status: "off", deviceRows: 0 });
          assert.ok(await gaLabel.isHidden());
        }
      }
      if (profile === "nominal") await page.screenshot({ path: path.join(output, `${framework}.png`) });
      results.push({ framework, profile, ticks, runtimeMode: replay.state.runtimeMode, checksum: replay.hashes.at(-1), colliderCount: replay.colliderCount,
        ...(checkGa ? { seed: replay.initial.seed, hashes: replay.hashes, ga: replay.state.ga } : {}) });
      console.log(`Verified ${framework}/${profile}: ticks 0..${ticks}, ${replay.hashes.at(-1)}`);
    }
  }
  if (requireGpu) assert.ok(gaDeviceRows > 0, "GA must apply real WebGPU results during the parity corpus");
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
  const queryFrameworks = { lite: "lite", classic: "classic", morpeh: "morpeh", static: "static", leoecslite: "lite", leoecs: "classic", staticecs: "static" };
  if (checkGa) queryFrameworks["skecs-ga"] = "skecs-ga";
  for (const [requested, expected] of Object.entries(queryFrameworks)) {
    const selectedUrl = new URL(target);
    selectedUrl.searchParams.set("framework", requested);
    await page.goto(selectedUrl.href);
    await page.waitForFunction(() => window.spaceBattle?.ready, undefined, { timeout: 180000 });
    assert.equal(await page.evaluate(() => JSON.parse(window.render_game_to_text()).framework), expected);
  }
  if (checkGa) {
    const localized = new URL(target);
    localized.searchParams.set("framework", "skecs-ga");
    localized.searchParams.set("lang", "ru");
    await page.goto(localized.href);
    await page.waitForFunction(() => window.spaceBattle?.ready, undefined, { timeout: 180000 });
    await page.evaluate(() => window.advanceTime(1000 / 60));
    assert.match(await page.locator("#ga-status").textContent(), /Срок жизни частиц.*(?:WebGPU|CPU)/);
  }
  if (requireGpu) {
    await withGaStartupPending(page, target, async release => {
      await page.click("#pause");
      release();
      await page.waitForFunction(() => document.querySelector("#ga-status").dataset.status === "webgpu");
      await requireFrozen(page);
      await page.evaluate(() => {
        Object.defineProperty(document, "hidden", { value: true, configurable: true });
        document.dispatchEvent(new Event("visibilitychange"));
      });
      await page.click("#pause");
      await requireFrozen(page);
      await page.evaluate(() => {
        delete document.hidden;
        document.dispatchEvent(new Event("visibilitychange"));
      });
      await page.waitForFunction(() => window.spaceBattle.snapshot.tick > 0);
    });
    await withGaStartupPending(page, target, async release => {
      await page.click("#pause");
      await page.selectOption("#framework", "lite");
      const pending = await page.evaluate(() => JSON.parse(window.render_game_to_text()));
      assert.equal(pending.framework, "skecs-ga");
      assert.equal(pending.tick, 0);
      release();
      await page.waitForFunction(() => window.spaceBattle.snapshot.framework === "lite");
      const reset = await page.evaluate(() => JSON.parse(window.render_game_to_text()));
      assert.equal(reset.tick, 0);
      assert.deepEqual(reset.ga, { status: "off", deviceRows: 0 });
    });
    const session = await (context?.browser() ?? browser).newBrowserCDPSession();
    try { ({ gpu } = await session.send("SystemInfo.getInfo")); }
    finally { await session.detach(); }
  }
  assert.deepEqual(errors, []);
  await fs.writeFile(path.join(output, "results.json"), JSON.stringify({ results, errors, gpu }, null, 2));
  console.log(`PASS: ${results.length} framework/profile runs, every tick 0..${ticks} matched SKECS; desktop/mobile controls verified.`);
} finally {
  await context?.close();
  await browser?.close();
  if (server) await new Promise(resolve => server.close(resolve));
}
