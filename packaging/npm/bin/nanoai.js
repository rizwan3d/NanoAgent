#!/usr/bin/env node
"use strict";

const { spawn } = require("child_process");

const { ensureBinary } = require("../scripts/download");
const { maybeUpdateBinary } = require("../scripts/update");
const { trackInstall } = require("../scripts/telemetry");

function log(message) {
  console.error(`[nanoagent] ${message}`);
}

function forwardSignal(child, signal) {
  if (!child || child.killed) {
    return;
  }

  try {
    child.kill(signal);
  } catch {
    // Ignore races where the process exits before the signal is forwarded.
  }
}

async function main() {
  // On a clean `bun add` (postinstall is skipped) the binary is fetched here on
  // first launch; record the install once at that point.
  let binaryPath = await ensureBinary({ log, onDownloaded: () => trackInstall() });
  binaryPath = await maybeUpdateBinary(binaryPath, { log });

  const child = spawn(binaryPath, process.argv.slice(2), {
    stdio: "inherit",
    windowsHide: false,
  });

  const forwardedSignals = ["SIGINT", "SIGTERM", "SIGHUP"];
  const signalHandlers = new Map();

  for (const signal of forwardedSignals) {
    const handler = () => forwardSignal(child, signal);
    signalHandlers.set(signal, handler);
    process.on(signal, handler);
  }

  child.on("error", (error) => {
    log(`Failed to start NanoAgent CLI: ${error.message}`);
    process.exit(1);
  });

  child.on("exit", (code, signal) => {
    for (const [event, handler] of signalHandlers) {
      process.removeListener(event, handler);
    }

    if (signal) {
      process.kill(process.pid, signal);
      return;
    }

    process.exit(code ?? 0);
  });
}

main().catch((error) => {
  log(error && error.message ? error.message : String(error));
  process.exit(1);
});
