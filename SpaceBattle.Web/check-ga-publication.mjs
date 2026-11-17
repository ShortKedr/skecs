import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import fs from "node:fs/promises";
import http from "node:http";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium } from "../tools/web/node_modules/playwright/index.mjs";

const [sitePath, outputPath, channel = "msedge"] = process.argv.slice(2);
assert.ok(sitePath && outputPath, "Usage: node SpaceBattle.Web/check-ga-publication.mjs <published wwwroot> <output directory> [msedge|chrome]");
assert.ok(["msedge", "chrome"].includes(channel), "Use installed Edge or Chrome");
const workspace = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const site = path.resolve(sitePath);
const output = path.resolve(outputPath);
assert.ok([site, output].every(directory => directory.startsWith(workspace + path.sep)), "Use directories in this workspace");
const index = await fs.readFile(path.join(site, "index.html"), "utf8");
const base = index.match(/<base href="(\/[^"]*)"\s*\/?>/)?.[1];
assert.match(base, /^\/([a-zA-Z0-9_-]+\/)*$/);
const identify = async filename => createHash("sha256").update(await fs.readFile(path.join(site, filename))).digest("hex");
const identity = JSON.parse(await fs.readFile(path.join(site, "build-identity.json"), "utf8"));
for (const filename of ["app.js", "ga-webgpu.js", "_framework/blazor.boot.json"]) {
  assert.equal(await identify(filename), identity.files[filename], `Build identity mismatch for ${filename}`);
}
assert.match(identity.runtime, /^\d+\.\d+\.\d+$/);
const mime = {
  ".html": "text/html", ".js": "text/javascript", ".mjs": "text/javascript",
  ".json": "application/json", ".css": "text/css", ".wasm": "application/wasm", ".png": "image/png",
};
await fs.mkdir(output, { recursive: true });
const scratch = await fs.mkdtemp(path.join(output, "browser-"));
const receipt = {
  site, channel, runtime: identity.runtime, appSha256: identity.files["app.js"],
  bridgeSha256: identity.files["ga-webgpu.js"], wasmSha256: await identify("_framework/dotnet.native.wasm"),
};
const server = http.createServer(async (request, response) => {
  try {
    const pathname = decodeURIComponent(new URL(request.url, "http://local").pathname);
    if (!pathname.startsWith(base)) return response.writeHead(404).end();
    const file = path.resolve(site, pathname.slice(base.length) || "index.html");
    if (!file.startsWith(site + path.sep)) return response.writeHead(403).end();
    const bytes = await fs.readFile(file);
    response.writeHead(200, { "Content-Type": mime[path.extname(file)] || "application/octet-stream" }).end(bytes);
  } catch {
    response.writeHead(404).end();
  }
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
const origin = `http://127.0.0.1:${server.address().port}`;
const wait = { timeout: 30_000, polling: 30 };
const tickMs = 1000 / 60;
const warmTicks = 150;
const startSeed = 1;
const resetSeed = 77;
let context;
try {
  context = await chromium.launchPersistentContext(path.join(scratch, "profile"), {
    headless: true, channel, chromiumSandbox: true,
    viewport: { width: 1440, height: 900 },
    env: { ...process.env, APPDATA: scratch, LOCALAPPDATA: scratch, TEMP: scratch, TMP: scratch },
    ignoreDefaultArgs: ["--enable-unsafe-swiftshader"],
    proxy: { server: "http://127.0.0.1:9", bypass: "127.0.0.1" },
    args: ["--host-resolver-rules=MAP * ~NOTFOUND, EXCLUDE 127.0.0.1"],
  });
  receipt.browserVersion = context.browser().version();
  await context.route("**/*", route => route.request().url().startsWith(origin + "/") ? route.continue() : route.abort());
  await context.addInitScript(() => {
    const probe = window.publicationProbe = { submits: 0, preps: 0, settled: 0, commits: 0, reads: {} };
    const raf = requestAnimationFrame.bind(window), cancel = cancelAnimationFrame.bind(window);
    const deferred = new Map();
    let running = false, deferredId = -1;
    window.requestAnimationFrame = callback => {
      if (running) return raf(callback);
      deferred.set(deferredId, callback);
      return deferredId--;
    };
    window.cancelAnimationFrame = id => id < 0 ? deferred.delete(id) : cancel(id);
    probe.startFrames = () => {
      running = true;
      for (const callback of deferred.values()) raf(callback);
      deferred.clear();
    };
    probe.arm = () => {
      if (probe.gate && !probe.gate.released) throw new Error("A WebGPU readback is already gated");
      const pending = Promise.withResolvers();
      probe.gate = { ...pending, started: 0, mapped: 0, released: false };
    };
    probe.release = () => {
      probe.gate.released = true;
      probe.gate.resolve();
    };
    if (navigator.gpu) {
      const requestAdapter = GPU.prototype.requestAdapter;
      GPU.prototype.requestAdapter = async function (...args) {
        const adapter = await requestAdapter.apply(this, args);
        if (adapter?.info) {
          const { vendor, architecture, device, description, isFallbackAdapter } = adapter.info;
          probe.adapter = { vendor, architecture, device, description, isFallbackAdapter };
        }
        return adapter;
      };
      const requestDevice = GPUAdapter.prototype.requestDevice;
      GPUAdapter.prototype.requestDevice = async function (...args) {
        const device = await requestDevice.apply(this, args);
        probe.device = device;
        return device;
      };
      const submit = GPUQueue.prototype.submit, mapAsync = GPUBuffer.prototype.mapAsync;
      GPUQueue.prototype.submit = function (...args) {
        const result = submit.apply(this, args);
        probe.submits++;
        return result;
      };
      GPUBuffer.prototype.mapAsync = async function (...args) {
        const gate = probe.gate;
        const held = gate && !gate.released && gate.started < 2;
        if (held) gate.started++;
        const result = await mapAsync.apply(this, args);
        if (held) {
          gate.mapped++;
          await gate.promise;
        }
        return result;
      };
    }
    probe.state = () => JSON.parse(window.render_game_to_text());
    probe.digest = async snapshot => {
      const { viewGeneration, ...binary } = snapshot.binary;
      const groups = ["ships", "projectiles", "debris", "particles", "turrets"];
      const rows = Object.fromEntries(groups.map(group => [group, snapshot[group].map(row => Array.from(row))]));
      const encoded = new TextEncoder().encode(JSON.stringify({
        tick: snapshot.tick, seed: snapshot.seed, profile: snapshot.profile, framework: snapshot.framework,
        arenaHalfSize: snapshot.arenaHalfSize, counts: snapshot.counts, aliveTeams: snapshot.aliveTeams,
        counters: snapshot.counters, pendingReinforcements: snapshot.pendingReinforcements, binary, rows,
      }));
      const bytes = new Uint8Array(await crypto.subtle.digest("SHA-256", encoded));
      return Array.from(bytes, byte => byte.toString(16).padStart(2, "0")).join("");
    };
    probe.calls = () => ({
      submits: probe.submits, preps: probe.preps, settled: probe.settled, commits: probe.commits, reads: { ...probe.reads },
    });
  });
  const page = context.pages()[0] ?? await context.newPage();
  const errors = receipt.pageErrors = [];
  page.on("pageerror", error => errors.push(String(error)));
  page.on("console", message => {
    if (message.type() === "error") errors.push(message.text());
  });
  const start = async (framework, test) => {
    const url = new URL(base, origin);
    url.searchParams.set("framework", framework);
    url.searchParams.set("lang", "en");
    if (test) url.searchParams.set("test", "1");
    await page.goto(url.href, { waitUntil: "domcontentloaded" });
    await page.waitForFunction(() => window.spaceBattle?.ready, undefined, wait);
    await page.evaluate(() => window.spaceBattle.pause(true));
    assert.equal(await page.evaluate(() => window.spaceBattle.runtimeMode), "aot-binary");
  };
  const setSeed = async seed => {
    await page.locator("#seed").fill(String(seed));
    await page.locator("#seed").dispatchEvent("change");
  };
  const advance = count => page.evaluate(ms => window.advanceTime(ms).then(() => {
    const { tick, seed, profile, checksum, ga } = window.publicationProbe.state();
    return { tick, seed, profile, checksum, ga };
  }), tickMs * count);
  await start("skecs", true);
  await setSeed(startSeed);
  const cpuWarm = await advance(warmTicks);
  assert.equal(cpuWarm.tick, warmTicks);
  const cpuNext = await advance(1);
  await setSeed(resetSeed);
  const cpuReset = await advance(0);
  assert.equal(cpuReset.tick, 0);
  const cpuResetNext = await advance(1);
  receipt.reference = { cpuWarm, cpuNext, cpuReset, cpuResetNext };

  await start("skecs-ga", false);
  await setSeed(startSeed);
  const gaWarm = await advance(warmTicks);
  assert.equal(gaWarm.tick, warmTicks);
  assert.equal(gaWarm.seed, startSeed);
  assert.equal(gaWarm.profile, cpuWarm.profile);
  assert.equal(gaWarm.checksum, cpuWarm.checksum);
  assert.equal(gaWarm.ga.status, "webgpu");
  assert.ok(gaWarm.ga.deviceRows > 0, "Warm-up must apply actual WebGPU rows");
  receipt.adapter = await page.evaluate(() => window.publicationProbe.adapter);
  assert.equal(receipt.adapter?.isFallbackAdapter, false);
  const session = await context.browser().newBrowserCDPSession();
  try {
    const { gpu } = await session.send("SystemInfo.getInfo");
    receipt.device = gpu.devices[0];
    assert.ok(receipt.device?.deviceId || receipt.device?.deviceString, "Report the active physical adapter identity");
    assert.equal(gpu.featureStatus.webgpu, "enabled");
  } finally {
    await session.detach();
  }

  await page.evaluate(async () => {
    const probe = window.publicationProbe;
    const all = await getDotnetRuntime(0).getAssemblyExports("SpaceBattle.Web.dll");
    const bridge = all.SpaceBattle.Web.BattleBridge;
    const observe = ["Checksum", "Validate", "TacticCounter", "ShipTactic", "Reinforcement", "ShipObservation",
      "ObserveShip", "ObservedDeath", "DebugColliders", "Snapshot"];
    for (const name of observe) {
      const call = bridge[name];
      if (typeof call !== "function") throw new Error(`Missing export ${name}`);
      probe.reads[name] = 0;
      bridge[name] = function (...args) {
        probe.reads[name]++;
        return call.apply(this, args);
      };
    }
    const prepare = bridge.PrepareGaTick;
    bridge.PrepareGaTick = async function (...args) {
      probe.preps++;
      try { return await prepare.apply(this, args); }
      finally { probe.settled++; }
    };
    const commit = bridge.CommitGaTick;
    bridge.CommitGaTick = function (...args) {
      probe.commits++;
      return commit.apply(this, args);
    };
  });
  const complete = await page.evaluate(async () => {
    const api = window.spaceBattle, probe = window.publicationProbe;
    api.showColliders(true);
    const snapshot = probe.retained = api.snapshot;
    const { tick, seed, checksum, ga } = probe.state();
    return {
      tick, seed, checksum, ga, calls: probe.calls(), viewGeneration: snapshot.binary.viewGeneration ?? null,
      digest: await probe.digest(snapshot), tactics: api.tactics(), reinforcements: api.reinforcements(),
      validated: api.validate(), colliderSummary: api.colliders(),
    };
  });
  assert.equal(complete.checksum, cpuWarm.checksum);
  assert.equal(complete.validated, cpuWarm.checksum);
  assert.ok(complete.calls.reads.DebugColliders > 0, "The bridge monitor must wrap the instance used by the application");
  assert.ok(complete.colliderSummary.rows.length > 0, "Collider view must be published during the heap growth test");
  receipt.completedFrame = {
    tick: complete.tick, seed: complete.seed, checksum: complete.checksum, ga: complete.ga,
    digest: complete.digest, viewGeneration: complete.viewGeneration,
  };

  const arm = () => page.evaluate(() => {
    const probe = window.publicationProbe;
    const calls = probe.calls();
    probe.arm();
    window.spaceBattle.pause(false);
    probe.startFrames();
    return calls;
  });
  const pendingCalls = await arm();
  await page.waitForFunction(() => window.publicationProbe.gate?.mapped === 2, undefined, wait);
  await page.waitForFunction(() => {
    const stats = window.spaceBattle.statistics();
    return stats.backlogTicks >= 11.99 && stats.discardedTicks > 0;
  }, undefined, wait);
  const held = await page.evaluate(async () => {
    const api = window.spaceBattle, probe = window.publicationProbe;
    const { tick, seed, checksum, ga, statistics, diagnostics } = probe.state();
    const tactics = api.tactics(), reinforcements = api.reinforcements(), validated = api.validate();
    const snapshot = api.snapshot, refreshed = api.refresh();
    const serial = snapshot.ships[0][0];
    api.select(serial);
    api.follow(serial);
    api.showColliders(false);
    return { tick, seed, checksum, ga, statistics, diagnostics: diagnostics.error,
      tactics, reinforcements, validated, calls: probe.calls(), mapped: probe.gate.mapped,
      digest: await probe.digest(snapshot), refreshedDigest: await probe.digest(refreshed),
      stillShowingColliders: api.colliders().enabled };
  });
  receipt.pending = {
    tick: held.tick, checksum: held.checksum, digest: held.digest, submits: held.calls.submits - pendingCalls.submits,
    mapped: held.mapped, backlogTicks: held.statistics.backlogTicks, discardedTicks: held.statistics.discardedTicks,
    readsBefore: pendingCalls.reads, readsAfter: held.calls.reads,
  };
  assert.equal(held.diagnostics, null);
  assert.equal(held.tick, warmTicks);
  assert.equal(held.checksum, complete.checksum);
  assert.equal(held.digest, complete.digest);
  assert.equal(held.refreshedDigest, complete.digest);
  assert.equal(held.stillShowingColliders, true);
  assert.deepEqual(held.ga, complete.ga);
  assert.deepEqual(held.tactics, complete.tactics);
  assert.deepEqual(held.reinforcements, complete.reinforcements);
  assert.equal(held.validated, complete.validated);
  assert.equal(held.calls.submits, pendingCalls.submits + 1);
  assert.equal(held.calls.preps, pendingCalls.preps + 1);
  assert.equal(held.calls.commits, pendingCalls.commits);
  assert.ok(held.statistics.backlogTicks <= 12 + 1e-8);
  assert.ok(held.statistics.discardedTicks > 0);

  const growth = await page.evaluate(async () => {
    const api = window.spaceBattle, probe = window.publicationProbe;
    const runtime = getDotnetRuntime(0), module = runtime.Module;
    const memory = module.getMemory(), prior = memory.buffer, bytesBefore = prior.byteLength;
    if (runtime.localHeapViewF64().buffer !== prior) throw new Error("The runtime is not using the observed WebAssembly.Memory");
    const address = module._malloc(bytesBefore);
    if (!address) throw new Error("Could not allocate enough memory to force active Wasm heap growth");
    try {
      const updated = memory.buffer;
      const snapshot = api.snapshot;
      const retained = probe.retained;
      return {
        bytesBefore, bytesAfter: updated.byteLength, sameBuffer: updated === prior, detached: prior.byteLength === 0,
        sameRuntimeBuffer: runtime.localHeapViewF64().buffer === updated, tick: snapshot.tick,
        viewGeneration: snapshot.binary.viewGeneration ?? null, retainedGeneration: retained.binary.viewGeneration ?? null,
        oldShipLength: retained.ships[0]?.length, oldDigest: await probe.digest(retained),
        newDigest: await probe.digest(snapshot), calls: probe.calls(), colliders: api.colliders(),
      };
    } finally { module._free(address); }
  });
  receipt.growth = { ...growth, colliders: growth.colliders.rows.length };
  assert.ok(growth.bytesAfter > growth.bytesBefore && growth.detached && !growth.sameBuffer && growth.sameRuntimeBuffer);
  assert.ok(Number.isSafeInteger(complete.viewGeneration) && growth.viewGeneration > complete.viewGeneration,
    "The same published frame needs a newer view generation after the Wasm heap buffer changes");
  assert.equal(growth.tick, warmTicks);
  assert.equal(growth.retainedGeneration, complete.viewGeneration);
  assert.equal(growth.oldShipLength, 16);
  assert.equal(growth.oldDigest, complete.digest);
  assert.equal(growth.newDigest, complete.digest);
  assert.deepEqual(growth.colliders, complete.colliderSummary);
  assert.deepEqual(growth.calls.reads, pendingCalls.reads, "JS diagnostics must keep all live World reads outside Pending");

  await page.evaluate(() => {
    window.spaceBattle.pause(true);
    window.publicationProbe.release();
  });
  await page.waitForFunction(expected => window.publicationProbe.settled === expected, pendingCalls.settled + 1, wait);
  const paused = await page.evaluate(() => ({
    tick: window.spaceBattle.snapshot.tick, calls: window.publicationProbe.calls(), state: window.publicationProbe.state(),
  }));
  assert.equal(paused.tick, warmTicks);
  assert.equal(paused.calls.commits, pendingCalls.commits);
  assert.equal(paused.calls.submits, pendingCalls.submits + 1);
  assert.equal(paused.state.checksum, cpuWarm.checksum);
  const next = await advance(1);
  const postCommit = await page.evaluate(async () => {
    const probe = window.publicationProbe;
    return { calls: probe.calls(), retainedDigest: await probe.digest(probe.retained),
      collidersDisabled: !window.spaceBattle.colliders().enabled };
  });
  receipt.pausedThenCommitted = { pauseTick: paused.tick, next, ...postCommit };
  assert.equal(next.tick, warmTicks + 1);
  assert.equal(next.checksum, cpuNext.checksum);
  assert.ok(next.ga.deviceRows > complete.ga.deviceRows);
  assert.equal(postCommit.calls.commits, pendingCalls.commits + 1);
  assert.equal(postCommit.calls.submits, pendingCalls.submits + 1, "Prepared tick must commit without another dispatch");
  assert.equal(postCommit.retainedDigest, complete.digest);
  assert.equal(postCommit.collidersDisabled, true);

  const resetCalls = await arm();
  await page.waitForFunction(() => window.publicationProbe.gate?.mapped === 2, undefined, wait);
  await page.evaluate(() => window.spaceBattle.pause(true));
  await setSeed(resetSeed);
  const beforeReset = await page.evaluate(() => ({
    tick: window.spaceBattle.snapshot.tick, seed: window.spaceBattle.snapshot.seed,
    uncachedValidation: window.spaceBattle.validate(), calls: window.publicationProbe.calls(),
  }));
  assert.equal(beforeReset.tick, warmTicks + 1);
  assert.equal(beforeReset.seed, startSeed);
  assert.equal(beforeReset.calls.commits, resetCalls.commits);
  assert.ok(beforeReset.uncachedValidation == null, "A new replay validation must wait for a completed tick");
  assert.deepEqual(beforeReset.calls.reads, resetCalls.reads);
  await page.evaluate(() => window.publicationProbe.release());
  await page.waitForFunction(seed => window.spaceBattle.snapshot.seed === seed && window.spaceBattle.snapshot.tick === 0, resetSeed, wait);
  const reset = await page.evaluate(() => ({
    state: window.publicationProbe.state(), calls: window.publicationProbe.calls(),
  }));
  assert.equal(reset.calls.submits, resetCalls.submits + 1);
  assert.equal(reset.calls.commits, resetCalls.commits, "Reset must discard the held result without committing");
  assert.equal(reset.state.checksum, cpuReset.checksum);
  assert.deepEqual(reset.state.ga, { status: "off", deviceRows: 0 });
  const afterReset = await advance(1);
  assert.equal(afterReset.seed, resetSeed);
  assert.equal(afterReset.tick, 1);
  assert.equal(afterReset.checksum, cpuResetNext.checksum);
  assert.equal(afterReset.ga.status, "webgpu");
  receipt.deferredReset = { previousTick: beforeReset.tick, newTick: reset.state.tick, seed: reset.state.seed,
    resetChecksum: reset.state.checksum, commitDelta: reset.calls.commits - resetCalls.commits, afterReset };

  await advance(warmTicks - 1);
  const beforeLoss = await page.evaluate(async () => {
    const probe = window.publicationProbe;
    window.spaceBattle.pause(true);
    const state = probe.state();
    probe.arm();
    return { tick: state.tick, checksum: state.checksum, rows: state.ga.deviceRows,
      digest: await probe.digest(window.spaceBattle.snapshot), calls: probe.calls() };
  });
  const interrupted = advance(1);
  interrupted.catch(() => {});
  await page.waitForFunction(() => window.publicationProbe.gate?.mapped === 2, undefined, wait);
  const loss = await page.evaluate(async () => {
    const probe = window.publicationProbe;
    probe.device.destroy();
    const { reason, message } = await probe.device.lost;
    probe.release();
    return { reason, message };
  });
  assert.equal(loss.reason, "destroyed");
  const interruptedState = await interrupted;
  assert.equal(interruptedState.ga.status, "faulted");
  assert.equal(interruptedState.tick, beforeLoss.tick);
  assert.equal(interruptedState.checksum, beforeLoss.checksum);
  assert.equal(interruptedState.ga.deviceRows, beforeLoss.rows);
  const heldFault = await page.evaluate(async () => {
    const api = window.spaceBattle, probe = window.publicationProbe;
    api.pause(false);
    await window.advanceTime(1000);
    const snapshot = api.refresh();
    api.showColliders(false);
    api.validate();
    return { calls: probe.calls(), digest: await probe.digest(snapshot) };
  });
  assert.equal(heldFault.digest, beforeLoss.digest);
  assert.equal(heldFault.calls.submits, beforeLoss.calls.submits + 1);
  assert.equal(heldFault.calls.commits, beforeLoss.calls.commits);
  assert.deepEqual(heldFault.calls.reads, beforeLoss.calls.reads);
  assert.match(await page.locator("#ga-status").textContent(), /reset to continue/i);

  await setSeed(resetSeed);
  const lossReset = await advance(0);
  assert.equal(lossReset.tick, 0);
  assert.equal(lossReset.checksum, cpuReset.checksum);
  const lossRestart = await advance(1);
  assert.equal(lossRestart.tick, 1);
  assert.equal(lossRestart.checksum, cpuResetNext.checksum);
  assert.equal(lossRestart.ga.status, "webgpu");
  receipt.deviceLoss = { loss, interruptedState, heldFault, lossReset, lossRestart };

  assert.deepEqual(errors, []);
  receipt.status = "passed";
  console.log(`PASS ${channel}: frame publication, device loss, explicit reset and exact CPU replay`);
} catch (error) {
  receipt.status = "failed";
  receipt.error = error.stack || String(error);
  throw error;
} finally {
  await fs.writeFile(path.join(output, "publication.json"), JSON.stringify(receipt, null, 2) + "\n");
  await context?.close();
  await new Promise(resolve => server.close(resolve));
}
