"use strict";

// Shared platform/asset resolution used by both the postinstall step and the
// runtime launcher. Keep this dependency-free so it loads in any environment.

const os = require("os");
const path = require("path");

const OWNER = "rizwan3d";
const REPO = "NanoAgent";
const APP_NAME = "NanoAgent.CLI";
// Executable name inside the release archive (matches the AOT-published output).
const EXECUTABLE_NAME = "NanoAgent.CLI";
const CHECKSUMS_NAME = "SHA256SUMS";

// Maps Node's process.platform/process.arch onto the .NET runtime identifiers
// used for the published release assets.
function resolveRid() {
  const platform = process.platform;
  const arch = process.arch;

  if (platform === "win32") {
    if (arch === "x64") return "win-x64";
    throw new Error(`Unsupported Windows architecture '${arch}'. NanoAgent ships win-x64 only.`);
  }

  if (platform === "darwin") {
    if (arch === "x64") return "osx-x64";
    if (arch === "arm64") return "osx-arm64";
    throw new Error(`Unsupported macOS architecture '${arch}'.`);
  }

  if (platform === "linux") {
    if (arch === "x64") return "linux-x64";
    if (arch === "arm64") return "linux-arm64";
    throw new Error(`Unsupported Linux architecture '${arch}'.`);
  }

  throw new Error(`Unsupported operating system '${platform}'.`);
}

function executableFileName() {
  return process.platform === "win32" ? `${EXECUTABLE_NAME}.exe` : EXECUTABLE_NAME;
}

// Version baked into package.json by the release workflow; the matching release
// is tagged "v<version>".
function resolveVersion() {
  const override = process.env.NANOAGENT_CLI_VERSION;
  if (override && override.trim()) {
    return override.trim().replace(/^v/i, "");
  }
  const pkg = require(path.join(__dirname, "..", "package.json"));
  return pkg.version;
}

function resolveTag() {
  const override = process.env.NANOAGENT_CLI_TAG;
  if (override && override.trim()) {
    return override.trim();
  }
  return `v${resolveVersion()}`;
}

function baseDownloadUrl(tagOverride) {
  const override = process.env.NANOAGENT_CLI_BASE_URL;
  if (override && override.trim()) {
    return override.trim().replace(/\/+$/, "");
  }
  const tag = tagOverride && tagOverride.trim()
    ? tagOverride.trim()
    : resolveTag();
  return `https://github.com/${OWNER}/${REPO}/releases/download/${tag}`;
}

function assetName(rid) {
  return `${APP_NAME}-${rid}.zip`;
}

function vendorDir() {
  return path.join(__dirname, "..", "vendor");
}

function installedBinaryPath() {
  return path.join(vendorDir(), executableFileName());
}

module.exports = {
  OWNER,
  REPO,
  APP_NAME,
  EXECUTABLE_NAME,
  CHECKSUMS_NAME,
  resolveRid,
  executableFileName,
  resolveVersion,
  resolveTag,
  baseDownloadUrl,
  assetName,
  vendorDir,
  installedBinaryPath,
  homedir: os.homedir,
};
