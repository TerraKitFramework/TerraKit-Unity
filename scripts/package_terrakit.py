#!/usr/bin/env python3
"""Produce a Unity UPM .tgz using verified native artifacts from TerraKit releases.

On CI: python3 scripts/package_terrakit.py --output dist
Offline/tests: python3 scripts/package_terrakit.py --archives-dir tests/fixtures --output dist
Requires Apple's `lipo` to combine Intel and ARM64 dylibs, so run on macOS.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tarfile
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parent.parent
TARGET_LIBRARIES = {
    "x86_64-pc-windows-msvc": "terrakit.dll",
    "x86_64-unknown-linux-gnu": "libterrakit.so",
    "x86_64-apple-darwin": "libterrakit.dylib",
    "aarch64-apple-darwin": "libterrakit.dylib",
}
# The C# bindings in Runtime/Native/TerraKitNativeLibrary.cs support ABI 0.1.x.
SUPPORTED_ABI = (0, 1)


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def plugin_guid(path: str) -> str:
    """Generate a stable 32-character Unity GUID for paths in release archives."""
    return sha256(("com.terrakit.unity:" + path).encode("utf-8"))[:32]


def folder_meta(path: str) -> str:
    return (f"fileFormatVersion: 2\nguid: {plugin_guid(path)}\nfolderAsset: yes\n"
            "DefaultImporter:\n  externalObjects: {}\n  userData: \n"
            "  assetBundleName: \n  assetBundleVariant: \n")


def plugin_meta(path: str, editor_os: str, editor_cpu: str, standalone: str, standalone_cpu: str) -> str:
    return f"""fileFormatVersion: 2
guid: {plugin_guid(path)}
PluginImporter:
  externalObjects: {{}}
  serializedVersion: 2
  iconMap: {{}}
  executionOrder: {{}}
  isPreloaded: 0
  isOverridable: 0
  platformData:
  - first:
      Any: 
    second:
      enabled: 0
      settings: {{}}
  - first:
      Editor: Editor
    second:
      enabled: 1
      settings:
        CPU: {editor_cpu}
        OS: {editor_os}
  - first:
      Standalone: {standalone}
    second:
      enabled: 1
      settings:
        CPU: {standalone_cpu}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""


def read_config(root: Path) -> tuple[str, str, str]:
    pin = json.loads((root / "terrakit-version.json").read_text(encoding="utf-8"))
    pkg = json.loads((root / "package.json").read_text(encoding="utf-8"))
    repo = pin["repository"]
    version = pin["version"]
    package_version = pkg["version"]
    if not re.fullmatch(r"[\w.-]+/[\w.-]+", repo):
        raise ValueError(f"Invalid TerraKit source repository: {repo!r}")
    for label, candidate in (("TerraKit", version), ("Unity package", package_version)):
        if not re.fullmatch(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?", candidate):
            raise ValueError(f"Invalid {label} version: {candidate!r}")
    if pkg["name"] != "com.terrakit.unity":
        raise ValueError("Expected com.terrakit.unity package")
    return repo, version, package_version


def fetch_archives(repository: str, version: str, directory: Path) -> None:
    """Download only exact pinned release assets, never a floating 'latest'."""
    tag = f"terrakit-v{version}"
    directory.mkdir(parents=True, exist_ok=True)
    for target in TARGET_LIBRARIES:
        name = f"terrakit-v{version}-{target}.zip"
        command = ["gh", "release", "download", tag, "--repo", repository,
                   "--pattern", name, "--pattern", name + ".sha256",
                   "--dir", str(directory)]
        subprocess.run(command, check=True)


def read_native_libraries(version: str, directory: Path) -> dict[str, bytes]:
    result: dict[str, bytes] = {}
    abi_versions: set[str] = set()
    for target, lib_filename in TARGET_LIBRARIES.items():
        stem = f"terrakit-v{version}-{target}"
        archive = directory / (stem + ".zip")
        checksum = directory / (stem + ".zip.sha256")
        expected_line = checksum.read_text(encoding="utf-8").strip()
        # Avoid trusting checksum filenames or arbitrary text from the server.
        match = re.fullmatch(r"([0-9a-fA-F]{64})  " + re.escape(archive.name), expected_line)
        if not match:
            raise ValueError(f"Unexpected checksum contents in {checksum.name}")
        data = archive.read_bytes()
        if sha256(data) != match.group(1).lower():
            raise ValueError(f"SHA-256 mismatch: {archive.name}")

        with zipfile.ZipFile(io.BytesIO(data)) as zf:
            manifest = json.loads(zf.read(stem + "/manifest.json"))
            if manifest.get("library_version") != version or manifest.get("target") != target:
                raise ValueError(f"Incorrect source version/target: {archive.name}")
            abi = manifest.get("abi_version", "")
            abi_parts = abi.split(".")
            if len(abi_parts) != 3 or not all(v.isdigit() for v in abi_parts):
                raise ValueError(f"Malformed C ABI version: {abi!r}")
            if tuple(map(int, abi_parts[:2])) != SUPPORTED_ABI:
                raise ValueError(f"C ABI {abi} unsupported by TerraKit-Unity (requires 0.1.x)")
            abi_versions.add(abi)
            # Verify ALL listed files, not just the shared library we're consuming.
            files = manifest["files"]
            for filename, digest in files.items():
                if not isinstance(filename, str) or filename.startswith("/") or ".." in Path(filename).parts:
                    raise ValueError(f"Invalid manifest path: {filename!r}")
                content = zf.read(stem + "/" + filename)
                if sha256(content) != digest:
                    raise ValueError(f"Tampered archive member: {filename} in {archive.name}")
            library_path = "lib/" + lib_filename
            if library_path not in files:
                raise ValueError(f"Missing {library_path} in manifest for {target}")
            result[target] = zf.read(stem + "/" + library_path)
    if len(abi_versions) != 1:
        raise ValueError(f"Target archives have different ABI versions: {sorted(abi_versions)}")
    return result


def create_stage(root: Path, stage: Path, binaries: dict[str, bytes], lipo: str) -> None:
    for directory in ("Runtime", "Editor", "Samples~"):
        shutil.copytree(root / directory, stage / directory)
    for optional in ("README.md", "CHANGELOG.md", "LICENSE.md", "LICENSE"):
        if (root / optional).is_file():
            shutil.copy2(root / optional, stage / optional)
    shutil.copy2(root / "package.json", stage / "package.json")
    shutil.copy2(root / "terrakit-version.json", stage / "terrakit-version.json")
    # The source has a .gitignore excluding native libraries; it is deliberately NOT
    # copied. The published archive contains the actual binary and stable .meta files.
    for folder in ("Windows", "Windows/x86_64", "Linux", "Linux/x86_64", "macOS"):
        folder_path = stage / "Runtime/Plugins" / folder
        folder_path.mkdir(parents=True, exist_ok=True)
        rel = "Runtime/Plugins/" + folder
        (stage / (rel + ".meta")).write_text(folder_meta(rel), encoding="utf-8")

    windows = stage / "Runtime/Plugins/Windows/x86_64/terrakit.dll"
    linux = stage / "Runtime/Plugins/Linux/x86_64/libterrakit.so"
    macos = stage / "Runtime/Plugins/macOS/libterrakit.dylib"
    windows.write_bytes(binaries["x86_64-pc-windows-msvc"])
    linux.write_bytes(binaries["x86_64-unknown-linux-gnu"])
    with tempfile.TemporaryDirectory() as temp:
        temp_path = Path(temp)
        intel = temp_path / "intel.dylib"
        arm = temp_path / "arm.dylib"
        intel.write_bytes(binaries["x86_64-apple-darwin"])
        arm.write_bytes(binaries["aarch64-apple-darwin"])
        subprocess.run([lipo, "-create", str(intel), str(arm), "-output", str(macos)], check=True)
        subprocess.run([lipo, "-verify_arch", "x86_64", "arm64", str(macos)], check=True)

    for path, os_name, cpu, build_target, build_cpu in (
        (windows, "Windows", "x86_64", "Win64", "x86_64"),
        (linux, "Linux", "x86_64", "Linux64", "x86_64"),
        (macos, "OSX", "AnyCPU", "OSXUniversal", "AnyCPU"),
    ):
        relative = path.relative_to(stage).as_posix()
        path.with_name(path.name + ".meta").write_text(
            plugin_meta(relative, os_name, cpu, build_target, build_cpu), encoding="utf-8")


def make_tarball(stage: Path, destination: Path) -> None:
    """UPM's local tarball support understands npm-style package/ .tgz layouts."""
    destination.parent.mkdir(parents=True, exist_ok=True)
    with tarfile.open(destination, "w:gz") as tar:
        for path in sorted(p for p in stage.rglob("*") if p.is_file()):
            relative = path.relative_to(stage).as_posix()
            # Source folder may contain ignored binary files on a developer machine:
            # only generated plugin destinations are packaged from Runtime/Plugins.
            tar.add(path, arcname="package/" + relative, recursive=False)
    with tarfile.open(destination, "r:gz") as tar:
        names = set(tar.getnames())
        required = {"package/package.json", "package/Runtime/Plugins/Windows/x86_64/terrakit.dll",
                    "package/Runtime/Plugins/Linux/x86_64/libterrakit.so",
                    "package/Runtime/Plugins/macOS/libterrakit.dylib"}
        if not required.issubset(names):
            raise RuntimeError("Built UPM package is missing required files")


def run(root: Path, output: Path, archives_dir: Path | None, lipo: str) -> Path:
    repo, version, package_version = read_config(root)
    with tempfile.TemporaryDirectory() as temp:
        scratch = Path(temp)
        source = archives_dir if archives_dir is not None else scratch / "downloads"
        if archives_dir is None:
            fetch_archives(repo, version, source)
        binaries = read_native_libraries(version, source)
        stage = scratch / "package"
        stage.mkdir()
        create_stage(root, stage, binaries, lipo)
        filename = f"com.terrakit.unity-{package_version}.tgz"
        dest = output / filename
        make_tarball(stage, dest)
        (output / (filename + ".sha256")).write_text(
            sha256(dest.read_bytes()) + "  " + filename + "\n", encoding="utf-8")
    print(f"Built {dest} (TerraKit native {version}, Unity package {package_version})")
    return dest


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=Path("dist"))
    parser.add_argument("--archives-dir", type=Path,
                        help="Use locally downloaded release ZIPs and checksums (offline mode)")
    parser.add_argument("--lipo", default="lipo", help="Path to Apple's lipo executable")
    args = parser.parse_args()
    run(ROOT, args.output, args.archives_dir, args.lipo)


if __name__ == "__main__":
    main()
