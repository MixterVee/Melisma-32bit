#!/usr/bin/env python3
"""Adjust only Melisma's Gradle ABI filter. Abort if upstream has changed."""
import argparse
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("gradle_file", type=Path)
parser.add_argument("--mode", required=True, choices=("test", "release"))
args = parser.parse_args()

source = args.gradle_file.read_text(encoding="utf-8")
if args.mode == "test":
    expected = 'ndk { abiFilters += listOf("arm64-v8a", "x86_64") }'
    replacement = 'ndk { abiFilters += listOf("armeabi-v7a") }'
else:
    expected = 'ndk { abiFilters += "arm64-v8a" }'
    replacement = 'ndk { abiFilters += listOf("armeabi-v7a", "arm64-v8a") }'

count = source.count(expected)
if count != 1:
    raise SystemExit(
        f"Refusing to patch: expected exactly one occurrence of {expected!r}; found {count}. "
        "Upstream build configuration may have changed."
    )
args.gradle_file.write_text(source.replace(expected, replacement, 1), encoding="utf-8")
print(f"Patched {args.gradle_file}: {replacement}")
