"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("fs");
const os = require("os");
const path = require("path");
const crypto = require("crypto");

const download = require("../scripts/download");
const platform = require("../scripts/platform");

function sha256(buffer) {
  return crypto.createHash("sha256").update(buffer).digest("hex").toLowerCase();
}

function makeZip(assetName, contents) {
  const AdmZip = require("adm-zip");
  const zip = new AdmZip();
  zip.addFile(assetName, Buffer.from(contents));
  return zip.toBuffer();
}

function fakeResponse(body, status = 200) {
  const buffer = Buffer.isBuffer(body) ? body : Buffer.from(body);
  return {
    ok: status >= 200 && status < 400,
    status,
    statusText: status === 404 ? "Not Found" : "OK",
    async arrayBuffer() {
      return buffer.buffer.slice(buffer.byteOffset, buffer.byteOffset + buffer.byteLength);
    },
  };
}

// Serves a zip (whose checksum is embedded in the SHA256SUMS response) for each
// tag in `zipByTag`, and returns HTTP 404 for tags in `missingTags`.
function makeFetch({ missingTags, zipByTag, asset }) {
  return async (url) => {
    const match = url.match(/\/releases\/download\/([^/]+)\/(.+)$/);
    assert.ok(match, `unexpected request url: ${url}`);
    const [, tag, file] = match;

    if (missingTags.has(tag)) {
      return fakeResponse("", 404);
    }

    const zipBuffer = zipByTag.get(tag);
    assert.ok(zipBuffer, `no prepared zip for tag ${tag}`);

    if (file === platform.CHECKSUMS_NAME) {
      return fakeResponse(`${sha256(zipBuffer)}  ${asset}\n`);
    }
    return fakeResponse(zipBuffer);
  };
}

async function withDownloadOverrides(overrides, fn) {
  const originals = {
    fetch: global.fetch,
    installedBinaryPath: platform.installedBinaryPath,
    vendorDir: platform.vendorDir,
    cliVersion: process.env.STEMCODE_CLI_VERSION,
  };
  try {
    global.fetch = overrides.fetch;
    platform.installedBinaryPath = overrides.installedBinaryPath;
    platform.vendorDir = overrides.vendorDir;
    if (overrides.cliVersion === undefined) {
      delete process.env.STEMCODE_CLI_VERSION;
    } else {
      process.env.STEMCODE_CLI_VERSION = overrides.cliVersion;
    }
    return await fn();
  } finally {
    global.fetch = originals.fetch;
    platform.installedBinaryPath = originals.installedBinaryPath;
    platform.vendorDir = originals.vendorDir;
    if (originals.cliVersion === undefined) {
      delete process.env.STEMCODE_CLI_VERSION;
    } else {
      process.env.STEMCODE_CLI_VERSION = originals.cliVersion;
    }
  }
}

test("ensureBinary falls back to the alternate-case tag when the primary 404s", async () => {
  const asset = platform.assetName(platform.resolveRid());
  const executable = platform.executableFileName();
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "stemcode-dl-"));

  try {
    const zipBuffer = makeZip(executable, "fake-stemcode-binary");
    const fetch = makeFetch({
      missingTags: new Set(["V1.1.10"]),
      zipByTag: new Map([["v1.1.10", zipBuffer]]),
      asset,
    });

    const result = await withDownloadOverrides(
      {
        fetch,
        installedBinaryPath: () => path.join(tempDir, executable),
        vendorDir: () => tempDir,
        cliVersion: "1.1.10", // resolveTag() => V1.1.10 (primary)
      },
      () => download.ensureBinary({ force: true, log: () => {} })
    );

    assert.equal(result, path.join(tempDir, executable));
    assert.ok(fs.existsSync(result), "binary should be extracted to disk");
  } finally {
    fs.rmSync(tempDir, { recursive: true, force: true });
  }
});

test("ensureBinary uses the primary tag when it resolves successfully", async () => {
  const asset = platform.assetName(platform.resolveRid());
  const executable = platform.executableFileName();
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "stemcode-dl-"));

  try {
    const zipBuffer = makeZip(executable, "fake-stemcode-binary-primary");
    const fetch = makeFetch({
      missingTags: new Set(),
      zipByTag: new Map([["V1.1.10", zipBuffer]]),
      asset,
    });

    const result = await withDownloadOverrides(
      {
        fetch,
        installedBinaryPath: () => path.join(tempDir, executable),
        vendorDir: () => tempDir,
        cliVersion: "1.1.10", // resolveTag() => V1.1.10 (primary)
      },
      () => download.ensureBinary({ force: true, log: () => {} })
    );

    assert.equal(result, path.join(tempDir, executable));
    assert.ok(fs.existsSync(result), "binary should be extracted to disk");
  } finally {
    fs.rmSync(tempDir, { recursive: true, force: true });
  }
});
