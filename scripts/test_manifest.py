import importlib.util
import io
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from urllib.error import HTTPError


spec = importlib.util.spec_from_file_location('manifest', Path(__file__).with_name('manifest.py'))
manifest = importlib.util.module_from_spec(spec)
spec.loader.exec_module(manifest)


class ManifestTests(unittest.TestCase):
    def test_workflow_reads_history_from_checked_out_branch(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'manifest.json'
            catalog = [{'versions': [{'version': '2026.915.1.0'}]}]
            path.write_text(json.dumps(catalog))
            with patch.dict('os.environ', {'METATUBE_MANIFEST_PATH': str(path)}):
                with patch.object(manifest, 'urlopen') as request:
                    self.assertEqual(manifest.load_manifest('owner/repo'), catalog)
            request.assert_not_called()

    def test_missing_local_history_initializes_without_network(self):
        with tempfile.TemporaryDirectory() as directory:
            with patch.dict('os.environ', {'METATUBE_MANIFEST_PATH': str(Path(directory) / 'manifest.json')}):
                with patch.object(manifest, 'urlopen') as request:
                    self.assertEqual(manifest.load_manifest('owner/repo')[0]['versions'], [])
            request.assert_not_called()

    def test_release_url_uses_current_repository(self):
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / 'Jellyfin.MetaTube@v2026.916.1.0.zip'
            archive.write_bytes(b'plugin archive')
            project = Path(directory) / 'plugin.csproj'
            project.write_text('<Project><ItemGroup><PackageReference Include="Jellyfin.Controller" '
                               'Version="12.0.0" /></ItemGroup></Project>')
            with patch.dict('os.environ', {'GITHUB_REPOSITORY': 'kevinmatthe/jellyfin-plugin-metatube'}):
                result = manifest.generate(str(archive), '2026.916.1.0', str(project))
            self.assertEqual(result['sourceUrl'],
                             'https://github.com/kevinmatthe/jellyfin-plugin-metatube/releases/download/'
                             'v2026.916.1.0/Jellyfin.MetaTube@v2026.916.1.0.zip')
            self.assertEqual(result['targetAbi'], '12.0.0.0')

    def test_first_release_initializes_empty_catalog(self):
        error = HTTPError('https://example.com', 404, 'Not Found', {}, None)
        with patch.object(manifest, 'urlopen', side_effect=error):
            catalog = manifest.load_manifest('kevinmatthe/jellyfin-plugin-metatube')
        self.assertEqual(catalog[0]['guid'], '01cc53ec-c415-4108-bbd4-a684a9801a32')
        self.assertEqual(catalog[0]['versions'], [])

    def test_existing_release_history_is_preserved(self):
        catalog = [{'guid': '01cc53ec-c415-4108-bbd4-a684a9801a32',
                    'versions': [{'version': '2026.915.1.0'}]}]
        with patch.object(manifest, 'urlopen', return_value=io.BytesIO(json.dumps(catalog).encode())) as request:
            result = manifest.load_manifest('kevinmatthe/jellyfin-plugin-metatube')
        self.assertEqual(result, catalog)
        self.assertEqual(request.call_args.args[0],
                         'https://raw.githubusercontent.com/kevinmatthe/jellyfin-plugin-metatube/dist/manifest.json')

    def test_network_error_does_not_reset_history(self):
        error = HTTPError('https://example.com', 503, 'Unavailable', {}, None)
        with patch.object(manifest, 'urlopen', side_effect=error):
            with self.assertRaises(HTTPError):
                manifest.load_manifest('kevinmatthe/jellyfin-plugin-metatube')


if __name__ == '__main__':
    unittest.main()
