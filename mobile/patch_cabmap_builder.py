from pathlib import Path

p = Path("mobile/MainActivity.cs")
if not p.is_file():
    raise SystemExit("mobile/MainActivity.cs missing after source restore")
s = p.read_text(encoding="utf-8")

asset_map_button = '''        AddButton(assetCard, "BUILD / EXPORT ASSETMAP", "#0F766E", "#FFFFFF",
            () => { ShowAssetMapBuilder(); return Task.CompletedTask; });'''
cab_button = asset_map_button + '''
        AddButton(assetCard, "BUILD CABMAP / BUILD BOTH", "#0F766E", "#FFFFFF",
            () => { ShowCabMapBuilder(); return Task.CompletedTask; });'''
export_buttons = cab_button + '''
        AddButton(assetCard, "EXPORT ALL EXPORTABLE ASSETS", "#1D4ED8", "#FFFFFF",
            async () => await ExportAllStudioAssetsAsync());
        AddButton(assetCard, "EXPORT SELECTED ASSETS", "#1D4ED8", "#FFFFFF",
            async () => await ExportSelectedStudioAssetsAsync());'''
if "ShowCabMapBuilder();" not in s:
    if s.count(asset_map_button) != 1:
        raise SystemExit("Could not locate AssetMap builder button")
    s = s.replace(asset_map_button, export_buttons, 1)

anchor = "    private async Task ScanAssetsAsync()"
methods = r"""
    private void ShowCabMapBuilder()
    {
        if (string.IsNullOrWhiteSpace(assetInputLocal) || !Directory.Exists(assetInputLocal))
        {
            Toast.MakeText(this, "Hãy chọn và quét thư mục game trước để dùng CABMap.", ToastLength.Long)!.Show();
            return;
        }
        if (assetOutputUri == null)
        {
            Toast.MakeText(this, "Chọn thư mục xuất kết quả trước.", ToastLength.Long)!.Show();
            return;
        }

        var form = new LinearLayout(this) { Orientation = Orientation.Vertical };
        form.SetPadding(Dp(18), Dp(8), Dp(18), Dp(8));
        form.AddView(Text($"Thư mục nguồn: {Path.GetFileName(assetInputLocal.TrimEnd(Path.DirectorySeparatorChar))}", 12, "#64748B"));
        var mapName = new EditText(this) { Hint = "Tên CABMap / AssetMap" };
        form.AddView(mapName, new LinearLayout.LayoutParams(-1, -2));
        string[] outputs = { "Chỉ CABMap (.bin)", "CABMap + AssetMap" };
        int outputMode = 0;
        string[] formats = { "MessagePack", "XML", "JSON", "Cả 3 định dạng" };
        int formatMode = 0;

        var outputSpinner = new Spinner(this);
        outputSpinner.Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerItem, outputs);
        form.AddView(outputSpinner, new LinearLayout.LayoutParams(-1, -2));
        var formatSpinner = new Spinner(this);
        formatSpinner.Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerItem, formats);
        form.AddView(formatSpinner, new LinearLayout.LayoutParams(-1, -2));

        outputSpinner.ItemSelected += (_, e) => outputMode = e.Position;
        formatSpinner.ItemSelected += (_, e) => formatMode = e.Position;

        new AlertDialog.Builder(this)
            .SetTitle("BUILD CABMAP / BOTH")
            .SetView(form)
            .SetNegativeButton("HỦY", (_, _) => { })
            .SetPositiveButton("BUILD", async (_, _) =>
            {
                string name = (mapName.Text ?? string.Empty).Trim();
                if (name.Length == 0)
                {
                    Toast.MakeText(this, "Nhập tên map trước.", ToastLength.Long)!.Show();
                    return;
                }
                await BuildCabMapAsync(assetInputLocal!, name, outputMode, formatMode);
            })
            .Show();
    }

    private async Task BuildCabMapAsync(string inputFolder, string mapName, int outputMode, int formatMode)
    {
        if (assetOutputUri == null) return;
        await RunBusyAsync(async token =>
        {
            string root = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "studio_cabmap");
            if (Directory.Exists(root)) Directory.Delete(root, true);
            Directory.CreateDirectory(root);
            string appData = Path.Combine(FilesDir!.AbsolutePath, "Studio-HoK");
            StudioMobileMapService.Initialize(appData);
            string safeMapName = string.Concat(mapName.Select(ch =>
                Path.GetInvalidFileNameChars().Contains(ch) || ch == '/' || ch == (char)92 ? '_' : ch)).Trim();
            if (safeMapName.Length == 0) throw new InvalidDataException("Tên map không hợp lệ.");

            string[] files = Directory.EnumerateFiles(inputFolder, "*", SearchOption.AllDirectories).ToArray();
            if (files.Length == 0) throw new InvalidDataException("Thư mục game không có file nào.");

            if (outputMode == 0)
            {
                WriteStatus($"Đang build CABMap từ {files.Length:N0} file…");
                string cab = await StudioMobileMapService.BuildCABMapAsync(inputFolder, safeMapName, mobileSettings.selectedGame);
                File.Copy(cab, Path.Combine(root, safeMapName + ".bin"), true);
            }
            else
            {
                AssetStudio.ExportListType type = formatMode switch
                {
                    0 => AssetStudio.ExportListType.MessagePack,
                    1 => AssetStudio.ExportListType.XML,
                    2 => AssetStudio.ExportListType.JSON,
                    _ => AssetStudio.ExportListType.MessagePack | AssetStudio.ExportListType.XML | AssetStudio.ExportListType.JSON
                };
                string maps = Path.Combine(root, "AssetMap");
                Directory.CreateDirectory(maps);
                WriteStatus($"Đang build CABMap + AssetMap từ {files.Length:N0} file…");
                var results = await StudioMobileMapService.BuildBothAsync(
                    inputFolder, safeMapName, mobileSettings.selectedGame, maps, type);
                foreach (string file in results)
                {
                    string name = Path.GetFileName(file);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    string destination = Path.Combine(root, name);
                    if (!string.Equals(Path.GetFullPath(file), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                        File.Copy(file, destination, true);
                }
            }

            var generated = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToArray();
            if (generated.Length == 0) throw new InvalidDataException("Không có file map nào được tạo.");
            await SafTree.CopyLocalToTreeAsync(
                this, root, assetOutputUri!,
                new Progress<(int done, string name)>(p => WriteStatus($"Đang chép map {p.done}: {p.name}")),
                token);
            WriteStatus($"Hoàn tất map: {generated.Length:N0} file. File gốc không bị thay đổi.");
            Toast.MakeText(this, $"Đã xuất {generated.Length:N0} file map.", ToastLength.Long)!.Show();
        });
    }

"""
if "private void ShowCabMapBuilder()" not in s:
    if s.count(anchor) != 1:
        raise SystemExit("Could not locate ScanAssetsAsync for CABMap handlers")
    s = s.replace(anchor, methods + anchor, 1)

p.write_text(s, encoding="utf-8")
print("Added CABMap-only and combined CABMap + AssetMap builders.")
