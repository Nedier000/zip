from pathlib import Path
import re

root = Path("mobile")
patterns = [
    "DbIndexExtractor.cs", "AssetBrowser.cs", "AssetExportService.cs",
    "MainActivity.cs", "Texture", "Model", "Audio", "AssetBundle",
    "StudioWorkspace", "StudioMobile", "StudioMesh", "StudioAudio", "StudioTexture", "AssetEntry"
]
files = sorted(p for p in root.rglob("*.cs") if any(k.lower() in p.name.lower() for k in patterns))
print("=== ANDROID SOURCE AUDIT: FILES ===")
for p in files:
    print(f"{p}: {p.stat().st_size} bytes")
    if p.name in {"DbIndexExtractor.cs", "AssetBrowser.cs", "AssetExportService.cs", "MainActivity.cs",
                  "StudioWorkspaceService.cs", "StudioMobilePanels.cs", "StudioMobileSettings.cs",
                  "StudioMeshPreviewView.cs", "StudioAudioPreviewView.cs", "StudioTexturePreviewView.cs",
                  "StudioMobileFileAccess.cs"}:
        lines = p.read_text(encoding="utf-8", errors="replace").splitlines()
        print(f"--- BEGIN {p} ({len(lines)} lines) ---")
        for i, line in enumerate(lines, 1):
            print(f"{i}: {line[:300]}")
        print(f"--- END {p} ---")
