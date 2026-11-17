# Command guide

Run `iodd --help` or `iodd COMMAND --help` for the full option list. Successful commands return JSON on stdout. Runtime failures return JSON on stderr with exit code 1; argument syntax failures use the command parser's diagnostics and nonzero exit status. MCP reserves stdout for protocol messages.

## Live search

```sh
iodd search --product TN2405
iodd suggest --text TN2405
iodd vendors
iodd search --vendor "ifm electronic gmbh" --article TN2405 --exact
iodd search --vendor-id 310 --device-id 706 --product TN2405 --exact --revision 1.1
iodd search --vendor "Balluff GmbH" --exact --page 0 --size 10 --sort productName,ASC
iodd details --variant-id 1350
iodd versions --vendor-id 310 --device-id 1407 --product "AL2201 Cyclic" --revision 1.1
```

`--page` starts at zero and `--size` is 1..100. Search preserves upstream pagination (`totalElements`, `totalPages`, `first`, `last`) and catalog rows. `--exact` applies exact matching to text filters. Sorting supports `vendorName`, `productName`, `productId`, `deviceId`, `ioLinkRev`, followed by `,ASC` or `,DESC`. Use `--all-versions` or the `versions` command for historical entries.

The service groups related products and revisions: one article number may return multiple rows, and several variants may share one IODD. Confirm vendor/device identity and select the returned `ioddId` deliberately. Do not assume a version-history response is newest-first or that an IO-Link revision is an IODD release version.

## Downloads and optional library

```sh
iodd download --vendor-id 310 --iodd-id 399
iodd download --vendor-id 310 --iodd-id 399 --refresh
iodd download --vendor-id 310 --iodd-id 399 --no-save --output ./TN2405.zip
iodd download --vendor-id 310 --iodd-id 399 --no-save
iodd library list
iodd library remove --vendor-id 310 --iodd-id 399
iodd library clear
```

A download selects an exact IODD ID, not “latest”. Default behavior saves and can reuse that exact package after checking SHA-256. `--refresh` forces network retrieval. `--no-save` ignores the library and discards the fetched bytes after reporting metadata unless an output file is supplied. `--output` exports an original ZIP to a new file outside the managed library; it never overwrites a file.

Results contain `package` (identity, SHA-256, bytes, original download timestamp and ZIP filenames), `source` (`network` or `library`) and `path` when a file is retained. If both a saved copy and an export are requested, `path` identifies the saved copy; the export is at the explicitly supplied output path.

The global `--library PATH` option, or `IODD_LIBRARY`, selects the saved-download directory. Otherwise it is the user's local application-data directory under `IoddFinder/library`. This is a reserved tool-managed directory, not a general downloads folder. Listing an absent library creates no state. Writes use a coordination lock and temporary-file renames. Corrupt saved ZIP bytes trigger a fresh download. The library is intended for a normal local filesystem.

Cleanup removes only managed package/metadata filenames and leaves the coordination lock. It does not touch exports, unrelated files or the remote catalog. Search works with an empty library. Automatic age/size eviction is not included in v0.1.0. Network errors remain errors; there is no silent offline fallback.

## Optional live viewer inspection

```sh
iodd inspect --vendor-id 310 --device-id 706
iodd inspect --vendor-id 310 --device-id 706 --section Parameter --limit 10
iodd inspect --vendor-id 310 --device-id 706 --query temperature --limit 5
```

Omit section/query to list available section names and IDs. Supply a section name or ID to inspect it; `--query` filters names, descriptions, IDs and indexes. Nested menu items are flattened. Responses include `total`, `offset`, and `nextOffset`; pass `--offset` to continue. The limit is 1..100.

The viewer is live and transient. `--revision 1.1` means IO-Link revision; upstream controls the IODD release being shown. Use downloaded ZIPs and separate XML tools for exact historical-release questions.

## Network limits

Requests have a 30-second deadline. Temporary transport errors and HTTP 429/502/503/504 can be retried at most twice. Short `Retry-After` values are respected; values over five seconds return an error rather than retrying early. Authorization and not-found failures are not retried. JSON responses are limited to 8 MiB and ZIP downloads to 32 MiB, with entry and declared expanded-size checks. The client does not bulk-crawl the catalog.
