## Plugin Security Model

NullWave uses a multi-layer security model for plugins:

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