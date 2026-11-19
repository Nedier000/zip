from pathlib import Path

p = Path("mobile/MainActivity.cs")
if not p.is_file():
    raise SystemExit("mobile/MainActivity.cs not found after restoring Android source payload")
source = p.read_text(encoding="utf-8")

button_anchor = '        AddButton(assetCard, "XEM ẢNH / THÔNG TIN ASSET ĐÃ CHỌN", "#E8EEF8", "#1E3A5F", () => { ShowSelectedAsset(); return Task.CompletedTask; });'
button_line = '        AddButton(assetCard, "SỬA TEXTASSET (TẠO BẢN .ASSETS RIÊNG)", "#7C3AED", "#FFFFFF", () => { EditSelectedTextAsset(); return Task.CompletedTask; });'
if button_line not in source:
    if source.count(button_anchor) != 1:
        raise SystemExit("Could not uniquely locate the asset preview button in MainActivity.cs")
    source = source.replace(button_anchor, button_anchor + "\n" + button_line, 1)

method_anchor = "    private void ShowSelectedAsset()"
method = r"""
    private void EditSelectedTextAsset()
    {
        var selected = GetSelectedAssets();
        if (selected.Count != 1 || selected[0].Instance is not AssetStudio.TextAsset textAsset)
        {
            Toast.MakeText(this, "Chọn đúng một asset có loại TextAsset.", ToastLength.Long)!.Show();
            return;
        }
        if (assetOutputUri == null)
        {
            Toast.MakeText(this, "Chọn thư mục xuất asset trước.", ToastLength.Long)!.Show();
            return;
        }

        byte[] originalBytes = textAsset.m_Script ?? Array.Empty<byte>();
        string original;
        try
        {
            original = new global::System.Text.UTF8Encoding(false, true).GetString(originalBytes);
        }
        catch (global::System.Text.DecoderFallbackException)
        {
            Toast.MakeText(this, "TextAsset này chứa dữ liệu nhị phân hoặc UTF-8 lỗi; trình sửa văn bản từ chối để tránh làm hỏng byte.", ToastLength.Long)!.Show();
            return;
        }
        if (original.IndexOf('\0') >= 0)
        {
            Toast.MakeText(this, "TextAsset có byte NUL; không dùng trình sửa văn bản cho dữ liệu này.", ToastLength.Long)!.Show();
            return;
        }

        var editor = new EditText(this) { Text = original, TextSize = 13 };
        editor.SetSingleLine(false);
        editor.SetMinLines(8);
        editor.Gravity = GravityFlags.Top;
        editor.InputType = global::Android.Text.InputTypes.ClassText | global::Android.Text.InputTypes.TextFlagMultiLine;

        var scroll = new ScrollView(this);
        scroll.SetPadding(Dp(10), Dp(8), Dp(10), Dp(8));
        scroll.AddView(editor);

        new AlertDialog.Builder(this)
            .SetTitle("Sửa TextAsset · " + textAsset.m_Name)
            .SetMessage("Chỉ áp dụng cho SerializedFile độc lập. Nội dung mới không được dài hơn dữ liệu gốc; file nguồn luôn được giữ nguyên.")
            .SetView(scroll)
            .SetNegativeButton("HỦY", (_, _) => { })
            .SetPositiveButton("TẠO BẢN .ASSETS", async (_, _) =>
                await SaveTextAssetEditAsync(textAsset, editor.Text ?? string.Empty))
            .Show();
    }

    private async Task SaveTextAssetEditAsync(AssetStudio.TextAsset textAsset, string replacement)
    {
        await RunBusyAsync(async token =>
        {
            string parent = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "text_asset_edits");
            Directory.CreateDirectory(parent);
            string output = Path.Combine(parent, "Edited_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(output);

            string written = await Task.Run(
                () => SerializedTextAssetPatcher.CreatePatchedCopy(textAsset, replacement, output), token);

            await SafTree.CopyLocalToTreeAsync(
                this, output, assetOutputUri!,
                new Progress<(int done, string name)>(p => WriteStatus($"Đang xuất bản sửa {p.done}: {p.name}")),
                token);

            WriteStatus("Đã tạo bản sửa độc lập: " + Path.GetFileName(written) +
                "\nFile gốc không bị thay đổi. Nếu asset ban đầu nằm trong AssetBundle, chức năng sẽ từ chối ghi ngược.");
        });
    }

"""
if method_anchor not in source:
    raise SystemExit("Could not uniquely locate ShowSelectedAsset() method insertion point")
if "private void EditSelectedTextAsset()" not in source:
    if source.count(method_anchor) != 1:
        raise SystemExit("ShowSelectedAsset() insertion point is ambiguous")
    source = source.replace(method_anchor, method + method_anchor, 1)

p.write_text(source, encoding="utf-8")
print("Applied safe standalone TextAsset editor patch")
