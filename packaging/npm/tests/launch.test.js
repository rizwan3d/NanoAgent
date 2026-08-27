"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");
const path = require("path");
const { EventEmitter } = require("events");

const {
  isRetryableWindowsErrorCode,
  launchBinary,
  resolveLaunchCwd,
  shouldRetryLaunch,
} = require("../scripts/launch");

function emitAsync(child, eventName, payload) {
  queueMicrotask(() => {
    child.emit(eventName, payload);
  });
  return child;
}

test("isRetryableWindowsErrorCode recognizes transient Windows spawn failures", () => {
  assert.equal(isRetryableWindowsErrorCode("UNKNOWN"), true);
  assert.equal(isRetryableWindowsErrorCode("EACCES"), true);
  assert.equal(isRetryableWindowsErrorCode("EBUSY"), true);
  assert.equal(isRetryableWindowsErrorCode("ENOENT"), true);
  assert.equal(isRetryableWindowsErrorCode("EPERM"), true);
  assert.equal(isRetryableWindowsErrorCode("EINVAL"), false);
  assert.equal(isRetryableWindowsErrorCode(""), false);
});

test("shouldRetryLaunch only retries retryable errors on Windows", () => {
  assert.equal(shouldRetryLaunch({ code: "UNKNOWN" }, "win32"), true);
  assert.equal(shouldRetryLaunch({ code: "EPERM" }, "win32"), true);
  assert.equal(shouldRetryLaunch({ code: "EINVAL" }, "win32"), false);
  assert.equal(shouldRetryLaunch({ code: "UNKNOWN" }, "linux"), false);
});

test("resolveLaunchCwd prefers the inherited cwd when it is valid", () => {
  const cwd = path.join("workspace", "project");
  const binaryPath = path.join("vendor", "StemCode.CLI.exe");

  const result = resolveLaunchCwd(binaryPath, {
    cwdProvider: () => cwd,
    existsSync: (candidate) => candidate === cwd,
    statSync: () => ({ isDirectory: () => true }),
  });

  assert.equal(result, cwd);
});

test("resolveLaunchCwd falls back to the binary directory when cwd is unavailable", () => {
  const binaryPath = path.join("vendor", "StemCode.CLI.exe");

  const result = resolveLaunchCwd(binaryPath, {
    cwdProvider: () => {
      throw new Error("cwd is unavailable");
    },
  });

  assert.equal(result, path.dirname(binaryPath));
});

test("launchBinary retries transient Windows spawn failures", async () => {
  const attempts = [];
  let callCount = 0;

  const result = await launchBinary(
    path.join("vendor", "StemCode.CLI.exe"),
    ["--version"],
    {
      platform: "win32",
      retryDelaysMs: [0, 0],
      wait: async () => {},
      cwdProvider: () => path.join("workspace", "repo"),
      existsSync: () => true,
      statSync: () => ({ isDirectory: () => true }),
      spawnImpl: (command, args, spawnOptions) => {
        attempts.push({ command, args, spawnOptions });
        callCount += 1;

        if (callCount === 1) {
          return emitAsync(
            new EventEmitter(),
            "error",
            Object.assign(new Error("spawn UNKNOWN"), { code: "UNKNOWN", errno: -4094 })
          );
        }

        return emitAsync(new EventEmitter(), "spawn");
      },
      log: () => {},
    }
  );

  assert.equal(result.attemptCount, 2);
  assert.equal(attempts.length, 2);
  assert.equal(attempts[0].spawnOptions.cwd, path.join("workspace", "repo"));
  assert.deepEqual(attempts[0].args, ["--version"]);
});

test("launchBinary throws a detailed error after the final failed attempt", async () => {
  await assert.rejects(
    () => launchBinary(
      path.join("vendor", "StemCode.CLI.exe"),
      [],
      {
        platform: "win32",
        retryDelaysMs: [0],
        wait: async () => {},
        cwdProvider: () => path.join("workspace", "repo"),
        existsSync: () => true,
        statSync: () => ({ isDirectory: () => true }),
        spawnImpl: () =>
          emitAsync(
            new EventEmitter(),
            "error",
            Object.assign(new Error("spawn UNKNOWN"), { code: "UNKNOWN", errno: -4094 })
          ),
      }
    ),
    (error) => {
      assert.equal(error.code, "UNKNOWN");
      assert.match(error.message, /Failed to start StemCode CLI/);
      assert.match(error.message, /Launch directory:/);
      return true;
    }
  );
});
