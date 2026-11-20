"""Build explicit release archives; never includes research, user data, SDKs or PDBs."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parent.parent


def add(archive, name, data, executable=False):
    info = zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0))
    info.compress_type = zipfile.ZIP_DEFLATED
    info.external_attr = (0o100755 if executable else 0o100644) << 16
    archive.writestr(info, data)


def dependencies():
    assets = json.loads((ROOT / 'src/IoddFinder.Cli/obj/project.assets.json').read_text(encoding='utf-8'))
    folders = [Path(p) for p in assets['packageFolders']]
    result = []
    for key, value in assets['libraries'].items():
        if value['type'] != 'package':
            continue
        package_id, version = key.split('/')
        nuspec = next((folder / value['path'] / (package_id.lower() + '.nuspec') for folder in folders
                       if (folder / value['path'] / (package_id.lower() + '.nuspec')).is_file()), None)
        if nuspec is None:
            raise RuntimeError('Missing package metadata: ' + key)
        tree = ET.parse(nuspec).getroot()
        for node in tree.iter():
            node.tag = node.tag.rsplit('}', 1)[-1]
        meta = tree.find('metadata')
        if meta is None:
            raise RuntimeError('Missing nuspec metadata: ' + key)
        def text(name):
            node = meta.find(name)
            return node.text if node is not None else None
        result.append({'id': package_id, 'version': version, 'license': text('license'),
                       'copyright': text('copyright'), 'projectUrl': text('projectUrl')})
    return json.dumps(result, indent=2).encode()


def package(rid, publish, output):
    manifest = json.loads((ROOT / 'plugin/plugin.json').read_text())
    version = manifest['version']
    project_version = ET.parse(ROOT / 'src/IoddFinder.Cli/IoddFinder.Cli.csproj').getroot().findtext('.//Version')
    if version != project_version or not re.fullmatch(r'\d+\.\d+\.\d+', version):
        raise ValueError('Plugin/project versions must match and be a stable semantic version')
    executable = 'iodd.exe' if rid.startswith('win-') else 'iodd'
    binary = (publish / executable).read_bytes()
    output.mkdir(parents=True, exist_ok=True)
    dep = dependencies()
    shared = [(name, (ROOT / name).read_bytes()) for name in ['LICENSE', 'README.md', 'CHANGELOG.md']]
    shared += [(p.relative_to(ROOT).as_posix(), p.read_bytes()) for p in sorted((ROOT / 'docs').rglob('*')) if p.is_file()]
    shared.append(('DEPENDENCIES.json', dep))
    cli = output / f'ioddfinder-agent-{version}-{rid}.zip'
    with zipfile.ZipFile(cli, 'w') as archive:
        add(archive, executable, binary, True)
        for name, data in shared:
            add(archive, name, data)
    plugin = output / f'ioddfinder-agent-plugin-{version}-{rid}.zip'
    prefix = 'ioddfinder-agent/'
    with zipfile.ZipFile(plugin, 'w') as archive:
        for path in sorted((ROOT / 'plugin').rglob('*')):
            if not path.is_file():
                continue
            name = path.relative_to(ROOT / 'plugin').as_posix()
            if name == 'mcp.json':
                config = json.loads(path.read_text())
                config['mcpServers']['ioddfinder']['command'] = './bin/' + executable
                data = json.dumps(config, indent=2).encode()
            elif name == '.mcp.json':
                # Codex 0.130 resolves cwd against the plugin root. Launch the
                # bundled binary from there using fixed arguments on each OS.
                command, args = ('cmd.exe', ['/d', '/c', r'bin\iodd.exe', 'mcp']) if rid.startswith('win-') else (
                    '/bin/sh', ['-c', 'exec ./bin/iodd mcp'])
                config = {'mcpServers': {'ioddfinder': {'command': command, 'args': args, 'cwd': './'}}}
                data = json.dumps(config, indent=2).encode()
            else:
                data = path.read_bytes()
            add(archive, prefix + name, data)
        add(archive, prefix + 'bin/' + executable, binary, True)
        for name, data in shared:
            add(archive, prefix + name, data)
    if rid == 'win-x64':
        skill = output / f'ioddfinder-skill-{version}.zip'
        with zipfile.ZipFile(skill, 'w') as archive:
            for path in sorted((ROOT / 'plugin/skills/ioddfinder').rglob('*')):
                if path.is_file():
                    add(archive, 'ioddfinder/' + path.relative_to(ROOT / 'plugin/skills/ioddfinder').as_posix(), path.read_bytes())
            add(archive, 'ioddfinder/LICENSE', (ROOT / 'LICENSE').read_bytes())
    sums = []
    for path in sorted(output.glob('*.zip')):
        sums.append(hashlib.sha256(path.read_bytes()).hexdigest() + '  ' + path.name)
    (output / 'SHA256SUMS.txt').write_text('\n'.join(sums) + '\n', encoding='utf-8')
    print(json.dumps({'version': version, 'rid': rid, 'binaryBytes': len(binary),
                      'archives': [p.name for p in sorted(output.glob('*.zip'))]}, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--rid', required=True, choices=['win-x64', 'linux-x64'])
    parser.add_argument('--publish-dir', type=Path, required=True)
    parser.add_argument('--output-dir', type=Path, default=ROOT / 'artifacts/release')
    args = parser.parse_args()
    package(args.rid, args.publish_dir.resolve(), args.output_dir.resolve())
