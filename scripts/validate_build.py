#!/usr/bin/env python3
"""Check a release build locally or in CI without fetching/publishing the catalog."""
import argparse
import hashlib
from pathlib import Path
from zipfile import ZipFile

from manifest import generate


def validate(configuration, version):
    project = Path(__file__).resolve().parents[1] / 'Jellyfin.Plugin.MetaTube'
    platform, framework = {
        'Release': ('Jellyfin', 'net10.0'),
        'Release.Emby': ('Emby', 'net8.0'),
    }[configuration]
    archive_path = project / 'bin' / f'{platform}.MetaTube@v{version}.zip'
    assembly_path = project / 'bin' / configuration / framework / 'MetaTube.dll'

    with ZipFile(archive_path) as archive:
        if archive.namelist() != ['MetaTube.dll']:
            raise ValueError(f'{archive_path.name} must contain only root MetaTube.dll')
        # Reading also verifies the entry CRC. Compare against this build's DLL.
        if archive.read('MetaTube.dll') != assembly_path.read_bytes():
            raise ValueError(f'{archive_path.name} differs from the built assembly')

    if platform == 'Jellyfin':
        # Exercise the release helper directly; main() fetches the live catalog.
        entry = generate(str(archive_path), version,
                         str(project / 'Jellyfin.Plugin.MetaTube.csproj'))
        expected = {
            'targetAbi': '12.0.0.0',
            'version': version,
            'checksum': hashlib.md5(archive_path.read_bytes()).hexdigest(),
            'sourceUrl': (
                'https://github.com/metatube-community/jellyfin-plugin-metatube/'
                f'releases/download/v{version}/{archive_path.name}'
            ),
        }
        for key, value in expected.items():
            if entry.get(key) != value:
                raise ValueError(f'Manifest {key}: expected {value!r}, got {entry.get(key)!r}')

    print(f'Validated {archive_path.name}' +
          (' and Jellyfin manifest' if platform == 'Jellyfin' else ''))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('configuration', choices=('Release', 'Release.Emby'))
    parser.add_argument('version', help='The Version property passed to dotnet build')
    args = parser.parse_args()
    validate(args.configuration, args.version)
