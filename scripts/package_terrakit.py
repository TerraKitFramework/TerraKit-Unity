#!/usr/bin/env python3
"""Produce a Unity UPM .tgz using verified native artifacts from TerraKit releases.

On CI: python3 scripts/package_terrakit.py --output dist
One-time GUID migration (before deleting existing .meta files):
    python3 scripts/package_terrakit.py --export-guid-map
Offline/tests: python3 scripts/package_terrakit.py --archives-dir tests/fixtures --output dist
Requires Apple's `lipo` to combine Intel and ARM64 dylibs, so run on macOS.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
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
# The C# bindings in Runtime/Native/TerraKitNativeLibrary.cs support ABI 0.2.x.
SUPPORTED_ABI = (0, 2)
GUID_MAP_NAME = "unity-guids.json"  # Commit this file; it is NOT included in the UPM archive.
GUID_PATTERN = re.compile(r"[0-9a-fA-F]{32}")
# platform entries match the plugin_meta() values below
NATIVE_PLUGIN_PLATFORMS = {
    "Runtime/Plugins/Windows/x86_64/terrakit.dll": ("Windows", "x86_64", "Win64", "x86_64"),
    "Runtime/Plugins/Linux/x86_64/libterrakit.so": ("Linux", "x86_64", "Linux64", "x86_64"),
    "Runtime/Plugins/macOS/libterrakit.dylib": ("OSX", "AnyCPU", "OSXUniversal", "AnyCPU"),
}


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def deterministic_guid(path: str) -> str:
    """Stable between runs and versions as long as the package asset path is unchanged."""
    return sha256(("com.terrakit.unity:" + path).encode("utf-8"))[:32]


def plugin_guid(path: str) -> str:
    """Backwards-compatible alias used by existing packaging tests."""
    return deterministic_guid(path)


def validate_guid_map(guid_map: dict[str, str]) -> dict[str, str]:
    result: dict[str, str] = {}
    used: dict[str, str] = {}
    for path, value in guid_map.items():
        if not isinstance(path, str) or not isinstance(value, str):
            raise ValueError("GUID map must be an object containing path: GUID strings")
        pure = PurePosixPath(path)
        if (not path or path.startswith("/") or "\\" in path or
                any(part in ("", ".", "..") for part in path.split("/")) or
                path.endswith(".meta") or str(pure) != path):
            raise ValueError(f"Invalid GUID map asset path: {path!r}")
        if not GUID_PATTERN.fullmatch(value):
            raise ValueError(f"Invalid GUID for {path}: {value!r}")
        guid = value.lower()
        if guid in used and used[guid] != path:
            raise ValueError(f"Duplicate GUID for {used[guid]} and {path}")
        used[guid] = path
        result[path] = guid
    return result


def read_guid_map(root: Path) -> dict[str, str]:
    mapping_path = root / GUID_MAP_NAME
    if not mapping_path.is_file():
        return {}
    data = json.loads(mapping_path.read_text(encoding="utf-8"))
    if not isinstance(data, dict):
        raise ValueError(f"{GUID_MAP_NAME} must contain a JSON object")
    return validate_guid_map(data)


def export_guid_map(root: Path) -> Path:
    """One-time migration: capture existing GUIDs before deleting .meta files."""
    mapping = read_guid_map(root)
    paths = list(root.glob("*.meta"))
    for folder in ("Runtime", "Editor", "Samples~"):
        if (root / folder).is_dir():
            paths.extend((root / folder).rglob("*.meta"))
    for meta in sorted(paths):
        asset = meta.with_name(meta.name[:-5])
        if not asset.exists() or asset == root / "Samples~":
            continue
        text = meta.read_text(encoding="utf-8")
        match = re.search(r"(?m)^guid:\s*([0-9a-fA-F]{32})\s*$", text)
        if not match:
            raise ValueError(f"Existing .meta has no valid GUID: {meta}")
        mapping[asset.relative_to(root).as_posix()] = match.group(1).lower()
    mapping = validate_guid_map(mapping)
    destination = root / GUID_MAP_NAME
    destination.write_text(json.dumps(mapping, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(f"Saved {len(mapping)} existing GUIDs to {destination}")
    return destination


def guid_for(path: str, overrides: dict[str, str]) -> str:
    return overrides.get(path, deterministic_guid(path))


def folder_meta(path: str, overrides: dict[str, str] | None = None) -> str:
    return (f"fileFormatVersion: 2\nguid: {guid_for(path, overrides or {})}\nfolderAsset: yes\n"
            "DefaultImporter:\n  externalObjects: {}\n  userData: \n"
            "  assetBundleName: \n  assetBundleVariant: \n")


def asset_meta(path: str, overrides: dict[str, str]) -> str:
    """Default Unity import settings; specialized settings need explicit templates."""
    extension = Path(path).suffix.lower()
    importer = {
        ".cs": "MonoImporter",
        ".asmdef": "AssemblyDefinitionImporter",
        ".asmref": "AssemblyDefinitionReferenceImporter",
        ".prefab": "PrefabImporter",
        ".asset": "NativeFormatImporter",
        ".mat": "NativeFormatImporter",
        ".shader": "ShaderImporter",
        ".compute": "ComputeShaderImporter",
        ".png": "TextureImporter",
        ".jpg": "TextureImporter",
        ".jpeg": "TextureImporter",
        ".tga": "TextureImporter",
        ".psd": "TextureImporter",
    }.get(extension, "DefaultImporter")
    extras = ("  serializedVersion: 2\n  defaultReferences: []\n"
              "  executionOrder: 0\n  icon: {instanceID: 0}\n") if importer == "MonoImporter" else ""
    return (f"fileFormatVersion: 2\nguid: {guid_for(path, overrides)}\n"
            f"{importer}:\n  externalObjects: {{}}\n{extras}"
            "  userData: \n  assetBundleName: \n  assetBundleVariant: \n")


def plugin_meta(path: str, editor_os: str, editor_cpu: str,
                standalone: str, standalone_cpu: str,
                overrides: dict[str, str] | None = None) -> str:
    return f"""fileFormatVersion: 2
guid: {guid_for(path, overrides or {})}
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
                raise ValueError(f"C ABI {abi} unsupported by TerraKit-Unity (requires 0.2.x)")
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


def _ignore_existing_meta(_directory: str, names: list[str]) -> list[str]:
    # Does not delete source metadata, but never lets it into the staged release.
    return [name for name in names if name.endswith(".meta")]


def generate_meta_files(stage: Path, overrides: dict[str, str]) -> None:
    """Generate a .meta for every staged asset and folder except Samples~ itself."""
    # Snapshot before creating any .meta files, so traversal never re-processes them.
    entries = sorted(stage.rglob("*"))
    used: dict[str, str] = {}
    for entry in entries:
        relative = entry.relative_to(stage).as_posix()
        if relative == "Samples~":  # UPM hidden root; its *contents* need metadata on import.
            continue
        if entry.name.endswith(".meta"):
            raise RuntimeError(f"Unexpected source metadata copied into stage: {relative}")
        guid = guid_for(relative, overrides)
        if guid in used:
            raise RuntimeError(f"Duplicate generated GUID: {relative} and {used[guid]}")
        used[guid] = relative
        target = entry.with_name(entry.name + ".meta")
        if entry.is_dir():
            content = folder_meta(relative, overrides)
        elif entry.is_file():
            if relative in NATIVE_PLUGIN_PLATFORMS:
                content = plugin_meta(relative, *NATIVE_PLUGIN_PLATFORMS[relative],
                                      overrides=overrides)
            else:
                content = asset_meta(relative, overrides)
        else:
            raise RuntimeError(f"Unsupported staged path type: {relative}")
        target.write_text(content, encoding="utf-8")


def create_stage(root: Path, stage: Path, binaries: dict[str, bytes],
                 lipo: str, overrides: dict[str, str] | None = None) -> None:
    overrides = read_guid_map(root) if overrides is None else overrides
    for directory in ("Runtime", "Editor", "Samples~"):
        shutil.copytree(root / directory, stage / directory, ignore=_ignore_existing_meta)
    for optional in ("README.md", "CHANGELOG.md", "LICENSE.md", "LICENSE"):
        if (root / optional).is_file():
            shutil.copy2(root / optional, stage / optional)
    shutil.copy2(root / "package.json", stage / "package.json")
    shutil.copy2(root / "terrakit-version.json", stage / "terrakit-version.json")

    # Native files come from verified pinned artifacts, never a floating release.
    for folder in ("Windows/x86_64", "Linux/x86_64", "macOS"):
        (stage / "Runtime/Plugins" / folder).mkdir(parents=True, exist_ok=True)

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
        subprocess.run([lipo, str(macos), "-verify_arch", "x86_64", "arm64"], check=True)

    generate_meta_files(stage, overrides)


def make_tarball(stage: Path, destination: Path) -> None:
    """UPM's local tarball support understands npm-style package/ .tgz layouts."""
    destination.parent.mkdir(parents=True, exist_ok=True)
    with tarfile.open(destination, "w:gz") as tar:
        for path in sorted(p for p in stage.rglob("*") if p.is_file()):
            relative = path.relative_to(stage).as_posix()
            tar.add(path, arcname="package/" + relative, recursive=False)
    with tarfile.open(destination, "r:gz") as tar:
        names = set(tar.getnames())
        required = {"package/package.json", *(
            "package/" + p for p in NATIVE_PLUGIN_PLATFORMS)}
        if not required.issubset(names):
            raise RuntimeError("Built UPM package is missing required files")
        expected = {"package/" + p.relative_to(stage).as_posix() + ".meta"
                    for p in stage.rglob("*")
                    if p != stage / "Samples~" and not p.name.endswith(".meta")}
        if not expected.issubset(names):
            missing = sorted(expected - names)
            raise RuntimeError(f"Built UPM package is missing {len(missing)} .meta files: {missing[:8]}")
        print(f"Verified {len(expected)} generated Unity .meta files in {destination.name}")


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
    parser.add_argument("--export-guid-map", action="store_true",
                        help="One-time export of existing GUIDs before deleting .meta files")
    args = parser.parse_args()
    if args.export_guid_map:
        export_guid_map(ROOT)
        return
    run(ROOT, args.output, args.archives_dir, args.lipo)


if __name__ == "__main__":
    main()
