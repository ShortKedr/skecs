"""Fetch and verify a pinned release package; extract only its .NET 8 assembly."""
import hashlib
import json
from pathlib import Path
import sys
from urllib.request import urlopen
import xml.etree.ElementTree as ET
from zipfile import ZipFile

root = Path(__file__).resolve().parents[2]
release = json.loads(sys.argv[1])
directory = root / 'artifacts' / 'docs-releases' / release['version']
directory.mkdir(parents=True, exist_ok=True)
package = directory / f"{release['packageId']}.{release['version']}.nupkg"
if not package.exists():
    with urlopen(release['packageUrl'], timeout=60) as response:
        data = response.read()
    if hashlib.sha256(data).hexdigest() != release['packageSha256']:
        raise ValueError('Downloaded release package checksum mismatch')
    package.write_bytes(data)
if hashlib.sha256(package.read_bytes()).hexdigest() != release['packageSha256']:
    raise ValueError('Local release package checksum mismatch; refusing to build')
with ZipFile(package) as archive:
    nuspecs = [name for name in archive.namelist() if name.endswith('.nuspec')]
    if len(nuspecs) != 1:
        raise ValueError('Expected exactly one package manifest')
    manifest = ET.fromstring(archive.read(nuspecs[0]))
    metadata = manifest.find('{*}metadata')
    for field, expected in [('id', release['packageId']), ('version', release['version'])]:
        if metadata.findtext('{*}' + field) != expected:
            raise ValueError(f'Package {field} does not match release registry')
    repository = metadata.find('{*}repository')
    if repository is None or repository.get('commit') != release['sourceCommit']:
        raise ValueError('Package repository commit does not match pinned release source')
    assembly = f"lib/net8.0/{release['product']}.dll"
    for name in [assembly, assembly[:-4] + '.xml']:
        if name in archive.namelist():
            destination = directory / Path(name).name
            destination.write_bytes(archive.read(name))
    has_comments = assembly[:-4] + '.xml' in archive.namelist()
print(json.dumps({'assembly': str(directory / Path(assembly).name), 'package': str(package), 'hasComments': has_comments}))
