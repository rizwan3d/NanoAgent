# nanoai-cli

The NanoAgent CLI (`nanoai`) — terminal UI, ACP server, and automation-friendly agent.

This package is a thin installer: on install (or on first run) it downloads the
matching self-contained NanoAgent CLI binary from the project's
[GitHub Releases](https://github.com/rizwan3d/NanoAgent/releases) and verifies it
against the published `SHA256SUMS`. No build toolchain is required.

## Install

```bash
npm install -g nanoai-cli
# or
bun add -g nanoai-cli
# or
pnpm add -g nanoai-cli
```

Then run:

```bash
nanoai
```

> **bun note:** bun skips `postinstall` scripts by default, so the binary is
> downloaded automatically the first time you run `nanoai`. To download eagerly,
> run `bunx nanoai-cli --version` once after installing.

## Supported platforms

| OS      | Architecture |
| ------- | ------------ |
| Windows | x64          |
| macOS   | x64, arm64   |
| Linux   | x64, arm64   |

## Environment variables

| Variable                  | Purpose                                                        |
| ------------------------- | -------------------------------------------------------------- |
| `NANOAGENT_SKIP_DOWNLOAD` | Set to `1` to skip the postinstall download (fetched on run).  |
| `NANOAGENT_CLI_TAG`       | Override the release tag to download (default `v<version>`).   |
| `NANOAGENT_CLI_BASE_URL`  | Override the release asset base URL (mirrors, testing).        |

## Reinstalling the binary

```bash
node node_modules/nanoagent/scripts/download.js
```

## License

Apache-2.0
