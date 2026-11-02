"""Tests for update-manifest.py."""
import importlib.util
import json
import os
import tempfile
import unittest

_HERE = os.path.dirname(os.path.abspath(__file__))
_SPEC = importlib.util.spec_from_file_location(
    "update_manifest", os.path.join(_HERE, "update-manifest.py"))
update_manifest = importlib.util.module_from_spec(_SPEC)
_SPEC.loader.exec_module(update_manifest)


def _plugin(versions):
    return {
        "guid": "08dd6fbf-c026-48c9-9405-824c3906df09",
        "name": "MCP Server",
        "description": "desc",
        "overview": "ov",
        "owner": "AeLui",
        "category": "General",
        "versions": versions,
    }


def _run(manifest_path, version, checksum="abc", changelog="Notes"):
    update_manifest.main([
        "--manifest", manifest_path,
        "--version", version,
        "--target-abi", "12.4.0.0",
        "--source-url", "https://example.invalid/%s.zip" % version,
        "--checksum", checksum,
        "--changelog", changelog,
        "--timestamp", "2026-11-02T10:00:00Z",
    ])


class UpdateManifestTests(unittest.TestCase):
    def setUp(self):
        self._dir = tempfile.TemporaryDirectory()
        self.addCleanup(self._dir.cleanup)
        self.path = os.path.join(self._dir.name, "manifest.json")

    def _write(self, data):
        with open(self.path, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2)

    def _read(self):
        with open(self.path, encoding="utf-8") as f:
            return json.load(f)

    def test_prepends_new_version(self):
        old = {"version": "0.1.0.0", "changelog": "old", "targetAbi": "12.4.0.0",
               "sourceUrl": "u", "checksum": "c", "timestamp": "t"}
        self._write([_plugin([old])])
        _run(self.path, "0.2.0.0")
        versions = self._read()[0]["versions"]
        self.assertEqual(["0.2.0.0", "0.1.0.0"], [v["version"] for v in versions])
        self.assertEqual(
            {"version": "0.2.0.0", "changelog": "Notes", "targetAbi": "12.4.0.0",
             "sourceUrl": "https://example.invalid/0.2.0.0.zip", "checksum": "abc",
             "timestamp": "2026-11-02T10:00:00Z"},
            versions[0])

    def test_replaces_same_version(self):
        old = {"version": "0.1.0.0", "changelog": "old", "targetAbi": "12.4.0.0",
               "sourceUrl": "u", "checksum": "old", "timestamp": "t"}
        other = dict(old, version="0.0.9.0")
        self._write([_plugin([old, other])])
        _run(self.path, "0.1.0.0", checksum="new")
        versions = self._read()[0]["versions"]
        self.assertEqual(["0.1.0.0", "0.0.9.0"], [v["version"] for v in versions])
        self.assertEqual("new", versions[0]["checksum"])

    def test_preserves_other_fields(self):
        self._write([_plugin([])])
        _run(self.path, "0.1.0.0")
        plugin = self._read()[0]
        expected = _plugin([])
        for key, value in expected.items():
            if key != "versions":
                self.assertEqual(value, plugin[key])
        self.assertEqual(1, len(plugin["versions"]))
        with open(self.path, encoding="utf-8") as f:
            text = f.read()
        self.assertIn('\n  {\n    "guid"', text)
        self.assertTrue(text.endswith("\n"))


if __name__ == "__main__":
    unittest.main()
