"""Exercise the real CLI and MCP process. No third-party Python dependencies.

python tests/smoke.py [--live] -- path/to/iodd[.exe]
python tests/smoke.py [--live] -- path/to/dotnet path/to/iodd.dll

--live performs a bounded search, viewer read and one package download.
All saved state is isolated in a temporary directory.
"""
import argparse
import json
import pathlib
import queue
import subprocess
import tempfile
import threading

parser = argparse.ArgumentParser()
parser.add_argument('--live', action='store_true')
parser.add_argument('command', nargs=argparse.REMAINDER)
args = parser.parse_args()
command = args.command[1:] if args.command[:1] == ['--'] else args.command
if not command:
    parser.error('Supply an executable command after --')


def cli(*arguments, ok=True):
    result = subprocess.run(command + list(arguments), text=True, capture_output=True, timeout=45)
    assert (result.returncode == 0) == ok, (result.returncode, result.stdout, result.stderr)
    return json.loads(result.stdout if ok else result.stderr)


with tempfile.TemporaryDirectory(prefix='ioddfinder-smoke-') as temporary:
    library = pathlib.Path(temporary) / 'library'
    option = ['--library', str(library)]
    assert cli(*option, 'library', 'list') == []
    assert not library.exists(), 'Listing an empty library should create no state'
    cli('search', '--size', '101', ok=False)
    if args.live:
        result = cli(*option, 'search', '--vendor-id', '310', '--device-id', '706',
                     '--product', 'TN2405', '--exact', '--revision', '1.1', '--size', '2')
        assert result['totalElements'] == 1
        assert not library.exists(), 'Search must not initialize a local library'
        result = cli(*option, 'inspect', '--vendor-id', '310', '--device-id', '706')
        assert result['sections']
        result = cli(*option, 'download', '--vendor-id', '310', '--iodd-id', '399')
        assert result['source'] == 'network'
        saved_hash = result['package']['sha256']
        result = cli(*option, 'download', '--vendor-id', '310', '--iodd-id', '399')
        assert result['source'] == 'library' and result['package']['sha256'] == saved_hash
        assert len(cli(*option, 'library', 'list')) == 1
        assert cli(*option, 'library', 'clear')['removedFiles'] == 2
        assert cli(*option, 'library', 'list') == []
    print('CLI smoke passed', flush=True)

    process = subprocess.Popen(command + option + ['mcp'], stdin=subprocess.PIPE,
                               stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, bufsize=1)
    lines = queue.Queue()
    errors = []

    def stdout():
        for line in process.stdout:
            lines.put(line)

    def stderr():
        for line in process.stderr:
            errors.append(line)

    threading.Thread(target=stdout, daemon=True).start()
    threading.Thread(target=stderr, daemon=True).start()

    def send(message):
        process.stdin.write(json.dumps(message) + '\n')
        process.stdin.flush()

    def request(identifier, method, params):
        send({'jsonrpc': '2.0', 'id': identifier, 'method': method, 'params': params})
        while True:
            try:
                message = json.loads(lines.get(timeout=25))
            except queue.Empty:
                raise AssertionError('MCP timed out: ' + ''.join(errors))
            if message.get('id') == identifier:
                assert 'error' not in message, message
                return message['result']

    try:
        initialized = request(1, 'initialize', {'protocolVersion': '2025-06-18',
            'capabilities': {}, 'clientInfo': {'name': 'ioddfinder-smoke', 'version': '1.0'}})
        assert initialized['serverInfo']
        send({'jsonrpc': '2.0', 'method': 'notifications/initialized'})
        tools = request(2, 'tools/list', {})['tools']
        assert len(tools) == 10, [t['name'] for t in tools]
        result = request(3, 'tools/call', {'name': 'iodd_library_list', 'arguments': {}})
        assert json.loads(result['content'][0]['text']) == []
        result = request(4, 'tools/call', {'name': 'iodd_search', 'arguments': {'size': 101}})
        assert result.get('isError'), 'Invalid arguments should produce an MCP tool error'
        result = request(5, 'tools/call', {'name': 'iodd_library_clear', 'arguments': {}})
        assert result.get('isError'), 'Clear must require explicit confirmation'
        print('MCP initialize, 10 tools, invocation and error smoke passed', flush=True)
    finally:
        process.stdin.close()
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.terminate()
            process.wait(timeout=5)
