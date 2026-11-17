"""Check release checksums, archive contents and the bundled executable via smoke.py."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument('--rid', required=True)
parser.add_argument('--directory', type=Path, default=Path('artifacts/release'))
args = parser.parse_args()
root = Path(__file__).resolve().parent.parent
directory = args.directory.resolve()
for line in (directory / 'SHA256SUMS.txt').read_text().splitlines():
    digest, name = line.split('  ', 1)
    assert re.fullmatch(r'[a-f0-9]{64}', digest)
    path = (directory / name).resolve()
    assert path.parent == directory and hashlib.sha256(path.read_bytes()).hexdigest() == digest, name

for path in sorted(directory.glob('*.zip')):
    with zipfile.ZipFile(path) as archive:
        assert archive.testzip() is None
        names = archive.namelist()
        assert not any(n.startswith('/') or '..' in Path(n).parts for n in names)
        assert not any('/research/' in '/' + n or n.endswith('.pdb') or '/.tools/' in '/' + n for n in names)
        if 'plugin-' not in path.name or not path.name.endswith(args.rid + '.zip'):
            continue
        with tempfile.TemporaryDirectory(prefix='ioddfinder-release-') as temporary:
            archive.extractall(temporary)
            plugin = Path(temporary) / 'ioddfinder-agent'
            subprocess.run([sys.executable, str(root / 'scripts/validate_plugin.py'), str(plugin)], check=True)
            config = json.loads((plugin / 'mcp.json').read_text())
            binary = (plugin / config['mcpServers']['ioddfinder']['command']).resolve()
            binary.chmod(0o755)
            subprocess.run([sys.executable, str(root / 'tests/smoke.py'), '--', str(binary)], check=True)
print('Checksums, archive integrity and bundled plugin executable verified')
