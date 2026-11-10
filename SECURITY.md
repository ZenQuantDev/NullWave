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

### Reporting Security Issues

If you discover a security vulnerability in NullWave:

1. **Do not** open a public GitHub issue
2. Use [GitHub's private vulnerability reporting](https://github.com/ZenQuantDev/NullWave/security/advisories/new)
3. Include: affected version, description of the vulnerability, steps to reproduce
4. We will acknowledge receipt within 48 hours and provide a detailed response within 5 business days
5. You will be credited in our security advisories (unless you request anonymity)