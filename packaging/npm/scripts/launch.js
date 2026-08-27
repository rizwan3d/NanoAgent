"use strict";

const fs = require("fs");
const path = require("path");
const { spawn } = require("child_process");

const WindowsLaunchRetryDelaysMs = [0, 150, 500, 1000];

function isRetryableWindowsErrorCode(code) {
  const normalized = typeof code === "string"
    ? code.trim().toUpperCase()
    : "";

  return normalized === "UNKNOWN" ||
    normalized === "EACCES" ||
    normalized === "EBUSY" ||
    normalized === "ENOENT" ||
    normalized === "EPERM";
}

function resolveLaunchCwd(binaryPath, options = {}) {
  const {
    cwdProvider = process.cwd.bind(process),
    existsSync = fs.existsSync,
    statSync = fs.statSync,
    fallbackDir,
  } = options;

  try {
    const cwd = cwdProvider();
    if (cwd &&
        existsSync(cwd) &&
        statSync(cwd).isDirectory()) {
      return cwd;
    }
  } catch {
    // Fall through to the binary directory when the inherited cwd is invalid.
  }

  return fallbackDir || path.dirname(binaryPath);
}

function shouldRetryLaunch(error, platform) {
  return platform === "win32" && isRetryableWindowsErrorCode(error?.code);
}

function waitForSpawnResult(child) {
  return new Promise((resolve) => {
    let settled = false;

    const finish = (result) => {
      if (settled) {
        return;
      }

      settled = true;
      child.removeListener("spawn", onSpawn);
      child.removeListener("error", onError);
      resolve(result);
    };

    const onSpawn = () => finish({ ok: true, child });
    const onError = (error) => finish({ ok: false, error });

    child.once("spawn", onSpawn);
    child.once("error", onError);
  });
}

function sleep(delayMs) {
  return new Promise((resolve) => setTimeout(resolve, delayMs));
}

function createLaunchError(error, binaryPath, launchCwd) {
  const details = [
    `Failed to start StemCode CLI at ${binaryPath}.`,
    `Launch directory: ${launchCwd}.`,
  ];

  if (error?.code) {
    details.push(`Windows/Node error: ${error.code}.`);
  }

  if (error?.message) {
    details.push(error.message);
  }

  const launchError = new Error(details.join(" "));
  if (error?.code) {
    launchError.code = error.code;
  }
  if (error?.errno !== undefined) {
    launchError.errno = error.errno;
  }
  launchError.cause = error;
  return launchError;
}

async function launchBinary(binaryPath, args, options = {}) {
  const {
    log = () => {},
    platform = process.platform,
    spawnImpl = spawn,
    wait = sleep,
    retryDelaysMs = WindowsLaunchRetryDelaysMs,
    cwdProvider,
    existsSync,
    statSync,
  } = options;

  const delays = platform === "win32" ? retryDelaysMs : [0];
  let lastError = null;
  let lastLaunchCwd = resolveLaunchCwd(binaryPath, {
    cwdProvider,
    existsSync,
    statSync,
  });

  for (let attempt = 0; attempt < delays.length; attempt += 1) {
    if (attempt > 0) {
      await wait(delays[attempt]);
    }

    const launchCwd = resolveLaunchCwd(binaryPath, {
      cwdProvider,
      existsSync,
      statSync,
    });
    lastLaunchCwd = launchCwd;

    const child = spawnImpl(binaryPath, args, {
      cwd: launchCwd,
      stdio: "inherit",
      windowsHide: false,
    });

    const result = await waitForSpawnResult(child);
    if (result.ok) {
      if (attempt > 0) {
        log(`StemCode CLI launch succeeded after ${attempt + 1} attempts.`);
      }

      return {
        child,
        launchCwd,
        attemptCount: attempt + 1,
      };
    }

    lastError = result.error;
    if (!shouldRetryLaunch(lastError, platform) || attempt === delays.length - 1) {
      throw createLaunchError(lastError, binaryPath, lastLaunchCwd);
    }

    log(
      `StemCode CLI launch failed (${lastError.message}). ` +
      `Retrying in ${delays[attempt + 1]}ms...`
    );
  }

  throw createLaunchError(lastError, binaryPath, lastLaunchCwd);
}

module.exports = {
  createLaunchError,
  isRetryableWindowsErrorCode,
  launchBinary,
  resolveLaunchCwd,
  shouldRetryLaunch,
  waitForSpawnResult,
};
