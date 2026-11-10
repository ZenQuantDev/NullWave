## Plugins (Current State)

Third-party plugin loading is **not enabled** in this release. The plugin contract (`NullWave.Plugins.Contracts`) and discovery infrastructure exist, but:

- `PluginDiscovery` is not called at startup
- The Store tab in Settings is a non-functional preview
- No external plugins are loaded or executed

### Planned (Not Yet Implemented)

When plugin support becomes active, it will include:

- **Signed manifests**: Plugin authors sign their `plugin.json` with Ed25519 keys
- **Hash verification**: NullWave verifies SHA-256 hashes before loading any DLL
- **Permission prompts**: Users approve each plugin's requested capabilities on first install
- **Curated registry**: Initially, only plugins from an official registry will be installable
- **Isolated execution**: Plugins run in separate `AssemblyLoadContext` instances

**Important**: Even with isolation, plugins will run in-process and have the same filesystem/network access as NullWave itself. Only install plugins you trust.

### Current Plugin Infrastructure

The following components exist but are inactive:

- `NullWave.Plugins.Contracts`: Public SDK for plugin authors (IPlugin, IPluginHost, PluginManifest)
- `PluginDiscovery`: Can load plugins from `plugins/` directory using `AssemblyLoadContext`
- `PluginHost`: Implements IPluginHost with permission-checked API key access
- `NullWave.Plugins.OpenWeather`: Reference plugin demonstrating the contract

None of these are invoked during normal operation in this release.

### Layer 1: Manifest Validation
- All plugins must declare their permissions in `plugin.json`
- Permissions are reviewed during store submission
- Users are prompted to grant permissions on first install

### Layer 2: Runtime Isolation
- **First-party plugins (C#):** Loaded in separate `AssemblyLoadContext` for dependency isolation
- **Community plugins (JavaScript):** Executed in Jint sandbox with no CLR access
- Plugins cannot directly access NullWave internals; they use the `IPluginHost` API

### Layer 3: API Key Protection
- Plugins request API keys via `IPluginHost.GetApiKey(keyName)`
- Access is logged and can be revoked by the user in Settings → API Keys → Plugin Permissions
- Plugins never see the raw key; they receive a scoped proxy

### Layer 4: Automated Scanning
- All store submissions are scanned for:
  - Known malware signatures (ClamAV, YARA)
  - Vulnerable dependencies (NuGet audit)
  - Suspicious network patterns (static analysis)
  - Hardcoded secrets (regex patterns)

### Reporting Security Issues
If you discover a security vulnerability in a plugin:
1. **Do not** open a public GitHub issue
2. Email security@zenquant.dev with:
   - Plugin ID and version
   - Description of the vulnerability
   - Steps to reproduce
3. We will contact the plugin author and remove the plugin if necessary
4. You will be credited in our security advisories (unless you request anonymity)