## Plugins

The app loads bundled Radio Browser and OpenWeather plugins from the application directory
and checks the user's `<data directory>/plugins/` folder first. A user-installed plugin with
the same manifest ID takes precedence over the bundled copy.

Plugins are loaded in separate collectible `AssemblyLoadContext` instances, but this is
**not a security sandbox**. Plugin code runs in-process and can use the same OS identity and
process privileges as NullWave. Manifests are not signed or hash-verified, and capabilities
are not approved through an install-time permission prompt. The host checks declared
permissions for host-mediated API-key access, but those checks cannot constrain arbitrary
code running in-process. Initialization timeouts request cancellation but cannot forcibly
stop a plugin that ignores its cancellation token.

Only install plugins from sources you trust. Do not treat `AssemblyLoadContext` isolation or
manifest permission declarations as protection against a malicious plugin.

### Reporting Security Issues

If you discover a security vulnerability in NullWave:

1. **Do not** open a public GitHub issue
2. Use [GitHub's private vulnerability reporting](https://github.com/ZenQuantDev/NullWave/security/advisories/new), if it is enabled for this repository
3. Include: affected version, description of the vulnerability, steps to reproduce
4. Reports will be reviewed on a best-effort basis; no response-time guarantee is made
5. You will be credited in our security advisories (unless you request anonymity)

The private reporting form is controlled in the repository's GitHub security settings. If it
is unavailable, open an issue asking for a private reporting channel without posting
vulnerability details or exploit steps publicly.