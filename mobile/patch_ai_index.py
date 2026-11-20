from pathlib import Path

path = Path("thirdparty/Studio-HoK/AssetStudio/ResourceIndex.cs")
if not path.is_file():
    raise SystemExit(f"ResourceIndex source is missing: {path}")
s = path.read_text(encoding="utf-8-sig")
anchor = "        public static void Clear()"
if "public static int GetAssetCount()" not in s:
    if s.count(anchor) != 1:
        raise SystemExit("Could not locate ResourceIndex.Clear()")
    s = s.replace(anchor, """        public static int GetAssetCount() => Instance.Assets?.Count ?? 0;

        public static int GetContainerCount() => BundleMap.Values.Sum(x => x.Count);

""" + anchor, 1)
path.write_text(s, encoding="utf-8")
print("Added ResourceIndex load-count diagnostics.")