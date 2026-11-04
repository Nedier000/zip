#!/usr/bin/env python3
"""Read-only-safe smoke test for a local AoV/HOK AssetBundle.

Usage:
  python tools/smoke_test.py /path/to/input.assetbundle

The script writes only a temporary modified copy and deletes it after read-back.
It never overwrites the input bundle.
"""
from __future__ import annotations
import json
import os
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PY_ROOT = ROOT / "app" / "src" / "main" / "python"
sys.path.insert(0, str(PY_ROOT))

import asset_engine  # noqa: E402


def main() -> int:
    if len(sys.argv) != 2:
        print(__doc__.strip())
        return 2
    source = Path(sys.argv[1]).expanduser().resolve()
    if not source.is_file():
        print(f"FAIL: input file not found: {source}")
        return 2

    report = json.loads(asset_engine.inspect_bundle(str(source)))
    print(f"Bundle: {source.name} ({report['fileSize']} bytes)")
    print(f"Objects: {report['objectCount']}; Material: {report['materialCount']}; "
          f"Texture2D: {report['textureCount']}; Mesh: {report['meshCount']}")

    material = next((m for m in report["materials"] if m["colors"]), None)
    if material is None:
        print("WARN: no Material.m_Colors property found; edit-write test skipped")
        return 0
    color = material["colors"][0]
    values = [color[k] for k in ("r", "g", "b", "a")]
    # A reversible-by-deletion smoke write: tweak only a temp output, verify
    # the actual saved typetree value, then remove the output bundle.
    values[0] = values[0] + 0.007 if values[0] <= 0.992 else values[0] - 0.007
    with tempfile.TemporaryDirectory(prefix="aov_asset_editor_smoke_") as temp:
        output = Path(temp) / (source.stem + "_smoke.assetbundle")
        result = json.loads(asset_engine.set_material_color(
            report["session"], material["pathId"], color["key"],
            *values, str(output)
        ))
        print(f"Material write/read-back: {result['status']}; "
              f"read-back objects={result['readbackObjects']}; "
              f"output bytes={result['fileSize']}")
        if result["status"] != "readback-ok" or not output.is_file():
            print("FAIL: output missing or did not pass value read-back")
            return 1
        expected = values
        actual = result["savedColor"]
        if any(abs(float(a) - float(b)) > 0.003 for a, b in zip(actual, expected)):
            print(f"FAIL: color mismatch, expected={expected}, actual={actual}")
            return 1
    print("PASS: parser load and temporary Material write/read-back verified.")
    print("NOTE: this does not verify Android GPU preview or in-game appearance.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
