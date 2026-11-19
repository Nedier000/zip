from pathlib import Path

p = Path("mobile/MainActivity.cs")
if not p.is_file():
    raise SystemExit("mobile/MainActivity.cs missing after restoring Android payload")
s = p.read_text(encoding="utf-8")

# Request code and UI actions.
old_const = "private const int PickDbInput = 1001, PickDbOutput = 1002, PickAssetInput = 1003, PickAssetOutput = 1004;"
new_const = "private const int PickDbInput = 1001, PickDbOutput = 1002, PickAssetInput = 1003, PickAssetOutput = 1004, PickTextureImage = 1005;"
if old_const in s:
    s = s.replace(old_const, new_const, 1)
elif new_const not in s:
    raise SystemExit("Could not find MainActivity picker request-code declaration")

preview = '        AddButton(assetCard, "XEM ẢNH / THÔNG TIN ASSET ĐÃ CHỌN", "#E8EEF8", "#1E3A5F", () => { ShowSelectedAsset(); return Task.CompletedTask; });'
buttons = preview + '''
        AddButton(assetCard, "SỬA TEXTURE2D TỪ PNG (RGBA32 / RGB24 / BGRA32)", "#7C3AED", "#FFFFFF", () => { PickTextureImage(); return Task.CompletedTask; });
        AddButton(assetCard, "SỬA SHADER SCRIPT (NẾU CÓ MÃ NGUỒN)", "#7C3AED", "#FFFFFF", () => { EditSelectedShader(); return Task.CompletedTask; });'''
if 'PickTextureImage(); return Task.CompletedTask;' not in s:
    if s.count(preview) != 1:
        raise SystemExit("Could not uniquely locate asset preview action")
    s = s.replace(preview, buttons, 1)

switch_anchor = '            case PickAssetOutput: assetOutputUri = uri; assetOutputText!.Text = "Đích: " + uri.LastPathSegment; break;'
switch_case = switch_anchor + '''
            case PickTextureImage: _ = ReplaceSelectedTextureAsync(uri); break;'''
if "case PickTextureImage: _ = ReplaceSelectedTextureAsync(uri); break;" not in s:
    if s.count(switch_anchor) != 1:
        raise SystemExit("Could not uniquely locate OnActivityResult switch")
    s = s.replace(switch_anchor, switch_case, 1)

method_anchor = "    private void ShowSelectedAsset()"
methods = r"""
    private void PickTextureImage()
    {
        var selected = GetSelectedAssets();
        if (selected.Count != 1 || selected[0].Instance is not AssetStudio.Texture2D)
        {
            Toast.MakeText(this, "Chọn đúng một Texture2D trước khi nhập PNG.", ToastLength.Long)!.Show();
            return;
        }
        if (assetOutputUri == null)
        {
            Toast.MakeText(this, "Chọn thư mục xuất asset trước.", ToastLength.Long)!.Show();
            return;
        }
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("image/png");
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);
        StartActivityForResult(intent, PickTextureImage);
    }

    private async Task ReplaceSelectedTextureAsync(Uri imageUri)
    {
        var selected = GetSelectedAssets();
        if (selected.Count != 1 || selected[0].Instance is not AssetStudio.Texture2D texture)
        {
            Toast.MakeText(this, "Texture2D đã chọn không còn hợp lệ.", ToastLength.Long)!.Show();
            return;
        }
        if (assetOutputUri == null) return;
        await RunBusyAsync(async token =>
        {
            string work = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "texture_import");
            Directory.CreateDirectory(work);
            string png = Path.Combine(work, "replacement.png");
            using (var source = ContentResolver!.OpenInputStream(imageUri)
                ?? throw new IOException("Không mở được PNG từ trình quản lý tệp."))
            await using (var destination = new FileStream(png, FileMode.Create, FileAccess.Write, FileShare.None))
                await source.CopyToAsync(destination, 1024 * 1024, token);
            var info = new FileInfo(png);
            if (info.Length == 0 || info.Length > 128L * 1024 * 1024)
                throw new InvalidDataException("PNG rỗng hoặc vượt giới hạn 128 MiB.");
            string output = Path.Combine(work, "patched");
            if (Directory.Exists(output)) Directory.Delete(output, true);
            Directory.CreateDirectory(output);
            string written = await Task.Run(
                () => Texture2DImporter.CreatePatchedCopy(texture, png, output), token);
            await SafTree.CopyLocalToTreeAsync(this, output, assetOutputUri!,
                new Progress<(int done, string name)>(p => WriteStatus($"Đang xuất Texture2D {p.done}: {p.name}")), token);
            WriteStatus("Đã tạo bản Texture2D đã sửa: " + Path.GetFileName(written) +
                ". Chỉ hỗ trợ PNG cùng kích thước, texture inline RGBA32/RGB24/BGRA32, không mipmap; file gốc được giữ nguyên.");
        });
    }

    private void EditSelectedShader()
    {
        var selected = GetSelectedAssets();
        if (selected.Count != 1 || selected[0].Instance is not AssetStudio.Shader shader)
        {
            Toast.MakeText(this, "Chọn đúng một Shader trước khi sửa.", ToastLength.Long)!.Show();
            return;
        }
        if (assetOutputUri == null)
        {
            Toast.MakeText(this, "Chọn thư mục xuất asset trước.", ToastLength.Long)!.Show();
            return;
        }
        string original;
        try { original = new global::System.Text.UTF8Encoding(false, true).GetString(shader.m_Script ?? Array.Empty<byte>()); }
        catch (global::System.Text.DecoderFallbackException)
        {
            Toast.MakeText(this, "Shader script không phải UTF-8 văn bản; chỉ có thể xuất blob gốc.", ToastLength.Long)!.Show();
            return;
        }
        if (original.Length == 0 || original.IndexOf('\0') >= 0)
        {
            Toast.MakeText(this, "Shader này không có mã nguồn văn bản m_Script; compiled blob không được sửa bằng trình này.", ToastLength.Long)!.Show();
            return;
        }
        var editor = new EditText(this) { Text = original, TextSize = 12 };
        editor.SetSingleLine(false);
        editor.SetMinLines(10);
        editor.Gravity = GravityFlags.Top;
        editor.InputType = global::Android.Text.InputTypes.ClassText | global::Android.Text.InputTypes.TextFlagMultiLine;
        var scroll = new ScrollView(this);
        scroll.SetPadding(Dp(10), Dp(8), Dp(10), Dp(8));
        scroll.AddView(editor);
        new AlertDialog.Builder(this)
            .SetTitle("Sửa Shader script · " + shader.m_Name)
            .SetMessage("Chỉ nhận mã UTF-8 có số byte không lớn hơn m_Script gốc. Shader compiled blob/parsed form không bị thay đổi.")
            .SetView(scroll)
            .SetNegativeButton("HỦY", (_, _) => { })
            .SetPositiveButton("TẠO BẢN SHADER", async (_, _) => await SaveShaderScriptEditAsync(shader, editor.Text ?? string.Empty))
            .Show();
    }

    private async Task SaveShaderScriptEditAsync(AssetStudio.Shader shader, string replacement)
    {
        await RunBusyAsync(async token =>
        {
            string parent = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "shader_edits");
            Directory.CreateDirectory(parent);
            string output = Path.Combine(parent, "Edited_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(output);
            string written = await Task.Run(() => ShaderScriptPatcher.CreatePatchedCopy(shader, replacement, output), token);
            await SafTree.CopyLocalToTreeAsync(this, output, assetOutputUri!,
                new Progress<(int done, string name)>(p => WriteStatus($"Đang xuất Shader {p.done}: {p.name}")), token);
            WriteStatus("Đã tạo bản Shader script đã sửa: " + Path.GetFileName(written) +
                ". Chỉ hỗ trợ m_Script UTF-8 inline, không lớn hơn dữ liệu gốc; compiled blob được giữ nguyên.");
        });
    }

"""
if "private void PickTextureImage()" not in s:
    if s.count(method_anchor) != 1:
        raise SystemExit("Could not uniquely locate ShowSelectedAsset insertion point")
    s = s.replace(method_anchor, methods + method_anchor, 1)

p.write_text(s, encoding="utf-8")
print("Integrated PNG Texture2D import and safe Shader m_Script editor")
