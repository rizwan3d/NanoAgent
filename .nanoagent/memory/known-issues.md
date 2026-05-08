# Known Issues

## Active Issues

### 1. Desktop Windows Installer Requires Inno Setup
**Area**: Release pipeline (`release.yml`, `release-signing.yml`)  
**Issue**: The Windows desktop installer is built using Inno Setup, installed via Chocolatey during CI. If Chocolatey is unavailable or Inno Setup installation fails, the installer step fails.  
**Workaround**: Manual packaging using `Compress-Archive` (fallback zip is always produced).  
**Status**: By design — Inno Setup provides a proper Windows installer experience.

### 2. AOT Constraints on Dynamic Features
**Area**: CLI (Native AOT)  
**Issue**: Native AOT compilation prevents runtime code generation, making certain patterns (e.g., `Regex` source generation, `JsonSerializer` without source context, dynamic assembly loading) unavailable.  
**Impact**: New features that rely on reflection or runtime compilation cannot be added without source-generated alternatives.  
**Workaround**: Use source generators (`JsonSerializerContext`, `GeneratedRegexAttribute`) or precompile needed artifacts.

### 3. macOS Code Signing and Notarization Complexity
**Area**: Release pipeline (`release-signing.yml`)  
**Issue**: macOS signing requires a Developer ID certificate, keychain setup, `codesign` with hardened runtime, and notarization via `notarytool`. Each step is fragile and depends on CI runner availability and secrets being configured correctly.  
**Impact**: macOS release builds may fail silently if signing infrastructure is misconfigured.  
**Workaround**: Manual codesign verification before release; unsigned builds are still functionally usable.

### 4. JetBrains Plugin Targets Specific IDE Versions
**Area**: `NanoAgent.JetBrains/build.gradle.kts`  
**Issue**: The plugin targets IntelliJ IC 2024.1.7 with `sinceBuild` and `untilBuild` properties from `gradle.properties`. It may not load in newer or older IDE versions.  
**Impact**: Users on IDEs outside the supported range cannot install the plugin.  
**Workaround**: Update `pluginSinceBuild` / `pluginUntilBuild` in `gradle.properties` for each release cycle.

### 5. CLI Terminal Rendering on Non-ANSI Terminals
**Area**: `NanoAgent.CLI/Program.cs`  
**Issue**: The CLI uses extensive ANSI escape sequences (alternate screen, bracketed paste, mouse tracking). On terminals that don't support these (e.g., Windows Console without Virtual Terminal), the UI may render incorrectly.  
**Workaround**: Use Windows Terminal, Windows PowerShell 7+, or a modern terminal emulator.

## Resolved Issues

*(None documented yet — this section can be populated as fixes are applied.)*

## Risky Areas

### 1. Locked Mode Dependencies
**Area**: CI pipeline, `Directory.Build.props`  
**Risk**: `--locked-mode` in `dotnet restore` requires exact package version matches in `packages.lock.json`. If a transitive dependency updates and the lock file is stale, CI fails.  
**Mitigation**: Lock files are checked in and updated manually/PR when dependencies change.

### 2. Long-Running Agent Turns
**Area**: `AgentConversationPipeline`, `MaxToolRoundsPerTurn`  
**Risk**: If `MaxToolRoundsPerTurn` is set too high, a single turn could make many API calls, incurring significant cost and latency.  
**Mitigation**: The setting is configurable in `ConversationSettings` with a reasonable default.
