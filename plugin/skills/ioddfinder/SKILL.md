---
name: ioddfinder
description: Search the live IO-Link IODDfinder catalog, resolve device descriptions and versions, and download IODD ZIPs using the iodd CLI or its MCP tools. Use for finding manufacturer IODDs and managing this tool's saved downloads.
---

Use IODDfinder Agent for live catalog access. No local catalog or initial synchronization is needed.

## Choose an interface

Prefer the connected `iodd_search`, `iodd_details`, `iodd_versions` and `iodd_download` MCP tools when available. Otherwise run the installed `iodd` CLI. CLI results are JSON on stdout; failures use stderr and nonzero exit codes. Use `iodd --help` or the subcommand's help for options.

If `iodd` is absent from PATH, a platform release plugin may bundle `bin/iodd.exe` (Windows) or `bin/iodd` relative to the plugin root, two directories above this skill folder. Use that absolute executable path. If neither is present, explain that the CLI must be installed from the project's GitHub releases; do not pretend the tool ran.

## Find the correct package

- Search by manufacturer/product/article, or combine `vendorId` and `deviceId` when known. Device IDs alone are not globally unique. Article numbers can identify multiple variants or revisions.
- Use exact text matching when resolving a known model. Keep user-supplied values as literal arguments; do not interpolate them into executable shell code.
- Follow pagination when the needed entry is not in the returned page. An empty page is not proof that a different spelling or filter has no matches.
- Use details to confirm the model and description. If materially different candidates remain, show the relevant differences and resolve the ambiguity before selecting a package.
- Use versions when a release is specified. `ioLinkRev` (such as 1.1) is different from an IODD release string. Version-history results are not guaranteed newest-first; do not automatically select the first row.
- Download by the selected `vendorId` and exact `ioddId`. Return those IDs, the model, release, local ZIP path and whether it came from the network or saved library.

CLI examples:

```text
iodd search --vendor-id 310 --device-id 706 --product TN2405 --exact --revision 1.1
iodd search --vendor "ifm electronic gmbh" --article TN2405 --exact --size 10 --page 0
iodd details --variant-id 1350
iodd versions --vendor-id 310 --device-id 706 --product TN2405
iodd download --vendor-id 310 --iodd-id 399
```

## Inspection and saved files

`iodd inspect --vendor-id 310 --device-id 706` lists live viewer sections. Add `--section`, `--query`, `--limit` and `--offset` for bounded results. The viewer chooses its release upstream; do not present those results as verified against an exact historical package.

For questions about an exact downloaded release, use the agent's available ZIP/XML tools on the original package. Account for translated text files and referenced identifiers. This client does not parse downloaded IODD XML or certify conformity.

Saved downloads are optional. The default download can reuse the same hash-verified package; live search still goes online. `--refresh` forces downloading again. `--no-save` leaves no library copy; add `--output PATH` to export a ZIP outside the managed library. Output files must be new. A path returned by a local MCP server belongs to the server's filesystem; a remote agent may not be able to open it.

Use `iodd library list`, `iodd library remove --vendor-id ... --iodd-id ...`, or `iodd library clear` only when relevant to the user's request. Clearing downloads does not remove the remote catalog or exported files. Respect a user-selected library directory (`--library` or `IODD_LIBRARY`). Report upstream errors instead of silently claiming no matches or a successful download.
