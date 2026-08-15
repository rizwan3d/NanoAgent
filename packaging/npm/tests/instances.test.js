"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");

const childProcess = require("child_process");
const instances = require("../scripts/instances");

// Applies process/platform/spawnSync overrides for the duration of an async
// callback, restoring everything only after the callback settles. Synchronous
// wrappers restore state too early because the awaited body runs later.
async function withOverrides(options, fn) {
  const { platform, tty, spawnSync } = options;
  const originalPlatform = Object.getOwnPropertyDescriptor(process, "platform");
  const originalStdin = Object.getOwnPropertyDescriptor(process.stdin, "isTTY");
  const originalStdout = Object.getOwnPropertyDescriptor(process.stdout, "isTTY");
  const originalSpawn = childProcess.spawnSync;

  if (platform !== undefined) {
    Object.defineProperty(process, "platform", { value: platform, configurable: true });
  }
  if (tty !== undefined) {
    Object.defineProperty(process.stdin, "isTTY", { value: tty, configurable: true });
    Object.defineProperty(process.stdout, "isTTY", { value: tty, configurable: true });
  }
  if (spawnSync) {
    childProcess.spawnSync = spawnSync;
  }

  try {
    return await fn();
  } finally {
    try {
      if (originalPlatform) Object.defineProperty(process, "platform", originalPlatform);
      else delete process.platform;
    } catch { /* ignore restore errors */ }
    try {
      if (originalStdin) Object.defineProperty(process.stdin, "isTTY", originalStdin);
      else delete process.stdin.isTTY;
    } catch { /* ignore restore errors */ }
    try {
      if (originalStdout) Object.defineProperty(process.stdout, "isTTY", originalStdout);
      else delete process.stdout.isTTY;
    } catch { /* ignore restore errors */ }
    childProcess.spawnSync = originalSpawn;
  }
}

test("matchesStemCode recognizes StemCode binaries", () => {
  assert.equal(instances.matchesStemCode("stemcode"), true);
  assert.equal(instances.matchesStemCode("StemCode.CLI"), true);
  assert.equal(instances.matchesStemCode("stemcode.exe"), true);
  assert.equal(instances.matchesStemCode("STEMCODE.EXE"), true);
  assert.equal(instances.matchesStemCode("dotnet"), false);
  assert.equal(instances.matchesStemCode("node"), false);
  assert.equal(instances.matchesStemCode(""), false);
});

test("parseWindowsTaskList parses tasklist CSV and excludes current pid", () => {
  const text = '"stemcode.exe","1234","Services","0","12,345 K"\r\n"dotnet.exe","5678","Console","1","1 K"\r\n';
  const result = instances.parseWindowsTaskList(text, 5678);
  assert.deepEqual(result, [{ pid: 1234, name: "stemcode.exe" }]);
});

test("parsePosixPs parses ps output and skips the header", () => {
  const text = "  PID COMM\n 1234 StemCode.CLI\n 5678 dotnet\n";
  const result = instances.parsePosixPs(text, 5678);
  assert.deepEqual(result, [{ pid: 1234, name: "StemCode.CLI" }]);
});

test("findOtherInstances uses tasklist on Windows", async () => {
  const text = '"stemcode.exe","1234","Services","0","1 K"\r\n"dotnet.exe","2","Console","1","1 K"\r\n';
  const mock = (cmd) => {
    assert.equal(cmd, "tasklist");
    return { stdout: text };
  };
  const found = await withOverrides({ platform: "win32", spawnSync: mock }, () =>
    instances.findOtherInstances());
  assert.deepEqual(found, [{ pid: 1234, name: "stemcode.exe" }]);
});

test("findOtherInstances uses ps on POSIX", async () => {
  const text = "  PID COMM\n 1234 StemCode.CLI\n 2 dotnet\n";
  const mock = (cmd) => {
    assert.equal(cmd, "ps");
    return { stdout: text };
  };
  const found = await withOverrides({ platform: "linux", spawnSync: mock }, () =>
    instances.findOtherInstances());
  assert.deepEqual(found, [{ pid: 1234, name: "StemCode.CLI" }]);
});

test("terminateInstance runs taskkill on Windows", async () => {
  const calls = [];
  const mock = (cmd, args) => { calls.push([cmd, args]); return {}; };
  await withOverrides({ platform: "win32", spawnSync: mock }, () =>
    instances.terminateInstance({ pid: 1234, name: "stemcode.exe" }));
  assert.deepEqual(calls, [["taskkill", ["/PID", "1234", "/F", "/T"]]]);
});

test("terminateInstance runs kill on POSIX", async () => {
  const calls = [];
  const mock = (cmd, args) => { calls.push([cmd, args]); return {}; };
  await withOverrides({ platform: "linux", spawnSync: mock }, () =>
    instances.terminateInstance({ pid: 1234, name: "StemCode.CLI" }));
  assert.deepEqual(calls, [["kill", ["-9", "1234"]]]);
});

test("terminateOtherInstances returns [] when no other instances exist", async () => {
  const text = "  PID COMM\n 2 dotnet\n";
  const mock = () => ({ stdout: text });
  const terminated = await withOverrides({ platform: "linux", spawnSync: mock }, () =>
    instances.terminateOtherInstances());
  assert.deepEqual(terminated, []);
});

test("terminateOtherInstances returns [] when non-interactive", async () => {
  const text = "  PID COMM\n 1234 StemCode.CLI\n";
  const calls = [];
  const mock = (cmd, args) => { calls.push([cmd, args]); return { stdout: text }; };
  const terminated = await withOverrides({ platform: "linux", tty: false, spawnSync: mock }, () =>
    instances.terminateOtherInstances());
  assert.deepEqual(terminated, []);
  // No kill command should have been issued without a prompt.
  assert.deepEqual(calls.filter(([cmd]) => cmd === "kill" || cmd === "taskkill"), []);
});

test("promptToTerminate approves when the user selects Yes", async () => {
  const fakeLoader = async () => async () => true;
  const result = await instances.promptToTerminate(
    [{ pid: 1234, name: "StemCode.CLI" }],
    fakeLoader
  );
  assert.equal(result, true);
});

test("promptToTerminate declines when the user selects No", async () => {
  const fakeLoader = async () => async () => false;
  const result = await instances.promptToTerminate(
    [{ pid: 1234, name: "stemcode.exe" }],
    fakeLoader
  );
  assert.equal(result, false);
});

test("terminateOtherInstances terminates when the prompt is approved", async () => {
  const text = "  PID COMM\n 1234 StemCode.CLI\n";
  const killCalls = [];
  const spawnMock = (cmd, args) => {
    if (cmd === "ps") return { stdout: text };
    killCalls.push([cmd, args]);
    return {};
  };

  // Exercise the orchestration end-to-end with a fake prompt loader so no real
  // TTY is required. tty:true makes canPromptForUpdate() return true.
  const terminated = await withOverrides(
    { platform: "linux", tty: true, spawnSync: spawnMock },
    async () => instances.terminateOtherInstances({ selectLoader: async () => async () => true }));

  assert.deepEqual(terminated, [{ pid: 1234, name: "StemCode.CLI" }]);
  assert.deepEqual(killCalls, [["kill", ["-9", "1234"]]]);
});
