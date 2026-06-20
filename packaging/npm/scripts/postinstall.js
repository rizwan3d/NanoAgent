"use strict";

// Runs after `npm install`/`pnpm install`. Best-effort: if the download fails
// (offline, proxy, rate limit) we do NOT fail the install. The binary is fetched
// lazily on first `nanoai` invocation instead. This also keeps `bun install`
// working, since bun skips postinstall scripts by default.

// Skip in CI/build contexts that set npm_config_global=false dev installs of the
// monorepo, or when the user explicitly opts out.
if (process.env.NANOAGENT_SKIP_DOWNLOAD === "1") {
  process.exit(0);
}

const { ensureBinary } = require("./download");
const { trackInstall } = require("./telemetry");

ensureBinary({
  log: (m) => console.error(`[nanoagent] ${m}`),
  onDownloaded: () => trackInstall(),
})
  .then(() => {
    console.error("[nanoagent] Ready. Run `nanoai` to start.");
  })
  .catch((err) => {
    console.error(`[nanoagent] Could not pre-download the CLI binary: ${err.message}`);
    console.error("[nanoagent] It will be downloaded automatically the first time you run `nanoai`.");
    // Intentionally exit 0 so install succeeds.
  });
