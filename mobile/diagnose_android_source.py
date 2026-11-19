from pathlib import Path
import re

root = Path("mobile")
patterns = [
    "DbIndexExtractor.cs", "AssetBrowser.cs", "AssetExportService.cs",
    "MainActivity.cs", "Texture", "Model", "Audio", "AssetBundle"
]
files = sorted(p for p in root.rglob("*.cs") if any(k.lower() in p.name.lower() for k in patterns))
print("=== ANDROID SOURCE AUDIT: FILES ===")
for p in files:
    print(f"{p}: {p.stat().st_size} bytes")
    if p.name in {"DbIndexExtractor.cs", "AssetBrowser.cs", "AssetExportService.cs", "MainActivity.cs"}:
        lines = p.read_text(encoding="utf-8", errors="replace").splitlines()
        print(f"--- BEGIN {p} ({len(lines)} lines) ---")
        for i, line in enumerate(lines, 1):
            if (p.name == "MainActivity.cs" and (re.search(r"private .*?(Async|Task|void|bool|string|byte\[\]).*\(", line) or "AddButton(" in line)) or p.name != "MainActivity.cs":
                print(f"{i}: {line[:240]}")
        print(f"--- END {p} ---")
