"use strict";

const fs = require("fs");
const { spawnSync } = require("child_process");

const platform = require("./platform");
const { ensureBinary } = require("./download");

const LatestReleaseApiUrl = `https://api.github.com/repos/${platform.OWNER}/${platform.REPO}/releases/latest`;
const VersionPattern = /\b(?:NanoAgent\s+CLI\s+)?v?(\d+(?:\.\d+){1,3}(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?)\b/i;

function normalizeVersionText(value) {
  if (!value || !value.trim()) {
    return "0.0.0";
  }

  let normalized = value.trim();
  const metadataIndex = normalized.indexOf("+");
  if (metadataIndex >= 0) {
    normalized = normalized.slice(0, metadataIndex);
  }

  if (normalized.startsWith("v") || normalized.startsWith("V")) {
    normalized = normalized.slice(1);
  }

  return normalized;
}

function parseComparableVersion(value) {
  const normalized = normalizeVersionText(value);
  const prereleaseIndex = normalized.indexOf("-");
  const releasePart = prereleaseIndex >= 0
    ? normalized.slice(0, prereleaseIndex)
    : normalized;
  const segments = releasePart
    .split(".")
    .map((segment) => Number.parseInt(segment, 10));

  if (segments.length === 0 || segments.some((segment) => !Number.isFinite(segment))) {
    return null;
  }

  while (segments.length < 4) {
    segments.push(0);
  }

  return {
    normalized,
    segments,
    hasPrerelease: prereleaseIndex >= 0,
  };
}

function compareVersions(left, right) {
  const parsedLeft = parseComparableVersion(left);
  const parsedRight = parseComparableVersion(right);

  if (!parsedLeft || !parsedRight) {
    const normalizedLeft = normalizeVersionText(left);
    const normalizedRight = normalizeVersionText(right);
    return normalizedLeft.localeCompare(normalizedRight, undefined, { numeric: true, sensitivity: "base" });
  }

  for (let index = 0; index < Math.max(parsedLeft.segments.length, parsedRight.segments.length); index += 1) {
    const leftSegment = parsedLeft.segments[index] ?? 0;
    const rightSegment = parsedRight.segments[index] ?? 0;
    if (leftSegment !== rightSegment) {
      return leftSegment - rightSegment;
    }
  }

  if (parsedLeft.hasPrerelease !== parsedRight.hasPrerelease) {
    return parsedLeft.hasPrerelease ? -1 : 1;
  }

  return parsedLeft.normalized.localeCompare(
    parsedRight.normalized,
    undefined,
    { numeric: true, sensitivity: "base" }
  );
}

async function fetchLatestRelease(options = {}) {
  const { timeoutMs = 4000 } = options;
  if (typeof fetch !== "function") {
    return null;
  }

  const response = await fetch(LatestReleaseApiUrl, {
    headers: {
      "Accept": "application/vnd.github+json",
      "User-Agent": `${platform.APP_NAME}-npm-launcher`,
    },
    redirect: "follow",
    signal: typeof AbortSignal !== "undefined" && typeof AbortSignal.timeout === "function"
      ? AbortSignal.timeout(timeoutMs)
      : undefined,
  });

  if (!response.ok) {
    throw new Error(`GitHub returned HTTP ${response.status} ${response.statusText}.`);
  }

  const payload = await response.json();
  const tag = typeof payload?.tag_name === "string" ? payload.tag_name.trim() : "";
  if (!tag) {
    throw new Error("GitHub did not return a release tag.");
  }

  const releaseUrl = typeof payload?.html_url === "string" && payload.html_url.trim()
    ? payload.html_url.trim()
    : `https://github.com/${platform.OWNER}/${platform.REPO}/releases/latest`;

  return {
    tag,
    version: normalizeVersionText(tag),
    releaseUrl,
  };
}

function readInstalledBinaryVersion(binaryPath) {
  if (!binaryPath || !fs.existsSync(binaryPath)) {
    return null;
  }

  try {
    const result = spawnSync(binaryPath, ["--version"], {
      encoding: "utf8",
      stdio: ["ignore", "pipe", "pipe"],
      timeout: 10000,
      windowsHide: true,
    });

    const combined = `${result.stdout || ""}\n${result.stderr || ""}`;
    const match = combined.match(VersionPattern);
    return match?.[1] ? normalizeVersionText(match[1]) : null;
  } catch {
    return null;
  }
}

function shouldSkipRuntimeUpdateCheck() {
  if (process.env.NANOAGENT_SKIP_UPDATE_CHECK === "1") {
    return true;
  }

  if (process.env.NANOAGENT_CLI_TAG || process.env.NANOAGENT_CLI_BASE_URL || process.env.NANOAGENT_CLI_VERSION) {
    return true;
  }

  return process.argv.slice(2).some((arg) => arg === "--no-update-check");
}

function canPromptForUpdate() {
  return Boolean(process.stdin.isTTY && process.stdout.isTTY);
}

async function promptForUpdate(currentVersion, latestVersion) {
  const { default: select } = await import("@inquirer/select");

  return await select({
    message: `NanoAgent ${latestVersion} is available. Update before launch?`,
    choices: [
      {
        name: `Yes, update from ${currentVersion} to ${latestVersion}`,
        value: true,
        description: "Downloads the latest NanoAgent CLI binary, then starts nanoai.",
      },
      {
        name: `No, continue with ${currentVersion}`,
        value: false,
        description: "Skip this update check and launch the currently installed binary.",
      },
    ],
    default: false,
    loop: false,
  });
}

async function maybeUpdateBinary(binaryPath, options = {}) {
  const { log = () => {} } = options;

  if (shouldSkipRuntimeUpdateCheck()) {
    return binaryPath;
  }

  let latestRelease;
  try {
    latestRelease = await fetchLatestRelease();
  } catch {
    return binaryPath;
  }

  if (!latestRelease) {
    return binaryPath;
  }

  const currentVersion = readInstalledBinaryVersion(binaryPath) || normalizeVersionText(platform.resolveVersion());
  if (compareVersions(latestRelease.version, currentVersion) <= 0) {
    return binaryPath;
  }

  if (!canPromptForUpdate()) {
    return binaryPath;
  }

  let shouldUpdate;
  try {
    shouldUpdate = await promptForUpdate(currentVersion, latestRelease.version);
  } catch {
    return binaryPath;
  }

  if (!shouldUpdate) {
    return binaryPath;
  }

  log(`Updating NanoAgent CLI to ${latestRelease.tag}...`);

  try {
    return await ensureBinary({
      force: true,
      tag: latestRelease.tag,
      log,
    });
  } catch (error) {
    const message = error && error.message
      ? error.message
      : String(error);
    log(`Update failed: ${message}`);
    log("Starting the currently installed NanoAgent CLI instead.");
    return binaryPath;
  }
}

module.exports = {
  compareVersions,
  fetchLatestRelease,
  maybeUpdateBinary,
  normalizeVersionText,
  readInstalledBinaryVersion,
  shouldSkipRuntimeUpdateCheck,
};
