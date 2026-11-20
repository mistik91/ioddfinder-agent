"""Validate manifests against vendored upstream schemas plus executable/path wiring."""
import json
from pathlib import Path
import sys
import jsonschema

root = Path(sys.argv[1] if len(sys.argv) > 1 else 'plugin').resolve()
schemas = Path(__file__).resolve().parent.parent / 'tests/schemas'
for name in ['plugin', 'mcp']:
    document = json.loads((root / (name + '.json')).read_text(encoding='utf-8'))
    schema = json.loads((schemas / (name + '.schema.json')).read_text(encoding='utf-8'))
    jsonschema.Draft202012Validator(schema).validate(document)
config = json.loads((root / 'mcp.json').read_text())
server = config['mcpServers']['ioddfinder']
assert server['type'] == 'stdio' and server['args'] == ['mcp']
command = server['command']
if command.startswith('./'):
    target = (root / command).resolve()
    assert target.is_relative_to(root) and target.is_file(), target
else:
    assert command == 'iodd', 'Source plugin uses the separately installed CLI on PATH'
portable = json.loads((root / 'plugin.json').read_text())
legacy = json.loads((root / '.codex-plugin/plugin.json').read_text())
assert all(legacy[key] == portable[key] for key in ('name', 'version'))
assert legacy['skills'] == './skills/' and legacy['mcpServers'] == './.mcp.json'
if command == 'iodd':
    legacy_server = json.loads((root / '.mcp.json').read_text())['mcpServers']['ioddfinder']
    assert legacy_server == {'command': 'iodd', 'args': ['mcp']}
skill = root / 'skills/ioddfinder/SKILL.md'
text = skill.read_text(encoding='utf-8')
assert text.startswith('---\n') and 'name: ioddfinder\n' in text
assert 'description:' in text and 'TODO' not in text
print('Agent Plugins 1.0 manifests and component paths validated')
