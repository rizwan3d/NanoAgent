"use strict";

// Detects and terminates other running StemCode CLI sessions so an in-place
// update can replace the currently executing binary without file-lock errors.
// Enumeration and termination delegate to OS tools (tasklist/ps, taskkill/kill)
// so the logic stays portable and dependency-free.

const childProcess = require("child_process");

// Process names that identify a running StemCode CLI binary: the archive
// executable (StemCode.CLI) and the installed command name (stemcode).
const STEMCODE_PROCESS_NAMES = new Set(["stemcode", "stemcode.cli"]);

function normalizeName(name) {
  if (!name) return "";
  let normalized = String(name).trim();
  if (normalized.toLowerCase().endsWith(".exe")) {
    normalized = normalized.slice(0, -4);
  }
  return normalized.toLowerCase();
}

function matchesStemCode(name) {
  return STEMCODE_PROCESS_NAMES.has(normalizeName(name));
}

// Extracts the quoted fields from a single `tasklist /FO CSV` line.
function parseCsvFields(line) {
  const fields = [];
  const regex = /"([^"]*)"/g;
  let match;
  while ((match = regex.exec(line)) !== null) {
    fields.push(match[1]);
  }
  return fields;
}

// Parses `tasklist /NH /FO CSV` output into StemCode instances, excluding the
// current process id.
function parseWindowsTaskList(text, selfPid) {
  const instances = [];
  if (!text) return instances;

  for (const line of text.split(/\r?\n/)) {
    const fields = parseCsvFields(line);
    if (fields.length < 2) continue;

    const name = fields[0];
    const pid = Number.parseInt(fields[1], 10);
    if (!Number.isFinite(pid) || pid === selfPid) continue;
    if (matchesStemCode(name)) {
      instances.push({ pid, name });
    }
  }

  return instances;
}

// Parses `ps -eo pid,comm` output into StemCode instances, excluding the
// current process id. The header row (starts with PID) is skipped.
function parsePosixPs(text, selfPid) {
  const instances = [];
  if (!text) return instances;

  const lines = text.split(/\r?\n/);
  for (const rawLine of lines) {
    const line = rawLine.trim();
    if (!line) continue;
    if (line.toUpperCase().startsWith("PID")) continue;

    const spaceIndex = line.indexOf(" ");
    if (spaceIndex < 0) continue;

    const pid = Number.parseInt(line.slice(0, spaceIndex), 10);
    const name = line.slice(spaceIndex + 1).trim();
    if (!Number.isFinite(pid) || pid === selfPid) continue;
    if (matchesStemCode(name)) {
      instances.push({ pid, name });
    }
  }

  return instances;
}

// Returns running StemCode CLI instances other than the current node process.
// Best-effort: any enumeration error returns an empty list.
function findOtherInstances() {
  const selfPid = process.pid;
  try {
    if (process.platform === "win32") {
      const result = childProcess.spawnSync("tasklist", ["/NH", "/FO", "CSV"], {
        encoding: "utf8",
        windowsHide: true,
      });
      return parseWindowsTaskList(result.stdout || "", selfPid);
    }

    const result = childProcess.spawnSync("ps", ["-eo", "pid,comm"], {
      encoding: "utf8",
      windowsHide: true,
    });
    return parsePosixPs(result.stdout || "", selfPid);
  } catch {
    return [];
  }
}

// Best-effort termination of a single instance. Failures (already exited,
// permissions) are swallowed so an update can proceed.
function terminateInstance(instance) {
  try {
    if (process.platform === "win32") {
      childProcess.spawnSync("taskkill", ["/PID", String(instance.pid), "/F", "/T"], {
        encoding: "utf8",
        windowsHide: true,
      });
    } else {
      childProcess.spawnSync("kill", ["-9", String(instance.pid)], {
        encoding: "utf8",
        windowsHide: true,
      });
    }
  } catch {
    // Best-effort: ignore termination failures.
  }
}

function canPromptForUpdate() {
  return Boolean(process.stdin.isTTY && process.stdout.isTTY);
}

// Loads the inquirer select prompt. Isolated so tests can inject a fake loader
// and exercise the prompt without a real TTY.
async function loadSelect() {
  const { default: select } = await import("@inquirer/select");
  return select;
}

async function promptToTerminate(instances, selectLoader = loadSelect) {
  const select = await selectLoader();
  const list = instances.map((instance) => `${instance.name} (PID ${instance.pid})`).join(", ");

  return await select({
    message: `${instances.length} other StemCode instance(s) are running (${list}). Terminate them and update?`,
    choices: [
      {
        name: "Yes, terminate them and update",
        value: true,
        description: "Closes the other sessions so the running binary can be replaced, then updates.",
      },
      {
        name: "No, update anyway",
        value: false,
        description: "Attempt the update without terminating other sessions (may fail if a session holds the binary).",
      },
    ],
    default: true,
    loop: false,
  });
}

// Orchestrates the running-instance check: finds other StemCode sessions,
// prompts (interactively) when any are found, and terminates the ones the user
// approves. Returns the terminated instances. Non-interactive callers (no TTY)
// skip silently so the update proceeds without surprising side effects.
async function terminateOtherInstances(options = {}) {
  const { log = () => {}, selectLoader } = options;
  const instances = findOtherInstances();
  if (instances.length === 0) {
    return [];
  }

  if (!canPromptForUpdate()) {
    return [];
  }

  let shouldTerminate;
  try {
    shouldTerminate = await promptToTerminate(instances, selectLoader);
  } catch {
    return [];
  }

  if (!shouldTerminate) {
    return [];
  }

  log(`Terminating ${instances.length} other StemCode instance(s)...`);
  for (const instance of instances) {
    terminateInstance(instance);
  }

  return instances;
}

module.exports = {
  STEMCODE_PROCESS_NAMES,
  canPromptForUpdate,
  findOtherInstances,
  matchesStemCode,
  normalizeName,
  parseCsvFields,
  parsePosixPs,
  parseWindowsTaskList,
  promptToTerminate,
  terminateInstance,
  terminateOtherInstances,
};
