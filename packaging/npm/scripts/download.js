"use strict";

// Downloads, verifies, and extracts the StemCode CLI binary from the matching
// GitHub release. Shared by the postinstall step and the runtime launcher so the
// CLI self-heals on first run even when a package manager skips lifecycle
// scripts (notably `bun install`, which does not run postinstall by default).

const fs = require("fs");
const path = require("path");
const crypto = require("crypto");
const AdmZip = require("adm-zip");

const platform = require("./platform");

async function fetchBuffer(url, { allowNotFound = false } = {}) {
  if (typeof fetch !== "function") {
    throw new Error(
      "Global fetch is unavailable. StemCode's npm package requires Node.js 18 or newer."
    );
  }

  const response = await fetch(url, {
    redirect: "follow",
    headers: { "User-Agent": `${platform.APP_NAME}-npm-installer` },
  });

  if (response.status === 404 && allowNotFound) {
    return null;
  }

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

// Fetches and SHA256-verifies the release archive for a single tag.
// Returns the archive Buffer, or null when the asset is missing (HTTP 404).
async function downloadForTag(candidateTag, asset, log) {
  const base = platform.baseDownloadUrl(candidateTag);
  const assetUrl = `${base}/${asset}`;
  const checksumsUrl = `${base}/${platform.CHECKSUMS_NAME}`;

  log(`Downloading ${asset} (${candidateTag})...`);
  const archiveBuffer = await fetchBuffer(assetUrl, { allowNotFound: true });
  if (!archiveBuffer) {
    return null;
  }

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

  return archiveBuffer;
}

// Ensures the platform binary is present in vendor/. Returns the absolute path.
// `onDownloaded` is awaited only when a fresh (non-update) binary is fetched, so
// callers can record an anonymous install event exactly once per real install.
async function ensureBinary(options = {}) {
  const { force = false, log = () => {}, tag, onDownloaded } = options;

  const binaryPath = platform.installedBinaryPath();
  if (!force && fs.existsSync(binaryPath)) {
    return binaryPath;
  }

  const rid = platform.resolveRid();
  const asset = platform.assetName(rid);
  const resolvedTag = tag && tag.trim()
    ? tag.trim()
    : platform.resolveTag();

  // GitHub release tags have shipped under both casings ("v1.1.10" and
  // "V1.1.10"). Try the resolved tag first, then its alternate-case variant,
  // so installs and updates succeed regardless of how the release was tagged.
  const candidateTags = [resolvedTag];
  const alternate = platform.alternateTag(resolvedTag);
  if (alternate && alternate !== resolvedTag) {
    candidateTags.push(alternate);
  }

  let archiveBuffer = null;
  let usedTag = null;
  let lastError = null;
  for (const candidateTag of candidateTags) {
    try {
      const buffer = await downloadForTag(candidateTag, asset, log);
      if (buffer) {
        archiveBuffer = buffer;
        usedTag = candidateTag;
        break;
      }
    } catch (err) {
      // A 404 under one casing is the common case and simply means "try the
      // other casing". Other errors (checksum mismatch, network) are recorded
      // so they can be surfaced if no candidate succeeds.
      lastError = err;
    }
  }

  if (!archiveBuffer) {
    if (lastError) {
      throw lastError;
    }
    throw new Error(
      `Could not find release asset ${asset} for tag ${resolvedTag}` +
        (alternate && alternate !== resolvedTag ? ` or ${alternate}` : "") +
        "."
    );
  }

  log("Extracting StemCode CLI...");
  // Extract to a temp file first, then rename so concurrent runs never observe
  // a partially written executable.
  const tempPath = path.join(
    platform.vendorDir(),
    `.${platform.executableFileName()}.${process.pid}.tmp`
  );
  extractExecutable(archiveBuffer, tempPath);
  fs.renameSync(tempPath, binaryPath);

  log(`Installed StemCode CLI to ${binaryPath} (${usedTag}).`);

  // Fire once per genuine install. Updates pass force=true and are intentionally
  // excluded so they are not counted as new installs.
  if (!force && typeof onDownloaded === "function") {
    try {
      await onDownloaded();
    } catch {
      // Telemetry must never affect installation.
    }
  }

  return binaryPath;
}

module.exports = { ensureBinary, parseExpectedChecksum };

// Allow `node scripts/download.js` for manual/forced reinstall.
if (require.main === module) {
  ensureBinary({ force: true, log: (m) => console.error(`[stemcode] ${m}`) }).catch((err) => {
    console.error(`[stemcode] ${err.message}`);
    process.exit(1);
  });
}
