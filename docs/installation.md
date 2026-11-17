# Installation

Download assets from [GitHub Releases](https://github.com/mistik91/ioddfinder-agent/releases/latest). No .NET runtime, Python, Node.js, account or catalog synchronization is required to run the native CLI.

## Choose a package

| Asset | Use |
|---|---|
| `ioddfinder-agent-VERSION-win-x64.zip` | Windows x64 CLI and docs |
| `ioddfinder-agent-VERSION-linux-x64.zip` | Linux x64 CLI and docs |
| `ioddfinder-agent-plugin-VERSION-PLATFORM.zip` | Agent Plugins package with a native executable, MCP configuration and skill |
| `ioddfinder-skill-VERSION.zip` | Standalone companion skill; CLI installed separately |
| `SHA256SUMS.txt` | SHA-256 hashes of release archives |

The initial native targets are Windows x64 and Linux x64. Linux releases are built on Ubuntu 24.04; other distributions need compatible glibc/system libraries. macOS and ARM prebuilt packages are not included in this release. Source builds are possible but are not a claim that those platforms have been validated.

## Windows

1. Download the `win-x64` CLI ZIP and `SHA256SUMS.txt` from the same release.
2. Compare `(Get-FileHash .\ioddfinder-agent-0.1.0-win-x64.zip -Algorithm SHA256).Hash` with the archive's line in `SHA256SUMS.txt`.
3. Extract the ZIP into a dedicated folder such as `%LOCALAPPDATA%\Programs\IoddFinder`.
4. Run it using its full path:

```powershell
$iodd = Join-Path $env:LOCALAPPDATA 'Programs\IoddFinder\iodd.exe'
& $iodd --version
& $iodd search --product TN2405
```

Optionally add that folder to your user PATH through Windows Environment Variables, then reopen your terminal and agent application. You can then use `iodd` directly. A GUI application started before a PATH change may still use the old PATH; an absolute executable path avoids that issue.

The initial executable is not code-signed. Checksums detect archive changes but are not an independent publisher signature. Windows may show an unknown-publisher prompt.

## Linux

Download the `linux-x64` CLI ZIP and `SHA256SUMS.txt` into the same empty directory:

```sh
sha256sum --check --ignore-missing SHA256SUMS.txt
mkdir -p "$HOME/.local/opt/ioddfinder-agent" "$HOME/.local/bin"
unzip ioddfinder-agent-0.1.0-linux-x64.zip -d "$HOME/.local/opt/ioddfinder-agent"
chmod +x "$HOME/.local/opt/ioddfinder-agent/iodd"
ln -s "$HOME/.local/opt/ioddfinder-agent/iodd" "$HOME/.local/bin/iodd"
"$HOME/.local/bin/iodd" --version
```

Ensure `~/.local/bin` is on PATH if using `iodd` without a full path. The commands assume a first installation; an existing symlink must be handled deliberately rather than overwritten.

## Update and uninstall

Close running MCP instances, verify the new archive, and replace the executable/docs in the installation folder. Keep the library directory separate from the program files. Remove the installation folder or symlink and agent configuration to uninstall. Saved downloads remain until you run `iodd library clear` or explicitly remove their directory.

Default downloads are stored under your local application-data directory in `IoddFinder/library`. Override with `--library PATH` or `IODD_LIBRARY`. Search works before that directory exists and after its managed downloads are cleared.

## Troubleshooting

- **Command not found:** use the full executable path; reopen the agent host after changing PATH.
- **Wrong architecture / cannot execute:** install the correct native target; Linux may need `chmod +x` after ZIP extraction.
- **Permission denied:** choose a writable library using `--library`; program files can stay read-only.
- **HTTP 429 / temporary upstream failure:** the client performs at most two bounded retries. Long upstream retry delays return an error; wait before trying again.
- **MCP appears to wait:** stdio servers wait for a client on stdin. Use `--help` for an interactive command or configure a client as described in [Agent setup](agents.md).
