"""Fetch and verify a pinned release package and extract its documented assembly."""
import hashlib
import json
from pathlib import Path
import re
import sys
from urllib.request import urlopen
import xml.etree.ElementTree as ET
from zipfile import ZipFile

root = Path(__file__).resolve().parents[2]
release = json.loads(sys.argv[1])
for field, pattern in [
    ('version', r'\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?'),
    ('packageId', r'[A-Za-z0-9][A-Za-z0-9._-]*'),
    ('product', r'[A-Za-z0-9][A-Za-z0-9._-]*'),
    ('assemblyTarget', r'net(?:standard|coreapp)?\d+\.\d+')
]:
    value = release.get(field, 'net8.0' if field == 'assemblyTarget' else None)
    if not isinstance(value, str) or not re.fullmatch(pattern, value):
        raise ValueError(f'Invalid package {field}')
directory = root / 'artifacts' / 'docs-releases' / release['version']
if release.get('id'):
    directory = directory / release['packageId']
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
    assembly = f"lib/{release.get('assemblyTarget', 'net8.0')}/{release['product']}.dll"
    if assembly not in archive.namelist():
        raise ValueError(f'Package lacks the documented assembly: {assembly}')
    xml = assembly[:-4] + '.xml'
    for name in [assembly, xml]:
        destination = directory / Path(name).name
        if name in archive.namelist():
            data = archive.read(name)
            if not destination.exists() or destination.read_bytes() != data:
                destination.write_bytes(data)
        elif destination.exists():
            destination.unlink()
    has_comments = xml in archive.namelist()
print(json.dumps({'assembly': str(directory / Path(assembly).name), 'package': str(package), 'hasComments': has_comments}))
