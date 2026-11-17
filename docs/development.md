# Development and release

Install the SDK specified by `global.json` (.NET 10.0.404, permitting its patch updates). Native AOT builds also need the platform's C/C++ toolchain. Windows builds use Visual Studio C++ Build Tools; Ubuntu builds use clang and zlib development headers. Normal managed builds do not need a native toolchain.

```sh
dotnet build IoddFinder.sln -c Release
dotnet test IoddFinder.sln -c Release --no-build
python tests/smoke.py -- dotnet src/IoddFinder.Cli/bin/Release/net10.0/iodd.dll
dotnet run --project src/IoddFinder.Cli -- search --product TN2405
```

Python 3.10+ is used only for development scripts. Plugin validation additionally needs `jsonschema==4.25.1` (`python -m pip install jsonschema==4.25.1`). The vendored JSON schemas in `tests/schemas` come from the Agent Plugins 1.0 canonical schema URLs; validation does not fetch them during CI.

```sh
python scripts/validate_plugin.py
dotnet publish src/IoddFinder.Cli/IoddFinder.Cli.csproj -c Release -r win-x64 -p:PublishAot=true -o artifacts/native
python scripts/package.py --rid win-x64 --publish-dir artifacts/native
python scripts/verify_archives.py --rid win-x64
```

Use `linux-x64` on the Linux build host. Archive verification checks SHA-256, ZIP integrity, explicit content boundaries, plugin schema and paths, and a real MCP handshake/tool invocation through the bundled executable. `tests/smoke.py --live -- EXECUTABLE` adds a bounded live lookup/viewer read and one package download; CI uses fake upstream responses and does not contact IODDfinder.

## Architecture

- `IoddFinder.Core` owns the upstream client, viewer projection and optional saved-download library, using .NET libraries only.
- `IoddFinder.Cli` owns command parsing and the official MCP SDK adapter. Both interfaces call the same core.
- Agent instructions live in `plugin/skills/ioddfinder`; they do not implement transport or download logic.

CLI output and MCP tool inputs/results are the external contracts. Preserve their meanings to keep a future implementation in another language practical. In particular, IO-Link revision is not an IODD release, catalog rows are not necessarily unique packages, and a local saved package never replaces a live search for the latest version.

## Release procedure

1. Update the project `Version`, `plugin/plugin.json`, changelog and versioned installation examples together.
2. Run tests, native publishing and archive verification. Check that changes contain no manufacturer downloads, research snapshots, credentials or personal machine paths.
3. Commit, then create and push `vVERSION` after CI passes.
4. The Release workflow builds Windows x64 and Linux x64 native packages. It tests both platforms and their bundled plugin binaries, then publishes the stable GitHub release only after both build jobs succeed. It creates five ZIPs (two CLI, two plugin, one standalone skill) and combined `SHA256SUMS.txt`.

Actions are pinned to commit SHAs. Application/test packages are pinned in project files; release archives include resolved dependency metadata and third-party notices. Changes to an already published release should use a new patch version rather than replacing assets silently.

## Contributing

Use GitHub issues for reproducible bugs, including version, OS, command/tool arguments and upstream device IDs. Keep fixes narrow and add tests for observable failures. Keep live integration tests opt-in to avoid unnecessary load on IODDfinder. IODD XML parsing and automatic library eviction are outside the initial release scope.
