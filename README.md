# IODDfinder Agent

[![CI](https://github.com/mistik91/ioddfinder-agent/actions/workflows/ci.yml/badge.svg)](https://github.com/mistik91/ioddfinder-agent/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/mistik91/ioddfinder-agent)](https://github.com/mistik91/ioddfinder-agent/releases/latest)

A small, online-first CLI and local MCP server for the IO-Link IODDfinder catalog. Search live, select a device and release, and download its original IODD ZIP. No local catalog, account setup or initial synchronization is required for the public endpoints used here.

**[Download a release](https://github.com/mistik91/ioddfinder-agent/releases/latest)** · **[Install](docs/installation.md)** · **[Agent/MCP/plugin setup](docs/agents.md)** · **[Command guide](docs/usage.md)**

```sh
iodd search --product TN2405
iodd search --vendor-id 310 --device-id 706 --product TN2405 --exact --revision 1.1
iodd details --variant-id 1350
iodd download --vendor-id 310 --iodd-id 399
iodd library list
iodd mcp
```

## What it does

- Searches the live catalog with manufacturer, article, model and device-identity filters, pagination and sorting.
- Retrieves device details and available IODD versions.
- Downloads an exact package, optionally reusing its hash-verified saved copy.
- Lists, removes or clears managed downloads without affecting exported files.
- Exposes the same functionality through ten MCP tools over stdio.
- Includes a portable Agent Plugins 1.0 package and a standalone CLI companion skill.
- Offers bounded live IODDviewer inspection for convenience.

The native executable runs without installing .NET. Initial release targets are Windows x64 and Linux x64; checksummed CLI and bundled-plugin archives are built and tested in GitHub Actions. The Windows executable is approximately 16 MiB before ZIP compression.

## Empty library by design

Search, details and viewer inspection always request the live service. Downloads are saved only when requested and can be discarded with `--no-save`. A saved package never silently becomes the answer to a request for the latest release. Clearing the library requires no rebuild or synchronization.

## Scope

Downloaded XML parsing is intentionally deferred: agents can use their existing ZIP/XML tools. Historical packages can be downloaded by exact IODD ID, but live viewer inspection cannot select an exact historical release. Package checks are not official IODD conformity or Stamp CRC certification.

This is an independent client, not an official PNO/IO-Link product. Upstream endpoints and access conditions may change; [upstream service and data](docs/upstream.md) explains the integration. The MIT license covers this software, not manufacturer IODDs.

## Development

C# / .NET 10, with one core library shared by CLI and MCP. No database, hosted service or embedded AI model. See [build, test and release instructions](docs/development.md), [changelog](CHANGELOG.md), and [third-party notices](docs/third-party.md).

```sh
dotnet build IoddFinder.sln -c Release
dotnet test IoddFinder.sln -c Release --no-build
```

Report problems through [GitHub Issues](https://github.com/mistik91/ioddfinder-agent/issues), including the application version, platform and relevant device IDs.
