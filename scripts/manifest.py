#!/usr/bin/env python3
import hashlib
import json
import os
import sys
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from urllib.error import HTTPError
from urllib.request import urlopen
from packaging.version import Version


UPSTREAM_REPOSITORY = 'metatube-community/jellyfin-plugin-metatube'


def load_manifest(repository: str) -> list:
    manifest_path = os.environ.get('METATUBE_MANIFEST_PATH')
    if manifest_path:
        try:
            with open(manifest_path) as f:
                return json.load(f)
        except FileNotFoundError:
            pass
    else:
        try:
            with urlopen(f'https://raw.githubusercontent.com/{repository}/dist/manifest.json', timeout=30) as f:
                return json.load(f)
        except HTTPError as error:
            if error.code != 404:
                raise
    return [{
        'guid': '01cc53ec-c415-4108-bbd4-a684a9801a32',
        'name': 'MetaTube',
        'description': 'MetaTube Plugin for Jellyfin/Emby.',
        'overview': 'MetaTube Plugin for Jellyfin/Emby.',
        'owner': 'MetaTube',
        'category': 'Metadata',
        'imageUrl': f'https://raw.githubusercontent.com/{UPSTREAM_REPOSITORY}/main/'
                    'Jellyfin.Plugin.MetaTube/thumb.png',
        'versions': []
    }]


def md5sum(filename) -> str:
    with open(filename, 'rb') as f:
        return hashlib.md5(f.read()).hexdigest()


def get_jellyfin_version(csproj: str) -> str:
    tree = ET.parse(csproj)
    root = tree.getroot()

    for pkg in root.iter("PackageReference"):
        if pkg.attrib.get("Include") in ("Jellyfin.Controller", "Jellyfin.Model"):
            return Version(pkg.attrib.get("Version")).base_version

    raise Exception("Jellyfin version not found")


def generate(filename, version, csproj) -> dict:
    repository = os.environ.get('GITHUB_REPOSITORY', UPSTREAM_REPOSITORY)
    return {
        'checksum': md5sum(filename),
        'changelog': 'Auto Released by Actions',
        'targetAbi': f'{get_jellyfin_version(csproj)}.0',
        'sourceUrl': f'https://github.com/{repository}/releases/download/'
                     f'v{version}/Jellyfin.MetaTube@v{version}.zip',
        'timestamp': datetime.now(timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ'),
        'version': version
    }


def main() -> None:
    filename = sys.argv[1]
    version = filename.split('@', maxsplit=1)[1] \
        .removeprefix('v') \
        .removesuffix('.zip')

    csproj = os.path.join(os.path.dirname(__file__),
                          "../Jellyfin.Plugin.MetaTube/Jellyfin.Plugin.MetaTube.csproj")

    manifest = load_manifest(os.environ.get('GITHUB_REPOSITORY', UPSTREAM_REPOSITORY))

    manifest[0]['versions'].insert(0, generate(filename, version, csproj))

    with open('manifest.json', 'w') as f:
        json.dump(manifest, f, indent=2)


if __name__ == '__main__':
    main()
