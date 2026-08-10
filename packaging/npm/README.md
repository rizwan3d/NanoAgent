# stemcode

`stemcode` installs the StemCode CLI as the `stemcode` command.

StemCode is a local AI coding agent for terminal workflows, ACP-compatible editors, and automation. This npm package is a thin installer: it downloads the matching self-contained StemCode release for your platform, verifies it against the published `SHA256SUMS`, and launches it without requiring a .NET toolchain.

## Why use this package

- Install StemCode with `npm`, `pnpm`, or `bun`.
- Download the correct native binary for the current platform automatically.
- Verify release archives before extraction with published SHA-256 checksums.
- Recover automatically on first run if `postinstall` was skipped or the binary is missing.
- Toggle a built-in git sidebar (F7) showing branch, queued prompts, recent commits, and changed files as `filename (relative/path)` — click a file to open it in your editor, and use `Alt+S`/`Alt+P`/`Alt+O`/`Alt+D`/`Alt+C`/`Alt+B` for stage, pull, push, discard, commit, and branch actions.

## Install

```bash
npm install -g stemcode
# or
pnpm add -g stemcode
# or
bun add -g stemcode
```

Start StemCode:

```bash
stemcode
```

If you want a quick non-interactive smoke test after install:

```bash
stemcode --version
```

## How installation works

On install, the package tries to:

1. Resolve the correct release asset for the current OS and CPU architecture.
2. Download the matching `StemCode.CLI-<rid>.zip` archive from GitHub Releases.
3. Download `SHA256SUMS` from the same release.
4. Verify the archive checksum before extraction.
5. Extract the StemCode binary into the package's `vendor/` directory.

If the download is skipped or fails during `postinstall`, installation still succeeds. The launcher downloads the binary automatically the first time you run `stemcode`.

## bun note

`bun add` skips `postinstall` scripts by default, so the binary is usually downloaded on first launch instead of during installation.

To fetch it eagerly after installing with bun, run:

```bash
bunx stemcode --version
```

## Supported platforms

| OS | Architectures |
| --- | --- |
| Windows | x64 |
| macOS | x64, arm64 |
| Linux | x64, arm64 |

## Updates

By default, the package downloads the release tag that matches the npm package version, using `v<package-version>`.

At runtime, the launcher can also check GitHub for a newer StemCode release. When running interactively, it prompts before replacing the installed binary with the latest release and then continues launch.

Skip the runtime update prompt with either:

```bash
stemcode --no-update-check
```

or:

```bash
STEMCODE_SKIP_UPDATE_CHECK=1 stemcode
```

## Environment variables

| Variable | Purpose |
| --- | --- |
| `STEMCODE_SKIP_DOWNLOAD` | Set to `1` to skip the install-time download. The binary will still be fetched on first run. |
| `STEMCODE_TELEMETRY_DISABLED` | Set to `1` to opt out of the anonymous `cli installed` analytics event. `DO_NOT_TRACK=1` is also honored. |
| `STEMCODE_SKIP_UPDATE_CHECK` | Set to `1` to disable the runtime check for newer GitHub releases. |
| `STEMCODE_CLI_TAG` | Override the release tag to download, such as `v1.2.3`. |
| `STEMCODE_CLI_VERSION` | Override the version used to derive the default release tag. |
| `STEMCODE_CLI_BASE_URL` | Override the release asset base URL for mirrors, testing, or private distribution. |

## Manual reinstall

If you need to force a fresh binary download for a local install, run:

```bash
node ./node_modules/stemcode/scripts/download.js
```

If the package was installed globally, reinstalling the package is usually the simplest way to refresh the bundled launcher files.

## Learn more

- Product overview: [StemCode README](https://github.com/rizwan3d/StemCode#readme)
- Full documentation: [docs/documentation.md](https://github.com/rizwan3d/StemCode/blob/master/docs/documentation.md)
- Releases: [GitHub Releases](https://github.com/rizwan3d/StemCode/releases)
- Issues: [GitHub Issues](https://github.com/rizwan3d/StemCode/issues)

## License

Apache-2.0
