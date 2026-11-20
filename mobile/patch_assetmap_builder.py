from pathlib import Path

p = Path("mobile/MainActivity.cs")
if not p.is_file():
    raise SystemExit("mobile/MainActivity.cs missing after source restore")
s = p.read_text(encoding="utf-8")

extract_button = '''        AddButton(assetCard, "EXTRACT FILE / FOLDER CONTAINERS", "#0F766E", "#FFFFFF",
            async () => await ExtractContainersAsync());'''
asset_map_button = extract_button + '''
        AddButton(assetCard, "BUILD / EXPORT ASSETMAP", "#0F766E", "#FFFFFF",
            () => { ShowAssetMapBuilder(); return Task.CompletedTask; });'''
if "ShowAssetMapBuilder();" not in s:
    if s.count(extract_button) != 1:
        raise SystemExit("Could not locate container extraction button")
    s = s.replace(extract_button, asset_map_button, 1)

anchor = "    private async Task ScanAssetsAsync()"
methods = r"""
    private void ShowAssetMapBuilder()
    {
        var loadedFiles = assetBrowser.GetLoadedFilePaths();
        if (loadedFiles.Length == 0)
        {
            Toast.MakeText(this, "Hãy quét Unity / AssetBundle trước để lấy danh sách SerializedFile.", ToastLength.Long)!.Show();
            return;
        }

        var form = new LinearLayout(this) { Orientation = Orientation.Vertical };
        form.SetPadding(Dp(18), Dp(8), Dp(18), Dp(8));
        form.AddView(new TextView(this)
        {
            Text = $"Nguồn: {loadedFiles.Length:N0} SerializedFile đã nạp",
            TextSize = 12
        });
        var mapName = new EditText(this) { Hint = "Tên AssetMap, ví dụ: HOK_1.64.1" };
        form.AddView(mapName, new LinearLayout.LayoutParams(-1, -2));

        var formats = new[]
        {
            "MessagePack (.map)",
            "XML (.xml)",
            "JSON (.json)",
            "Cả 3 định dạng"
        };
        int formatIndex = 0;

        new AlertDialog.Builder(this)
            .SetTitle("BUILD ASSETMAP")
            .SetView(form)
            .SetSingleChoiceItems(formats, formatIndex, (_, args) => formatIndex = args.Which)
            .SetNegativeButton("HỦY", (_, _) => { })
            .SetPositiveButton("BUILD", async (_, _) =>
            {
                string name = (mapName.Text ?? string.Empty).Trim();
                if (name.Length == 0)
                {
                    Toast.MakeText(this, "Nhập tên AssetMap trước.", ToastLength.Long)!.Show();
                    return;
                }
                if (assetOutputUri == null)
                {
                    Toast.MakeText(this, "Chọn thư mục xuất trước.", ToastLength.Long)!.Show();
                    return;
                }
                await BuildAssetMapAsync(loadedFiles, name, formatIndex);
            })
            .Show();
    }

    private async Task BuildAssetMapAsync(string[] loadedFiles, string mapName, int formatIndex)
    {
        if (assetOutputUri == null) return;
        await RunBusyAsync(async token =>
        {
            string output = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "studio_assetmap");
            if (Directory.Exists(output)) Directory.Delete(output, true);
            Directory.CreateDirectory(output);

            StudioMobileMapService.Initialize(Path.Combine(FilesDir!.AbsolutePath, "Studio-HoK"));
            AssetStudio.ExportListType type = formatIndex switch
            {
                0 => AssetStudio.ExportListType.MessagePack,
                1 => AssetStudio.ExportListType.XML,
                2 => AssetStudio.ExportListType.JSON,
                _ => AssetStudio.ExportListType.MessagePack | AssetStudio.ExportListType.XML | AssetStudio.ExportListType.JSON
            };

            WriteStatus($"Đang build AssetMap từ {loadedFiles.Length:N0} SerializedFile…");
            await Task.Run(
                () => StudioMobileMapService.BuildAssetMapAsync(
                    loadedFiles, mapName, mobileSettings.selectedGame, output, type),
                token);

            var generated = Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).ToArray();
            if (generated.Length == 0)
                throw new InvalidDataException("AssetStudio không tạo file AssetMap. Xem log để biết lỗi đọc hoặc xử lý dữ liệu.");

            await SafTree.CopyLocalToTreeAsync(
                this, output, assetOutputUri!,
                new Progress<(int done, string name)>(x => WriteStatus($"Đang chép AssetMap {x.done}: {x.name}")),
                token);
            WriteStatus($"AssetMap hoàn tất: {generated.Length:N0} file được xuất.");
            Toast.MakeText(this, $"Đã xuất {generated.Length:N0} file AssetMap.", ToastLength.Long)!.Show();
        });
    }

"""
if "private void ShowAssetMapBuilder()" not in s:
    if s.count(anchor) != 1:
        raise SystemExit("Could not locate ScanAssetsAsync to add AssetMap handlers")
    s = s.replace(anchor, methods + anchor, 1)

p.write_text(s, encoding="utf-8")
print("Added native Android AssetMap builder for MessagePack, XML and JSON.")
