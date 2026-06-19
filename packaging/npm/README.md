# nanoai-cli

`nanoai-cli` installs the NanoAgent CLI as the `nanoai` command.

NanoAgent is a local AI coding agent for terminal workflows, ACP-compatible editors, and automation. This npm package is a thin installer: it downloads the matching self-contained NanoAgent release for your platform, verifies it against the published `SHA256SUMS`, and launches it without requiring a .NET toolchain.

## Why use this package

- Install NanoAgent with `npm`, `pnpm`, or `bun`.
- Download the correct native binary for the current platform automatically.
- Verify release archives before extraction with published SHA-256 checksums.
- Recover automatically on first run if `postinstall` was skipped or the binary is missing.

## Install

```bash
npm install -g nanoai-cli
# or
pnpm add -g nanoai-cli
# or
bun add -g nanoai-cli
```

Start NanoAgent:

```bash
nanoai
```

If you want a quick non-interactive smoke test after install:

```bash
nanoai --version
```

## How installation works

On install, the package tries to:

1. Resolve the correct release asset for the current OS and CPU architecture.
2. Download the matching `NanoAgent.CLI-<rid>.zip` archive from GitHub Releases.
3. Download `SHA256SUMS` from the same release.
4. Verify the archive checksum before extraction.
5. Extract the NanoAgent binary into the package's `vendor/` directory.

If the download is skipped or fails during `postinstall`, installation still succeeds. The launcher downloads the binary automatically the first time you run `nanoai`.

## bun note

`bun add` skips `postinstall` scripts by default, so the binary is usually downloaded on first launch instead of during installation.

To fetch it eagerly after installing with bun, run:

```bash
bunx nanoai-cli --version
```

## Supported platforms

| OS | Architectures |
| --- | --- |
| Windows | x64 |
| macOS | x64, arm64 |
| Linux | x64, arm64 |

## Updates

By default, the package downloads the release tag that matches the npm package version, using `v<package-version>`.

At runtime, the launcher can also check GitHub for a newer NanoAgent release. When running interactively, it prompts before replacing the installed binary with the latest release and then continues launch.

Skip the runtime update prompt with either:

```bash
nanoai --no-update-check
```

or:

```bash
NANOAGENT_SKIP_UPDATE_CHECK=1 nanoai
```

## Environment variables

| Variable | Purpose |
| --- | --- |
| `NANOAGENT_SKIP_DOWNLOAD` | Set to `1` to skip the install-time download. The binary will still be fetched on first run. |
| `NANOAGENT_SKIP_UPDATE_CHECK` | Set to `1` to disable the runtime check for newer GitHub releases. |
| `NANOAGENT_CLI_TAG` | Override the release tag to download, such as `v1.2.3`. |
| `NANOAGENT_CLI_VERSION` | Override the version used to derive the default release tag. |
| `NANOAGENT_CLI_BASE_URL` | Override the release asset base URL for mirrors, testing, or private distribution. |

## Manual reinstall

If you need to force a fresh binary download for a local install, run:

```bash
node ./node_modules/nanoai-cli/scripts/download.js
```

If the package was installed globally, reinstalling the package is usually the simplest way to refresh the bundled launcher files.

## Learn more

- Product overview: [NanoAgent README](https://github.com/rizwan3d/NanoAgent#readme)
- Full documentation: [docs/documentation.md](https://github.com/rizwan3d/NanoAgent/blob/master/docs/documentation.md)
- Releases: [GitHub Releases](https://github.com/rizwan3d/NanoAgent/releases)
- Issues: [GitHub Issues](https://github.com/rizwan3d/NanoAgent/issues)

## License

Apache-2.0
