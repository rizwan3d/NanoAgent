"use strict";

// Downloads, verifies, and extracts the NanoAgent CLI binary from the matching
// GitHub release. Shared by the postinstall step and the runtime launcher so the
// CLI self-heals on first run even when a package manager skips lifecycle
// scripts (notably `bun install`, which does not run postinstall by default).

const fs = require("fs");
const path = require("path");
const crypto = require("crypto");
const AdmZip = require("adm-zip");

const platform = require("./platform");

async function fetchBuffer(url) {
  if (typeof fetch !== "function") {
    throw new Error(
      "Global fetch is unavailable. NanoAgent's npm package requires Node.js 18 or newer."
    );
  }

  const response = await fetch(url, {
    redirect: "follow",
    headers: { "User-Agent": `${platform.APP_NAME}-npm-installer` },
  });

  if (!response.ok) {
    throw new Error(`Request to ${url} failed with HTTP ${response.status} ${response.statusText}.`);
  }

  return Buffer.from(await response.arrayBuffer());
}

function parseExpectedChecksum(checksumsText, assetName) {
  for (const rawLine of checksumsText.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line) continue;

    const match = line.match(/^([0-9a-fA-F]{64})[\s*]+(.+)$/);
    if (!match) continue;

    let file = match[2].trim();
    file = file.replace(/^\*/, "").replace(/^\.\//, "");

    if (file === assetName) {
      return match[1].toLowerCase();
    }
  }
  return null;
}

function sha256(buffer) {
  return crypto.createHash("sha256").update(buffer).digest("hex").toLowerCase();
}

function extractExecutable(zipBuffer, destinationPath) {
  const zip = new AdmZip(zipBuffer);
  const wanted = platform.executableFileName();

  const entry = zip.getEntries().find((candidate) => {
    if (candidate.isDirectory) return false;
    const base = candidate.entryName.split("/").pop();
    return base === wanted;
  });

  if (!entry) {
    throw new Error(`Release archive did not contain the expected executable '${wanted}'.`);
  }

  const data = entry.getData();
  fs.mkdirSync(path.dirname(destinationPath), { recursive: true });
  fs.writeFileSync(destinationPath, data);
  if (process.platform !== "win32") {
    fs.chmodSync(destinationPath, 0o755);
  }
}

// Ensures the platform binary is present in vendor/. Returns the absolute path.
async function ensureBinary(options = {}) {
  const { force = false, log = () => {}, tag } = options;

  const binaryPath = platform.installedBinaryPath();
  if (!force && fs.existsSync(binaryPath)) {
    return binaryPath;
  }

  const rid = platform.resolveRid();
  const asset = platform.assetName(rid);
  const resolvedTag = tag && tag.trim()
    ? tag.trim()
    : platform.resolveTag();
  const base = platform.baseDownloadUrl(resolvedTag);
  const assetUrl = `${base}/${asset}`;
  const checksumsUrl = `${base}/${platform.CHECKSUMS_NAME}`;

  log(`Downloading ${asset} (${resolvedTag})...`);
  const archiveBuffer = await fetchBuffer(assetUrl);

  log(`Verifying ${platform.CHECKSUMS_NAME}...`);
  const checksumsText = (await fetchBuffer(checksumsUrl)).toString("utf8");
  const expected = parseExpectedChecksum(checksumsText, asset);
  if (!expected) {
    throw new Error(`${platform.CHECKSUMS_NAME} does not contain a checksum for ${asset}.`);
  }

  const actual = sha256(archiveBuffer);
  if (actual !== expected) {
    throw new Error(
      `SHA256 verification failed for ${asset}. Expected ${expected}, got ${actual}.`
    );
  }

  log("Extracting NanoAgent CLI...");
  // Extract to a temp file first, then rename so concurrent runs never observe
  // a partially written executable.
  const tempPath = path.join(
    platform.vendorDir(),
    `.${platform.executableFileName()}.${process.pid}.tmp`
  );
  extractExecutable(archiveBuffer, tempPath);
  fs.renameSync(tempPath, binaryPath);

  log(`Installed NanoAgent CLI to ${binaryPath}.`);
  return binaryPath;
}

module.exports = { ensureBinary, parseExpectedChecksum };

// Allow `node scripts/download.js` for manual/forced reinstall.
if (require.main === module) {
  ensureBinary({ force: true, log: (m) => console.error(`[nanoagent] ${m}`) }).catch((err) => {
    console.error(`[nanoagent] ${err.message}`);
    process.exit(1);
  });
}
