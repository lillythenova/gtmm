#!/usr/bin/env python3
"""Publish self-contained Avalonia single-file builds for supported platforms."""

import argparse
import re
import shutil
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT / "gtmm" / "gtmm.csproj"
DIST = ROOT / "dist"

LINUX_RIDS = ("linux-x64", "linux-arm64")
WINDOWS_RIDS = ("win-x64", "win-arm64")


def publish(runtime_id: str, version: str) -> None:
    output_dir = DIST / runtime_id
    if output_dir.exists():
        shutil.rmtree(output_dir)

    command = [
        "dotnet",
        "publish",
        str(PROJECT),
        "--configuration",
        "Release",
        "--runtime",
        runtime_id,
        "--self-contained",
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:DebugType=None",
        "-p:DebugSymbols=false",
        f"-p:Version={version}",
        "--output",
        str(output_dir),
    ]
    print(f"\nPublishing {runtime_id}...", flush=True)
    subprocess.run(command, cwd=ROOT, check=True)

    executable = output_dir / ("gtmm.exe" if runtime_id.startswith("win-") else "gtmm")
    if not executable.is_file():
        raise FileNotFoundError(f"Publish succeeded but expected executable was not created: {executable}")
    for symbol_file in output_dir.glob("*.pdb"):
        symbol_file.unlink()
    extra_files = [path.name for path in output_dir.iterdir() if path != executable]
    if extra_files:
        raise RuntimeError(f"Expected a single-file publish, found extra output in {output_dir}: {extra_files}")
    print(f"Created {executable.relative_to(ROOT)}", flush=True)


def main() -> None:
    parser = argparse.ArgumentParser(description="Build one-file Avalonia releases.")
    targets = parser.add_mutually_exclusive_group()
    targets.add_argument("--linux", action="store_true", help="Build Linux x64 and ARM64 (glibc)")
    targets.add_argument("--windows", action="store_true", help="Build Windows x64 and ARM64")
    targets.add_argument("--all", action="store_true", help="Build every supported target")
    targets.add_argument("--rid", choices=LINUX_RIDS + WINDOWS_RIDS, help="Build one runtime identifier")
    parser.add_argument("--version", default="1.0.0", help="Application version (optionally prefixed with v)")
    args = parser.parse_args()

    version = args.version.removeprefix("v").removeprefix("V")
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?", version):
        parser.error("--version must be a semantic version such as 1.2.3 or v1.2.3-rc.1")
    runtimes = (
        (args.rid,)
        if args.rid
        else LINUX_RIDS + WINDOWS_RIDS
        if args.all
        else WINDOWS_RIDS
        if args.windows
        else LINUX_RIDS
    )
    DIST.mkdir(exist_ok=True)
    for runtime_id in runtimes:
        publish(runtime_id, version)

    print("\nAll requested single-file builds completed.")


if __name__ == "__main__":
    main()
