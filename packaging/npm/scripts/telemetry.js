"use strict";

// Best-effort, anonymous PostHog "installed" event shared by the postinstall step
// and the runtime first-run download (covers `bun add`, which skips postinstall).
// Telemetry must never affect installation: every failure is swallowed and the
// network call is bounded by a short timeout. Opt out with NANOAGENT_TELEMETRY_DISABLED
// or the cross-tool DO_NOT_TRACK convention.

const crypto = require("crypto");

const platform = require("./platform");

// Mirrors NanoAgent.Infrastructure.Configuration.TelemetryOptions so install and
// in-product analytics land in the same PostHog project.
const TELEMETRY_HOST = "https://us.i.posthog.com";
const TELEMETRY_PROJECT_TOKEN = "phc_AKZFSyU239kkQ5GQ2y4idb8MtFX96kVekgezgnsELHRk";
const TELEMETRY_EVENT = "nanoagent cli installed";
const TIMEOUT_MS = 5000;

function isTruthy(value) {
  if (!value) return false;
  return ["1", "true", "yes", "on"].includes(String(value).trim().toLowerCase());
}

function telemetryEnabled() {
  if (isTruthy(process.env.NANOAGENT_TELEMETRY_DISABLED) || isTruthy(process.env.DO_NOT_TRACK)) {
    return false;
  }
  return Boolean(TELEMETRY_PROJECT_TOKEN);
}

// Identify the package manager that triggered the install. npm/pnpm/yarn set
// npm_config_user_agent during their lifecycle; bun is detected from the runtime
// when the binary is fetched lazily on first launch.
function detectInstallMethod() {
  const ua = (process.env.npm_config_user_agent || "").toLowerCase();
  if (ua.includes("bun")) return "bun";
  if (ua.includes("pnpm")) return "pnpm";
  if (ua.includes("yarn")) return "yarn";
  if (ua.includes("npm")) return "npm";
  if (process.versions && process.versions.bun) return "bun";
  return "npm";
}

function osFamily() {
  switch (process.platform) {
    case "win32":
      return "windows";
    case "darwin":
      return "macos";
    case "linux":
      return "linux";
    default:
      return "other";
  }
}

function isCi() {
  return Boolean(
    process.env.CI ||
      process.env.GITHUB_ACTIONS ||
      process.env.GITLAB_CI ||
      process.env.BITBUCKET_BUILD_NUMBER
  );
}

function resolveQuietly(resolver, fallback) {
  try {
    const value = resolver();
    return value && String(value).trim() ? value : fallback;
  } catch {
    return fallback;
  }
}

async function trackInstall(options = {}) {
  try {
    if (!telemetryEnabled() || typeof fetch !== "function") {
      return;
    }

    const ci = isCi();
    const payload = {
      api_key: TELEMETRY_PROJECT_TOKEN,
      event: TELEMETRY_EVENT,
      distinct_id: crypto.randomUUID(),
      properties: {
        $lib: "nanoagent-installer",
        install_method: options.method || detectInstallMethod(),
        nanoagent_version: resolveQuietly(() => platform.resolveTag(), "unknown"),
        os_family: osFamily(),
        platform: resolveQuietly(() => platform.resolveRid(), "unknown"),
        app_surface: "cli",
        execution_environment: ci ? "ci" : "local",
        is_ci: ci,
      },
    };

    const signal =
      typeof AbortSignal !== "undefined" && typeof AbortSignal.timeout === "function"
        ? AbortSignal.timeout(TIMEOUT_MS)
        : undefined;

    await fetch(`${TELEMETRY_HOST}/i/v0/e/`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
      signal,
    });
  } catch {
    // Telemetry must never affect installation.
  }
}

module.exports = { trackInstall, detectInstallMethod };
