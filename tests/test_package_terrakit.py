"""Offline tests for release packaging, with synthetic archive fixtures."""

from __future__ import annotations

import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tarfile
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("package_terrakit", ROOT / "scripts/package_terrakit.py")
packager = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(packager)


def fixture_archives(directory: Path, version: str, bad_checksum: bool = False,
                     bad_abi: bool = False) -> None:
    for target, filename in packager.TARGET_LIBRARIES.items():
        stem = f"terrakit-v{version}-{target}"
        files = {"lib/" + filename: ("test-native-" + target).encode(),
                 "include/terrakit.h": b"/* test */"}
        manifest = {"library_version": version, "target": target,
                    "abi_version": "0.3.0" if bad_abi else "0.2.0",
                    "files": {path: packager.sha256(data) for path, data in files.items()}}
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w", zipfile.ZIP_STORED) as zf:
            zf.writestr(stem + "/manifest.json", json.dumps(manifest))
            for path, data in files.items():
                zf.writestr(stem + "/" + path, data)
        archive = directory / (stem + ".zip")
        archive.write_bytes(stream.getvalue())
        digest = packager.sha256(stream.getvalue())
        if bad_checksum and target == "x86_64-pc-windows-msvc":
            digest = "0" * 64
        (directory / (stem + ".zip.sha256")).write_text(
            f"{digest}  {archive.name}\n", encoding="utf-8")


class PackageTests(unittest.TestCase):
    def test_pin_and_package_versions(self) -> None:
        repo, version, pkg_version = packager.read_config(ROOT)
        self.assertEqual(repo, "TerraKitFramework/TerraKit")
        self.assertTrue(version)
        self.assertTrue(pkg_version)

    def test_archives_integrity(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp)
            fixture_archives(directory, "0.0.3")
            binaries = packager.read_native_libraries("0.0.3", directory)
            self.assertEqual(set(binaries), set(packager.TARGET_LIBRARIES))

    def test_reject_tampered_archives(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp)
            fixture_archives(directory, "0.0.3", bad_checksum=True)
            with self.assertRaisesRegex(ValueError, "SHA-256 mismatch"):
                packager.read_native_libraries("0.0.3", directory)

    def test_reject_unsupported_abi(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp)
            fixture_archives(directory, "0.0.3", bad_abi=True)
            with self.assertRaisesRegex(ValueError, "unsupported"):
                packager.read_native_libraries("0.0.3", directory)

    def test_upm_package_contains_three_platform_plugins(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            directory = Path(tmp)
            inputs = directory / "in"
            inputs.mkdir()
            fixture_archives(inputs, "0.0.3")
            lipo = directory / "fake-lipo.sh"
            
            lipo.write_text(
                "#!/bin/sh\n"
                "if [ \"$2\" = '-verify_arch' ]; then\n"
                "  [ -f \"$1\" ] && [ \"$3\" = x86_64 ] && "
                "[ \"$4\" = arm64 ] && [ \"$#\" -eq 4 ]\n"
                "  exit $?\n"
                "fi\n"
                "[ \"$1\" = '-create' ] || exit 1\n"
                "cat \"$2\" \"$3\" > \"$5\"\n"
            )
            lipo.chmod(0o755)
            destination = packager.run(ROOT, directory / "out", inputs, str(lipo))
            with tarfile.open(destination, "r:gz") as archive:
                members = set(archive.getnames())
                for path in (
                    "package/package.json",
                    "package/terrakit-version.json",
                    "package/Runtime/Plugins/Windows/x86_64/terrakit.dll",
                    "package/Runtime/Plugins/Linux/x86_64/libterrakit.so",
                    "package/Runtime/Plugins/macOS/libterrakit.dylib",
                    "package/Runtime/Plugins/Windows/x86_64/terrakit.dll.meta",
                    "package/Runtime/Plugins/Linux/x86_64/libterrakit.so.meta",
                    "package/Runtime/Plugins/macOS/libterrakit.dylib.meta",
                    "package/Runtime/Native/TerraKitNativeLibrary.cs",
                ):
                    self.assertIn(path, members)
                meta = archive.extractfile("package/Runtime/Plugins/macOS/libterrakit.dylib.meta").read()
                self.assertIn(b"OSXUniversal", meta)
            checksum = destination.with_name(destination.name + ".sha256").read_text()
            self.assertIn(hashlib.sha256(destination.read_bytes()).hexdigest(), checksum)

    def test_guid_placeholders_follow_generated_meta_and_overrides(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            stage = Path(tmp)
            script_path = "Samples~/Runtime World Demo/Scripts/TerraKitRuntimeWorldDemo.cs"
            script = stage / script_path
            script.parent.mkdir(parents=True)
            script.write_text("public class TerraKitRuntimeWorldDemo {}\n")

            scene = stage / "Samples~/Runtime World Demo/RuntimeWorldDemo.unity"
            prefab = stage / "Samples~/Runtime World Demo/Extra.prefab"
            token = "{{GUID:" + script_path + "}}"

            scene.write_text(f"m_Script: {{guid: {token}}}\n")
            prefab.write_text(f"guid: {token}\n")

            expected = "a" * 32
            packager.generate_meta_files(stage, {script_path: expected})
            packager.resolve_guid_placeholders(stage)

            self.assertIn(f"guid: {expected}", scene.read_text())
            self.assertEqual(prefab.read_text(), f"guid: {expected}\n")
            self.assertNotIn("{{GUID:", scene.read_text())


    def test_guid_placeholders_reject_invalid_references(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            stage = Path(tmp)
            scene = stage / "Demo.unity"

            cases = (
                ("{{GUID:Samples~/missing.cs}}", "Unknown GUID reference"),
                ("{{GUID:../outside.cs}}", "Invalid GUID asset path"),
                ("{{GUID:unclosed.cs", "Unresolved GUID placeholder"),
            )

            for token, error in cases:
                with self.subTest(token=token):
                    scene.write_text(f"guid: {token}\n")

                    with self.assertRaisesRegex(ValueError, error):
                        packager.resolve_guid_placeholders(stage)

if __name__ == "__main__":
    unittest.main()
