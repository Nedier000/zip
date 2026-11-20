from pathlib import Path

p = Path("mobile/MainActivity.cs")
if not p.is_file():
    raise SystemExit("mobile/MainActivity.cs missing after source restore")
s = p.read_text(encoding="utf-8")

scan_button = '        scanButton = AddButton(assetCard, "QUÉT / ĐỌC ASSET", "#1D4ED8", "#FFFFFF", async () => await ScanAssetsAsync());'
extract_button = scan_button + '''
        AddButton(assetCard, "EXTRACT FILE / FOLDER CONTAINERS", "#0F766E", "#FFFFFF",
            async () => await ExtractContainersAsync());'''
if "ExtractContainersAsync()" not in s:
    if s.count(scan_button) != 1:
        raise SystemExit("Could not locate Scan Assets button")
    s = s.replace(scan_button, extract_button, 1)

anchor = "    private async Task ScanAssetsAsync()"
method = r"""
    private async Task ExtractContainersAsync()
    {
        if (assetInputUri == null && string.IsNullOrWhiteSpace(assetInputLocal))
        {
            Toast.MakeText(this, "Chọn một thư mục hoặc file container trước.", ToastLength.Long)!.Show();
            return;
        }
        if (assetOutputUri == null)
        {
            Toast.MakeText(this, "Chọn thư mục lưu kết quả trước.", ToastLength.Long)!.Show();
            return;
        }

        await RunBusyAsync(async token =>
        {
            string input;
            if (!string.IsNullOrWhiteSpace(assetInputLocal) && Directory.Exists(assetInputLocal))
            {
                input = assetInputLocal!;
            }
            else
            {
                input = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "container_input");
                if (Directory.Exists(input)) Directory.Delete(input, true);
                await SafTree.CopyToLocalAsync(this, assetInputUri!, input,
                    new Progress<(int done, string name)>(p => WriteStatus("Đang nhận container: " + p.name)), token);
                assetInputLocal = input;
            }

            string output = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "container_extract");
            if (Directory.Exists(output)) Directory.Delete(output, true);
            Directory.CreateDirectory(output);

            var extractor = new StudioContainerExtractor(AssetStudio.GameManager.GetGame(mobileSettings.selectedGame));
            var extractProgress = new Progress<ContainerExtractionProgress>(
                p => WriteStatus(p.Message + $" · {p.FilesWritten:N0} file"));
            var result = await Task.Run(
                () => extractor.ExtractFolder(input, output, extractProgress, token), token);

            await SafTree.CopyLocalToTreeAsync(
                this, output, assetOutputUri!,
                new Progress<(int done, string name)>(p => WriteStatus($"Đang xuất container {p.done}: {p.name}")),
                token);

            WriteStatus($"Extract hoàn tất: {result.FilesWritten:N0} file, {FormatBytes(result.BytesWritten)}." +
                (result.Warnings.Count > 0 ? "\nCảnh báo:\n" + string.Join("\n", result.Warnings.Take(12)) : string.Empty));
        });
    }

"""
if "private async Task ExtractContainersAsync()" not in s:
    if s.count(anchor) != 1:
        raise SystemExit("Could not locate ScanAssetsAsync insertion point")
    s = s.replace(anchor, method + anchor, 1)

p.write_text(s, encoding="utf-8")
print("Connected Studio-style Extract file/folder workflow to Android SAF.")
