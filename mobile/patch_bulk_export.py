from pathlib import Path

p = Path("mobile/MainActivity.cs")
if not p.is_file():
    raise SystemExit("mobile/MainActivity.cs missing after source restore")
s = p.read_text(encoding="utf-8")

anchor = "    private async Task ScanAssetsAsync()"
methods = r"""
    private async Task ExportAllStudioAssetsAsync()
    {
        if (assetOutputUri == null)
        {
            Toast.MakeText(this, "Chọn thư mục xuất asset trước.", ToastLength.Long)!.Show();
            return;
        }

        await RunBusyAsync(async token =>
        {
            string output = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "studio_export_all");
            if (Directory.Exists(output)) Directory.Delete(output, true);
            Directory.CreateDirectory(output);
            mobileSettings.ApplyCoreSettings();
            var progress = new Progress<string>(message => WriteStatus(message));
            var result = await Task.Run(
                () => assetBrowser.ExportAllAssets(output, mobileSettings, token, progress), token);

            if (result.Exported > 0)
                await SafTree.CopyLocalToTreeAsync(
                    this, output, assetOutputUri!,
                    new Progress<(int done, string name)>(p => WriteStatus($"Đang chép asset {p.done}: {p.name}")),
                    token);

            WriteStatus($"Export All: {result.Exported:N0} thành công / {result.Skipped:N0} bỏ qua / {result.Errors.Count:N0} lỗi.");
            if (result.Errors.Count > 0)
                WriteStatus(string.Join("\n", result.Errors.Take(16)));
            Toast.MakeText(this, $"Export All: {result.Exported:N0} asset.", ToastLength.Long)!.Show();
        });
    }

    private async Task ExportSelectedStudioAssetsAsync()
    {
        var selected = GetSelectedAssets();
        if (selected.Count == 0)
        {
            Toast.MakeText(this, "Chọn ít nhất một asset trong danh sách trước.", ToastLength.Long)!.Show();
            return;
        }
        if (assetOutputUri == null)
        {
            Toast.MakeText(this, "Chọn thư mục xuất asset trước.", ToastLength.Long)!.Show();
            return;
        }

        await RunBusyAsync(async token =>
        {
            string output = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "studio_export_selected");
            if (Directory.Exists(output)) Directory.Delete(output, true);
            Directory.CreateDirectory(output);
            mobileSettings.ApplyCoreSettings();
            var progress = new Progress<string>(message => WriteStatus(message));
            var result = await Task.Run(
                () => assetBrowser.ExportSelectedAssets(selected, output, mobileSettings, token, progress), token);

            if (result.Exported > 0)
                await SafTree.CopyLocalToTreeAsync(
                    this, output, assetOutputUri!,
                    new Progress<(int done, string name)>(p => WriteStatus($"Đang chép asset đã chọn {p.done}: {p.name}")),
                    token);

            WriteStatus($"Export Selected: {result.Exported:N0} thành công / {result.Skipped:N0} bỏ qua / {result.Errors.Count:N0} lỗi.");
            if (result.Errors.Count > 0)
                WriteStatus(string.Join("\n", result.Errors.Take(16)));
            Toast.MakeText(this, $"Export Selected: {result.Exported:N0} asset.", ToastLength.Long)!.Show();
        });
    }

"""
if "private async Task ExportAllStudioAssetsAsync()" not in s:
    if s.count(anchor) != 1:
        raise SystemExit("Could not locate ScanAssetsAsync for bulk export handlers")
    s = s.replace(anchor, methods + anchor, 1)

p.write_text(s, encoding="utf-8")
print("Added Export All and Export Selected handlers.")
