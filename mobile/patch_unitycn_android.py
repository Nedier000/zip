from pathlib import Path

root = Path("thirdparty/Studio-HoK/AssetStudio")
path = root / "UnityCNManager.cs"
if not path.is_file():
    raise SystemExit(f"Studio-HoK UnityCNManager source not found: {path}")
s = path.read_text(encoding="utf-8-sig")
if "GetAndroidKeysPath()" not in s:
    s = s.replace(
        '        private static List<UnityCN.Entry> Entries = new List<UnityCN.Entry>();',
        '''        private static List<UnityCN.Entry> Entries = new List<UnityCN.Entry>();

        private static string GetAndroidKeysPath()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root))
                root = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            if (string.IsNullOrWhiteSpace(root))
                root = Path.GetTempPath();

            string folder = Path.Combine(root, "Studio-HoK-Android");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, KeysFileName);
        }'''
    )
    old_ctor = '''        static UnityCNManager()
        {
            var str = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, KeysFileName));
            Entries = JsonConvert.DeserializeObject<List<UnityCN.Entry>>(str);
        }'''
    new_ctor = '''        static UnityCNManager()
        {
            string writablePath = GetAndroidKeysPath();
            if (!File.Exists(writablePath))
            {
                string bundledPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, KeysFileName);
                if (File.Exists(bundledPath))
                    File.Copy(bundledPath, writablePath, overwrite: false);
                else
                    File.WriteAllText(writablePath, "[]");
            }

            var str = File.ReadAllText(writablePath);
            Entries = JsonConvert.DeserializeObject<List<UnityCN.Entry>>(str)
                ?? new List<UnityCN.Entry>();
        }'''
    if old_ctor not in s:
        raise SystemExit("Could not locate UnityCNManager static constructor")
    s = s.replace(old_ctor, new_ctor, 1)
    s = s.replace(
        'File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, KeysFileName), str);',
        'File.WriteAllText(GetAndroidKeysPath(), str);',
        1
    )
    path.write_text(s, encoding="utf-8")
    print("Patched UnityCN key storage to writable Android app data.")
else:
    print("UnityCN Android key path patch already applied.")

# Ensure the default key list is copied into the application output.
project = root / "AssetStudio.csproj"
if project.is_file():
    text = project.read_text(encoding="utf-8")
    if "CopyToOutputDirectory" not in text or "Keys.json" not in text:
        raise SystemExit("AssetStudio.csproj does not preserve Keys.json; refusing to silently omit bundled keys.")
