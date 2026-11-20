# Agent setup

There are three independent ways to use the application: direct CLI with a skill, direct stdio MCP, or a portable Agent Plugins package combining the MCP server and skill. You do not need all three.

## Direct MCP

Configure your agent to launch the installed executable with the `mcp` argument. For hosts using an `mcpServers` JSON configuration:

```json
{
  "mcpServers": {
    "ioddfinder": {
      "command": "C:\\Users\\YOUR_USER\\AppData\\Local\\Programs\\IoddFinder\\iodd.exe",
      "args": ["mcp"]
    }
  }
}
```

Use your actual absolute executable path. On Linux, use e.g. `/home/YOUR_USER/.local/opt/ioddfinder-agent/iodd`. Add `"--library", "ABSOLUTE_LIBRARY_PATH"` before `"mcp"` in `args` to choose another library.

Codex's TOML equivalent is:

```toml
[mcp_servers.ioddfinder]
command = "C:\\Users\\YOUR_USER\\AppData\\Local\\Programs\\IoddFinder\\iodd.exe"
args = ["mcp"]
```

Merge only this entry into the host's existing configuration; do not replace other servers. The local server has no listening port and needs no model API key. Its tools use the network for IODDfinder requests. Tool results are JSON text. A local file path is usable only by an agent with access to that filesystem.

Try: “Find the IODD for ifm TN2405. Confirm the vendor and device IDs, download the exact package and tell me its saved path.”

## Portable Agent Plugins 1.0

The repository also provides a `.codex-plugin/plugin.json` compatibility manifest for Codex versions that require it. The repository marketplace uses an installed `iodd` on PATH. Plugin archives built from this source launch the bundled executable in the plugin directory.

Release plugin ZIPs contain one `ioddfinder-agent/` folder with:

```text
plugin.json
mcp.json
bin/iodd.exe             # bin/iodd in the Linux archive
skills/ioddfinder/SKILL.md
skills/ioddfinder/agents/openai.yaml
```

The manifest and MCP configuration target [Agent Plugins 1.0](https://agent-plugins.org/). The release MCP command is plugin-relative (`./bin/iodd.exe` or `./bin/iodd`), so it does not require a separate CLI installation or PATH entry. Import or register the extracted plugin folder using a compatible host's local plugin mechanism. Linux users may need to make the bundled executable executable after extraction.

Plugin installation is host-specific; a portable manifest does not guarantee that every agent can import a local ZIP. The manifests are schema-validated and the bundled executable is exercised through the actual MCP protocol. Interactive plugin installation has not been tested in every host. This repository is not a listing in a universal plugin directory.

### Repository marketplace for Codex

The repository includes `.agents/plugins/marketplace.json`, pointing at `./plugin`. Its source MCP configuration uses `iodd` on PATH, so install the CLI first for this route. With a current Codex client supporting plugin marketplaces:

```sh
codex plugin marketplace add mistik91/ioddfinder-agent --ref main
```

Then use the supported desktop plugin browser to install the plugin from the added marketplace. Availability depends on the host version and workspace settings. This setup follows [OpenAI's plugin packaging documentation](https://developers.openai.com/plugins/build/plugins). A downloaded platform plugin archive is the bundled-binary route; the repository marketplace is the separately-installed-CLI route.

## Standalone CLI companion skill

Install the CLI first. Extract `ioddfinder-skill-VERSION.zip`, then copy its `ioddfinder` folder into your host's skills directory. For Codex that is typically `~/.codex/skills/ioddfinder`; do not overwrite an existing custom skill without reviewing it. Start a new chat or refresh the host's skills, then invoke `$ioddfinder` or ask for an IO-Link IODD lookup. Other Agent Skills hosts can use the same `SKILL.md` in their own discovery location.

The skill teaches exact device matching, pagination, version selection, download reuse and cleanup. It adds no credentials or services. It can use either connected MCP tools or the CLI, and it leaves downloaded XML interpretation to the agent's existing tools.
