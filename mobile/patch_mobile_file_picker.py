from pathlib import Path

p = Path("mobile/MainActivity.cs")
if not p.is_file():
    raise SystemExit("mobile/MainActivity.cs not found after restoring source payload")
s = p.read_text(encoding="utf-8")

old_const = "private const int PickDbInput = 1001, PickDbOutput = 1002, PickAssetInput = 1003, PickAssetOutput = 1004;"
new_const = "private const int PickDbInput = 1001, PickDbOutput = 1002, PickAssetInput = 1003, PickAssetOutput = 1004, PickAssetFiles = 1005;"
if old_const in s:
    s = s.replace(old_const, new_const, 1)
elif new_const not in s:
    raise SystemExit("Could not locate MainActivity picker request codes")

folder_button = '        AddButton(assetCard, "CHỌN THƯ MỤC ASSET", "#E8EEF8", "#1E3A5F", () => PickFolder(PickAssetInput));'
file_button = folder_button + '''
        AddButton(assetCard, "CHỌN FILE(S) UNITY / ASSETBUNDLE", "#E8EEF8", "#1E3A5F",
            () => { PickAssetFiles(); return Task.CompletedTask; });'''
if "PickAssetFiles();" not in s:
    if s.count(folder_button) != 1:
        raise SystemExit("Could not locate Asset folder picker button")
    s = s.replace(folder_button, file_button, 1)

old_guard = """        if (resultCode != Result.Ok || data?.Data == null) return;
        var uri = data.Data;
        try
        {
            var flags = data.Flags & (ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
            ContentResolver!.TakePersistableUriPermission(uri, flags);
        }
        catch { }

        switch (requestCode)"""
new_guard = """        if (resultCode != Result.Ok || data == null) return;
        var uri = data.Data;
        var flags = data.Flags & (ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);

        if (requestCode == PickAssetFiles)
        {
            var selectedUris = new List<Uri>();
            if (data.ClipData != null)
            {
                for (int i = 0; i < data.ClipData.ItemCount; i++)
                {
                    var itemUri = data.ClipData.GetItemAt(i)?.Uri;
                    if (itemUri != null) selectedUris.Add(itemUri);
                }
            }
            else if (uri != null)
            {
                selectedUris.Add(uri);
            }

            foreach (var itemUri in selectedUris)
            {
                try { ContentResolver!.TakePersistableUriPermission(itemUri, flags); }
                catch { }
            }
            _ = LoadAssetFilesAsync(selectedUris);
            return;
        }

        if (uri == null) return;
        try { ContentResolver!.TakePersistableUriPermission(uri, flags); }
        catch { }

        switch (requestCode)"""
if old_guard in s:
    s = s.replace(old_guard, new_guard, 1)
elif "if (requestCode == PickAssetFiles)" not in s:
    raise SystemExit("Could not locate OnActivityResult document URI guard")

old_asset_case = '            case PickAssetInput: assetInputUri = uri; assetInputText!.Text = "Nguồn: " + uri.LastPathSegment; break;'
new_asset_case = '            case PickAssetInput: assetInputUri = uri; assetInputLocal = null; assetInputText!.Text = "Nguồn: " + uri.LastPathSegment; break;'
if old_asset_case in s:
    s = s.replace(old_asset_case, new_asset_case, 1)
elif "case PickAssetInput: assetInputUri = uri; assetInputLocal = null;" not in s:
    raise SystemExit("Could not locate Asset folder result handler")

pick_anchor = "    private void PickFolder(int requestCode)"
pick_method = """    private void PickAssetFiles()
    {
        if (busy) return;
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*");
        intent.PutExtra(Intent.ExtraAllowMultiple, true);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);
        StartActivityForResult(intent, PickAssetFiles);
    }

    private async Task LoadAssetFilesAsync(IReadOnlyList<Uri> uris)
    {
        if (uris == null || uris.Count == 0) return;
        assetInputUri = null;
        assetInputLocal = null;

        await RunBusyAsync(async token =>
        {
            string input = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "asset_input_files");
            if (Directory.Exists(input)) Directory.Delete(input, true);
            Directory.CreateDirectory(input);
            var copied = await StudioMobileFileAccess.CopyUrisToLocalAsync(
                this, uris, input,
                new Progress<(int done, string name)>(p => WriteStatus($"Đang nhận file {p.done}: {p.name}")),
                token);
            if (copied.Count == 0) throw new InvalidDataException("Không có file hợp lệ được sao chép.");
            assetInputLocal = input;
            assetInputText!.Text = $"Đã chọn {copied.Count:N0} file Unity / AssetBundle";
        });

        if (!string.IsNullOrWhiteSpace(assetInputLocal))
            await ScanAssetsAsync();
    }

"""
if "private async Task LoadAssetFilesAsync(" not in s:
    if s.count(pick_anchor) != 1:
        raise SystemExit("Could not locate PickFolder method insertion point")
    s = s.replace(pick_anchor, pick_method + pick_anchor, 1)

old_scan_guard = """        if (assetInputUri == null)
        {
            Toast.MakeText(this, "Chọn thư mục AssetBundle trước.", ToastLength.Long)!.Show();
            return;
        }"""
new_scan_guard = """        if (assetInputUri == null && string.IsNullOrWhiteSpace(assetInputLocal))
        {
            Toast.MakeText(this, "Chọn một thư mục hoặc một/nhiều file AssetBundle trước.", ToastLength.Long)!.Show();
            return;
        }"""
if old_scan_guard in s:
    s = s.replace(old_scan_guard, new_scan_guard, 1)
elif "assetInputUri == null && string.IsNullOrWhiteSpace(assetInputLocal)" not in s:
    raise SystemExit("Could not locate ScanAssetsAsync input guard")

old_copy = """            string input = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "asset_input");
            WriteStatus("Đang chuẩn bị thư mục AssetStudio…");
            await SafTree.CopyToLocalAsync(this, assetInputUri!, input, null, token);
            assetInputLocal = input;"""
new_copy = """            string input;
            if (assetInputUri != null)
            {
                input = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "asset_input");
                WriteStatus("Đang sao chép thư mục vào vùng làm việc AssetStudio…");
                await SafTree.CopyToLocalAsync(this, assetInputUri, input, null, token);
                assetInputLocal = input;
            }
            else
            {
                input = assetInputLocal!;
                if (!Directory.Exists(input) || !Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories).Any())
                    throw new InvalidDataException("Thư mục file đã chọn đang trống hoặc không còn tồn tại.");
            }"""
if old_copy in s:
    s = s.replace(old_copy, new_copy, 1)
elif "Thư mục file đã chọn đang trống" not in s:
    raise SystemExit("Could not locate ScanAssetsAsync SAF folder-copy block")

p.write_text(s, encoding="utf-8")
print("Added SAF multi-file loading alongside the existing folder workflow.")
