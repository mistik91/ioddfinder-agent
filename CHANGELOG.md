# Changelog

## 0.1.1

- Added a compatibility manifest and bundled MCP launchers for Codex versions that require the `.codex-plugin` layout.
- Updated the companion skill to recover when an exact manufacturer alias returns no matches.
- Documented manual agent checks for ambiguous models, exact matches and saved package reuse.

## 0.1.0

First public release for finding and downloading IODDs through a CLI or local MCP server.

- Live paginated catalog search, suggestions, manufacturer listing, product details and version history.
- Bounded live viewer inspection without a local catalog.
- Exact package downloads with optional hash-verified local reuse and manual cleanup.
- Standalone native executable, checksummed release archives and installation documentation.
- Portable Agent Plugins 1.0 package and a standalone CLI companion skill.
- Automated unit, protocol, packaging and native executable checks.

Downloaded IODD XML parsing is intentionally deferred. Historical packages can be downloaded; live viewer inspection cannot select an exact historical IODD release.
