from pathlib import Path

p = Path("mobile/MainActivity.cs")
if not p.is_file():
    raise SystemExit("mobile/MainActivity.cs missing after restoring Android payload")
s = p.read_text(encoding="utf-8")

old_const = "private const int PickDbInput = 1001, PickDbOutput = 1002, PickAssetInput = 1003, PickAssetOutput = 1004;"
new_const = "private const int PickDbInput = 1001, PickDbOutput = 1002, PickAssetInput = 1003, PickAssetOutput = 1004, PickTextureImageRequest = 1005, PickAudioFileRequest = 1006, PickTextureRawRequest = 1007, PickMeshObjRequest = 1008;"
if old_const in s:
    s = s.replace(old_const, new_const, 1)
elif new_const not in s:
    raise SystemExit("Could not find picker request-code declaration")

preview = '        AddButton(assetCard, "XEM ẢNH / THÔNG TIN ASSET ĐÃ CHỌN", "#E8EEF8", "#1E3A5F", () => { ShowSelectedAsset(); return Task.CompletedTask; });'
buttons = preview + '''
        AddButton(assetCard, "SỬA TEXTURE2D TỪ PNG (ĐỊNH DẠNG UNCOMPRESSED)", "#7C3AED", "#FFFFFF", () => { PickTextureImage(); return Task.CompletedTask; });
        AddButton(assetCard, "THAY TEXTURE RAW (HỖ TRỢ ASTC / ETC / DXT)", "#7C3AED", "#FFFFFF", () => { PickTextureRaw(); return Task.CompletedTask; });
        AddButton(assetCard, "NHẬP OBJ (CHỈ SỬA VỊ TRÍ VERTEX)", "#7C3AED", "#FFFFFF", () => { PickMeshObj(); return Task.CompletedTask; });
        AddButton(assetCard, "SỬA SHADER SCRIPT (NẾU CÓ MÃ NGUỒN)", "#7C3AED", "#FFFFFF", () => { EditSelectedShader(); return Task.CompletedTask; });
        AddButton(assetCard, "SỬA MATERIAL FLOAT / COLOR", "#7C3AED", "#FFFFFF", () => { EditSelectedMaterial(); return Task.CompletedTask; });
        AddButton(assetCard, "THAY AUDIOCLIP (CÙNG KÍCH THƯỚC / ĐỊNH DẠNG)", "#7C3AED", "#FFFFFF", () => { PickAudioFile(); return Task.CompletedTask; });'''
if 'PickTextureImage(); return Task.CompletedTask;' not in s:
    if s.count(preview) != 1:
        raise SystemExit("Could not uniquely locate asset preview action")
    s = s.replace(preview, buttons, 1)

switch_anchor = '            case PickAssetOutput: assetOutputUri = uri; assetOutputText!.Text = "Đích: " + uri.LastPathSegment; break;'
switch_case = switch_anchor + '''
            case PickTextureImageRequest: _ = ReplaceSelectedTextureAsync(uri); break;
            case PickTextureRawRequest: _ = ReplaceSelectedTextureRawAsync(uri); break;
            case PickMeshObjRequest: _ = ReplaceSelectedMeshAsync(uri); break;
            case PickAudioFileRequest: _ = ReplaceSelectedAudioAsync(uri); break;'''
if "case PickTextureImageRequest: _ = ReplaceSelectedTextureAsync(uri); break;" not in s:
    if s.count(switch_anchor) != 1:
        raise SystemExit("Could not uniquely locate OnActivityResult switch")
    s = s.replace(switch_anchor, switch_case, 1)

method_anchor = "    private void ShowSelectedAsset()"
methods = r"""


    private void PickMeshObj()
    {
        var selected = GetSelectedAssets();
        if (selected.Count != 1 || selected[0].Instance is not AssetStudio.Mesh)
        {
            Toast.MakeText(this, "Chọn đúng một Mesh trước khi nhập OBJ.", ToastLength.Long)!.Show();
            return;
        }
        if (assetOutputUri == null)
        {
            Toast.MakeText(this, "Chọn thư mục xuất asset trước.", ToastLength.Long)!.Show();
            return;
        }
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*");
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);
        StartActivityForResult(intent, PickMeshObjRequest);
    }

    private async Task ReplaceSelectedMeshAsync(Uri objUri)
    {
        var selected = GetSelectedAssets();
        if (selected.Count != 1 || selected[0].Instance is not AssetStudio.Mesh mesh)
        {
            Toast.MakeText(this, "Mesh đã chọn không còn hợp lệ.", ToastLength.Long)!.Show();
            return;
        }
        if (assetOutputUri == null) return;
        await RunBusyAsync(async token =>
        {
            string work = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "mesh_obj_import");
            Directory.CreateDirectory(work);
            string input = Path.Combine(work, "replacement.obj");
            using (var source = ContentResolver!.OpenInputStream(objUri)
                ?? throw new IOException("Không mở được OBJ từ trình quản lý tệp."))
            await using (var destination = new FileStream(input, FileMode.Create, FileAccess.Write, FileShare.None))
                await source.CopyToAsync(destination, 1024 * 1024, token);
            var info = new FileInfo(input);
            if (info.Length == 0 || info.Length > 128L * 1024 * 1024)
                throw new InvalidDataException("OBJ rỗng hoặc vượt giới hạn 128 MiB.");
            string output = Path.Combine(work, "patched");
            if (Directory.Exists(output)) Directory.Delete(output, true);
            Directory.CreateDirectory(output);
            string written = await Task.Run(() => MeshPositionPatcher.CreatePatchedCopy(mesh, input, output), token);
            await SafTree.CopyLocalToTreeAsync(this, output, assetOutputUri!,
                new Progress<(int done, string name)>(p => WriteStatus($"Đang xuất Mesh {p.done}: {p.name}")), token);
            WriteStatus("Đã tạo bản Mesh chỉnh vertex: " + Path.GetFileName(written) +
                ". Chỉ hỗ trợ Mesh vertex buffer inline hiện đại, position float32 và giữ nguyên số/thứ tự vertex; topology, UV, normal, rig và file gốc không đổi.");
        });
    }

    private void PickTextureRaw()
    {
        var selected = GetSelectedAssets();
        if (selected.Count != 1 || selected[0].Instance is not AssetStudio.Texture2D)
        {
            Toast.MakeText(this, "Chọn đúng một Texture2D trước khi nhập raw.", ToastLength.Long)!.Show();
            return;
        }
        if (assetOutputUri == null)
        {
            Toast.MakeText(this, "Chọn thư mục xuất asset trước.", ToastLength.Long)!.Show();
            return;
        }
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*");
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);
        StartActivityForResult(intent, PickTextureRawRequest);
    }

    private async Task ReplaceSelectedTextureRawAsync(Uri rawUri)
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
            string work = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "texture_raw_import");
            Directory.CreateDirectory(work);
            string input = Path.Combine(work, "replacement.texture.raw");
            using (var source = ContentResolver!.OpenInputStream(rawUri)
                ?? throw new IOException("Không mở được raw texture từ trình quản lý tệp."))
            await using (var destination = new FileStream(input, FileMode.Create, FileAccess.Write, FileShare.None))
                await source.CopyToAsync(destination, 1024 * 1024, token);
            var info = new FileInfo(input);
            long expected = texture.image_data?.Size ?? 0;
            if (info.Length == 0 || info.Length != expected)
                throw new InvalidDataException($"Raw texture phải đúng {expected} byte; nhận {info.Length} byte.");
            string output = Path.Combine(work, "patched");
            if (Directory.Exists(output)) Directory.Delete(output, true);
            Directory.CreateDirectory(output);
            string written = await Task.Run(
                () => Texture2DRawImporter.CreatePatchedCopy(texture, input, output), token);
            await SafTree.CopyLocalToTreeAsync(this, output, assetOutputUri!,
                new Progress<(int done, string name)>(p => WriteStatus($"Đang xuất raw Texture2D {p.done}: {p.name}")), token);
            WriteStatus("Đã thay raw Texture2D: " + Path.GetFileName(written) +
                ". Hỗ trợ payload nén ASTC/ETC/DXT hoặc định dạng khác nếu raw đã mã hóa đúng định dạng và đúng kích thước; file .resS ngoài chưa hỗ trợ.");
        });
    }

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
        StartActivityForResult(intent, PickTextureImageRequest);
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
                ". Chỉ hỗ trợ PNG cùng kích thước, texture inline RGBA32/RGB24/BGRA32/ARGB32/RGB565/ARGB4444/RGBA4444/Alpha8, không mipmap; file gốc được giữ nguyên.");
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


    private void PickAudioFile()
    {
        var selected = GetSelectedAssets();
        if (selected.Count != 1 || selected[0].Instance is not AssetStudio.AudioClip)
        {
            Toast.MakeText(this, "Chọn đúng một AudioClip trước khi thay âm thanh.", ToastLength.Long)!.Show();
            return;
        }
        if (assetOutputUri == null)
        {
            Toast.MakeText(this, "Chọn thư mục xuất asset trước.", ToastLength.Long)!.Show();
            return;
        }
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("audio/*");
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);
        StartActivityForResult(intent, PickAudioFileRequest);
    }

    private async Task ReplaceSelectedAudioAsync(Uri audioUri)
    {
        var selected = GetSelectedAssets();
        if (selected.Count != 1 || selected[0].Instance is not AssetStudio.AudioClip audio)
        {
            Toast.MakeText(this, "AudioClip đã chọn không còn hợp lệ.", ToastLength.Long)!.Show();
            return;
        }
        if (assetOutputUri == null) return;
        await RunBusyAsync(async token =>
        {
            string work = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "audio_import");
            Directory.CreateDirectory(work);
            string input = Path.Combine(work, "replacement.audio");
            using (var source = ContentResolver!.OpenInputStream(audioUri)
                ?? throw new IOException("Không mở được audio từ trình quản lý tệp."))
            await using (var destination = new FileStream(input, FileMode.Create, FileAccess.Write, FileShare.None))
                await source.CopyToAsync(destination, 1024 * 1024, token);
            var info = new FileInfo(input);
            if (info.Length == 0 || info.Length > 128L * 1024 * 1024)
                throw new InvalidDataException("Audio rỗng hoặc vượt giới hạn 128 MiB.");
            string output = Path.Combine(work, "patched");
            if (Directory.Exists(output)) Directory.Delete(output, true);
            Directory.CreateDirectory(output);
            string written = await Task.Run(() => AudioClipPatcher.CreatePatchedCopy(audio, input, output), token);
            await SafTree.CopyLocalToTreeAsync(this, output, assetOutputUri!,
                new Progress<(int done, string name)>(p => WriteStatus($"Đang xuất AudioClip {p.done}: {p.name}")), token);
            WriteStatus("Đã tạo bản AudioClip đã sửa: " + Path.GetFileName(written) +
                ". Chỉ hỗ trợ dữ liệu inline, cùng kích thước và cùng định dạng OGG/FSB5/RIFF/MP3/MP4; file gốc được giữ nguyên.");
        });
    }

    private void EditSelectedMaterial()
    {
        var selected = GetSelectedAssets();
        if (selected.Count != 1 || selected[0].Instance is not AssetStudio.Material material)
        {
            Toast.MakeText(this, "Chọn đúng một Material trước khi sửa.", ToastLength.Long)!.Show();
            return;
        }
        if (assetOutputUri == null)
        {
            Toast.MakeText(this, "Chọn thư mục xuất asset trước.", ToastLength.Long)!.Show();
            return;
        }
        var initial = new
        {
            floats = material.m_SavedProperties.m_Floats.ToDictionary(x => x.Key, x => x.Value),
            colors = material.m_SavedProperties.m_Colors.ToDictionary(
                x => x.Key, x => new[] { x.Value.R, x.Value.G, x.Value.B, x.Value.A })
        };
        string json = global::Newtonsoft.Json.JsonConvert.SerializeObject(initial, global::Newtonsoft.Json.Formatting.Indented);
        var editor = new EditText(this) { Text = json, TextSize = 12 };
        editor.SetSingleLine(false);
        editor.SetMinLines(12);
        editor.Gravity = GravityFlags.Top;
        editor.InputType = global::Android.Text.InputTypes.ClassText | global::Android.Text.InputTypes.TextFlagMultiLine;
        var scroll = new ScrollView(this);
        scroll.SetPadding(Dp(10), Dp(8), Dp(10), Dp(8));
        scroll.AddView(editor);
        new AlertDialog.Builder(this)
            .SetTitle("Sửa Material · " + material.m_Name)
            .SetMessage("Sửa giá trị trong floats và colors. Chỉ thay property đã tồn tại; không thêm/xóa texture slot hoặc keyword. File gốc luôn được giữ nguyên.")
            .SetView(scroll)
            .SetNegativeButton("HỦY", (_, _) => { })
            .SetPositiveButton("TẠO BẢN MATERIAL", async (_, _) => await SaveMaterialPropertyEditAsync(material, editor.Text ?? "{}"))
            .Show();
    }

    private async Task SaveMaterialPropertyEditAsync(AssetStudio.Material material, string replacementJson)
    {
        await RunBusyAsync(async token =>
        {
            string parent = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "material_edits");
            Directory.CreateDirectory(parent);
            string output = Path.Combine(parent, "Edited_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(output);
            string written = await Task.Run(
                () => MaterialPropertyPatcher.CreatePatchedCopy(material, replacementJson, output), token);
            await SafTree.CopyLocalToTreeAsync(this, output, assetOutputUri!,
                new Progress<(int done, string name)>(p => WriteStatus($"Đang xuất Material {p.done}: {p.name}")), token);
            WriteStatus("Đã tạo bản Material đã sửa: " + Path.GetFileName(written) +
                ". Chỉ sửa float/color có sẵn; shader, texture pointer và keyword được giữ nguyên.");
        });
    }

"""
if "private void PickTextureImage()" not in s:
    if s.count(method_anchor) != 1:
        raise SystemExit("Could not uniquely locate ShowSelectedAsset insertion point")
    s = s.replace(method_anchor, methods + method_anchor, 1)

p.write_text(s, encoding="utf-8")
print("Integrated safe Texture2D PNG import, Shader script editing, and Material float/color editing")
