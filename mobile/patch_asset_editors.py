from pathlib import Path

p = Path("mobile/MainActivity.cs")
if not p.is_file():
    raise SystemExit("mobile/MainActivity.cs missing after restoring Android payload")
s = p.read_text(encoding="utf-8")

# MainActivity shared state for the explicit Build workflow.
old_uris = "private Uri? dbInputUri, dbOutputUri, assetInputUri, assetOutputUri;"
new_uris = "private Uri? dbInputUri, dbOutputUri, assetInputUri, assetOutputUri, buildBundleUri, buildSerializedUri;"
if old_uris in s: s = s.replace(old_uris, new_uris, 1)
elif new_uris not in s: raise SystemExit("Could not locate URI state fields")

old_texts = "private TextView? statusText, dbInputText, dbOutputText, assetInputText, assetOutputText, summaryText, selectedText;"
new_texts = "private TextView? statusText, dbInputText, dbOutputText, assetInputText, assetOutputText, summaryText, selectedText, buildBundleText, buildSerializedText;"
if old_texts in s: s = s.replace(old_texts, new_texts, 1)
elif new_texts not in s: raise SystemExit("Could not locate Build status label fields")

old_list = "assetList = new ListView(this)"
new_list = "assetList = new AssetListView(this)"
if old_list in s: s = s.replace(old_list, new_list, 1)
elif new_list not in s: raise SystemExit("Could not locate AssetStudio ListView creation")

build_anchor = '        body.AddView(SectionTitle("03  ·  GHI CHÚ TÍNH NĂNG"));'
build_ui = """        body.AddView(SectionTitle("03  ·  BUILD / REPACK ASSETBUNDLE"));
        var buildCard = Card();
        buildCard.AddView(Text("Trích SerializedFile (.assets/CAB-*), chỉnh sửa bằng công cụ phù hợp, sau đó thay lại đúng entry trong UnityFS gốc. Kích thước file .assets phải giữ nguyên để không làm lệch offset.", 13, "#475569"));
        buildBundleText = Text("Bundle gốc: chưa chọn", 12, "#64748B");
        buildCard.AddView(buildBundleText);
        AddButton(buildCard, "CHỌN UNITYFS GỐC", "#E8EEF8", "#1E3A5F", () => PickBuildFile(PickBuildBundleRequest));
        buildSerializedText = Text("SerializedFile đã sửa: chưa chọn", 12, "#64748B");
        buildCard.AddView(buildSerializedText);
        AddButton(buildCard, "CHỌN .ASSETS ĐÃ SỬA", "#E8EEF8", "#1E3A5F", () => PickBuildFile(PickBuildSerializedRequest));
        AddButton(buildCard, "TRÍCH .ASSETS RA KHỎI UNITYFS", "#0F766E", "#FFFFFF", async () => await ExtractBundleSerializedFilesAsync());
        AddButton(buildCard, "BUILD / REPACK ASSETBUNDLE", "#1D4ED8", "#FFFFFF", async () => await BuildAssetBundleAsync());
        body.AddView(buildCard);

        body.AddView(SectionTitle("04  ·  GHI CHÚ TÍNH NĂNG"));"""
if build_anchor in s: s = s.replace(build_anchor, build_ui, 1)
elif "BUILD / REPACK ASSETBUNDLE" not in s: raise SystemExit("Could not insert visible Build panel")

old_switch = '            case PickAssetOutput: assetOutputUri = uri; assetOutputText!.Text = "Đích: " + uri.LastPathSegment; break;'
new_switch = old_switch + """
            case PickBuildBundleRequest: buildBundleUri = uri; buildBundleText!.Text = "Bundle gốc: " + uri.LastPathSegment; break;
            case PickBuildSerializedRequest: buildSerializedUri = uri; buildSerializedText!.Text = "SerializedFile đã sửa: " + uri.LastPathSegment; break;"""
if old_switch in s: s = s.replace(old_switch, new_switch, 1)
elif "case PickBuildBundleRequest:" not in s: raise SystemExit("Could not add Build file-picker results")


old_const = "private const int PickDbInput = 1001, PickDbOutput = 1002, PickAssetInput = 1003, PickAssetOutput = 1004;"
new_const = "private const int PickDbInput = 1001, PickDbOutput = 1002, PickAssetInput = 1003, PickAssetOutput = 1004, PickTextureImageRequest = 1005, PickAudioFileRequest = 1006, PickTextureRawRequest = 1007, PickMeshObjRequest = 1008, PickBuildBundleRequest = 1009, PickBuildSerializedRequest = 1010;"
if old_const in s:
    s = s.replace(old_const, new_const, 1)
elif new_const not in s:
    raise SystemExit("Could not find picker request-code declaration")

preview = '        AddButton(assetCard, "XEM ẢNH / THÔNG TIN ASSET ĐÃ CHỌN", "#E8EEF8", "#1E3A5F", () => { ShowSelectedAsset(); return Task.CompletedTask; });'
buttons = preview + '''
        AddButton(assetCard, "SỬA TEXTURE2D TỪ PNG (ĐỊNH DẠNG UNCOMPRESSED)", "#7C3AED", "#FFFFFF", () => { PickTextureImage(); return Task.CompletedTask; });
        AddButton(assetCard, "THAY TEXTURE RAW (HỖ TRỢ ASTC / ETC / DXT)", "#7C3AED", "#FFFFFF", () => { PickTextureRaw(); return Task.CompletedTask; });
        AddButton(assetCard, "NHẬP OBJ (VERTEX / NORMAL / UV0)", "#7C3AED", "#FFFFFF", () => { PickMeshObj(); return Task.CompletedTask; });
        AddButton(assetCard, "SỬA SHADER SCRIPT (NẾU CÓ MÃ NGUỒN)", "#7C3AED", "#FFFFFF", () => { EditSelectedShader(); return Task.CompletedTask; });
        AddButton(assetCard, "SỬA MATERIAL FLOAT / COLOR", "#7C3AED", "#FFFFFF", () => { EditSelectedMaterial(); return Task.CompletedTask; });
        AddButton(assetCard, "ĐỔI TEXTURE SLOT CỦA MATERIAL", "#7C3AED", "#FFFFFF", () => { EditSelectedMaterialTextureSlot(); return Task.CompletedTask; });
        AddButton(assetCard, "GÁN SHADER CHO MATERIAL", "#7C3AED", "#FFFFFF", () => { EditSelectedMaterialShader(); return Task.CompletedTask; });
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
                ". Chỉ hỗ trợ vertex buffer inline hiện đại, position/normal/UV0 float32 và giữ nguyên số/thứ tự vertex; topology, rig và file gốc không đổi.");
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
                ". Yêu cầu cùng kích thước và cùng định dạng OGG/FSB5/RIFF/MP3/MP4. Với .resS ngoài, thư mục đầu ra có cả bản sao SerializedFile và resource đã sửa; external stream trong UnityFS vẫn chưa hỗ trợ.");
        });
    }



    private void EditSelectedMaterialShader()
    {
        var selected = GetSelectedAssets();
        if (selected.Count != 2)
        {
            Toast.MakeText(this, "Chọn đúng 2 asset: một Material và một Shader.", ToastLength.Long)!.Show();
            return;
        }
        var materials = selected.Select(x => x.Instance).OfType<AssetStudio.Material>().ToList();
        var shaders = selected.Select(x => x.Instance).OfType<AssetStudio.Shader>().ToList();
        if (materials.Count != 1 || shaders.Count != 1)
        {
            Toast.MakeText(this, "Lựa chọn phải gồm đúng một Material và một Shader.", ToastLength.Long)!.Show();
            return;
        }
        if (assetOutputUri == null)
        {
            Toast.MakeText(this, "Chọn thư mục xuất asset trước.", ToastLength.Long)!.Show();
            return;
        }
        var material = materials[0];
        var shader = shaders[0];
        new AlertDialog.Builder(this)
            .SetTitle("Gán Shader cho Material")
            .SetMessage("Material: " + material.m_Name + "\nShader đích: " + shader.m_Name +
                "\nChỉ đổi PPtr shader đã tồn tại và yêu cầu cùng SerializedFile. Không sửa compiled shader code.")
            .SetNegativeButton("HỦY", (_, _) => { })
            .SetPositiveButton("GÁN SHADER", async (_, _) =>
                await SaveMaterialShaderEditAsync(material, shader))
            .Show();
    }

    private async Task SaveMaterialShaderEditAsync(AssetStudio.Material material, AssetStudio.Shader shader)
    {
        await RunBusyAsync(async token =>
        {
            string parent = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "material_shader_edits");
            Directory.CreateDirectory(parent);
            string output = Path.Combine(parent, "Edited_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(output);
            string written = await Task.Run(
                () => MaterialTextureSlotPatcher.CreateShaderPatchedCopy(material, shader, output), token);
            await SafTree.CopyLocalToTreeAsync(this, output, assetOutputUri!,
                new Progress<(int done, string name)>(p => WriteStatus($"Đang xuất Material shader {p.done}: {p.name}")), token);
            WriteStatus("Đã gán Shader '" + shader.m_Name + "' vào Material '" + material.m_Name +
                "' trong bản mới: " + Path.GetFileName(written) + ". File gốc không đổi.");
        });
    }

    private void EditSelectedMaterialTextureSlot()
    {
        var selected = GetSelectedAssets();
        if (selected.Count != 2)
        {
            Toast.MakeText(this, "Chọn đúng 2 asset: một Material và một Texture2D.", ToastLength.Long)!.Show();
            return;
        }
        var materials = selected.Select(x => x.Instance).OfType<AssetStudio.Material>().ToList();
        var textures = selected.Select(x => x.Instance).OfType<AssetStudio.Texture2D>().ToList();
        if (materials.Count != 1 || textures.Count != 1)
        {
            Toast.MakeText(this, "Lựa chọn phải gồm đúng một Material và một Texture2D.", ToastLength.Long)!.Show();
            return;
        }
        var material = materials[0];
        var texture = textures[0];
        if (assetOutputUri == null)
        {
            Toast.MakeText(this, "Chọn thư mục xuất asset trước.", ToastLength.Long)!.Show();
            return;
        }
        var properties = material.m_SavedProperties.m_TexEnvs.Select(x => x.Key).Distinct().ToList();
        if (properties.Count == 0)
        {
            Toast.MakeText(this, "Material không có texture slot đã lưu.", ToastLength.Long)!.Show();
            return;
        }
        var spinner = new Spinner(this);
        var adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleSpinnerItem, properties);
        adapter.SetDropDownViewResource(global::Android.Resource.Layout.SimpleSpinnerDropDownItem);
        spinner.Adapter = adapter;
        new AlertDialog.Builder(this)
            .SetTitle("Chọn texture slot · " + material.m_Name)
            .SetMessage("Texture đích: " + texture.m_Name + ". Chỉ đổi PPtr của slot đã tồn tại; Material và Texture phải cùng SerializedFile.")
            .SetView(spinner)
            .SetNegativeButton("HỦY", (_, _) => { })
            .SetPositiveButton("GÁN TEXTURE", async (_, _) =>
                await SaveMaterialTextureSlotEditAsync(material, spinner.SelectedItem?.ToString() ?? string.Empty, texture))
            .Show();
    }

    private async Task SaveMaterialTextureSlotEditAsync(
        AssetStudio.Material material, string propertyName, AssetStudio.Texture2D texture)
    {
        await RunBusyAsync(async token =>
        {
            string parent = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "material_texture_edits");
            Directory.CreateDirectory(parent);
            string output = Path.Combine(parent, "Edited_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(output);
            string written = await Task.Run(
                () => MaterialTextureSlotPatcher.CreatePatchedCopy(material, propertyName, texture, output), token);
            await SafTree.CopyLocalToTreeAsync(this, output, assetOutputUri!,
                new Progress<(int done, string name)>(p => WriteStatus($"Đang xuất Material texture {p.done}: {p.name}")), token);
            WriteStatus("Đã gán texture '" + texture.m_Name + "' vào slot '" + propertyName +
                "' trong bản Material mới: " + Path.GetFileName(written) + ". File gốc không đổi.");
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
            ints = material.m_SavedProperties.m_Ints?.ToDictionary(x => x.Key, x => x.Value)
                ?? new Dictionary<string, int>(),
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
            .SetMessage("Sửa giá trị trong ints, floats và colors. Chỉ thay property đã tồn tại; texture slot và keyword được giữ nguyên. File gốc luôn được giữ nguyên.")
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
methods += r"""
    private void PickBuildFile(int requestCode)
    {
        if (busy) return;
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*");
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);
        StartActivityForResult(intent, requestCode);
    }

    private async Task<string> CopyPickedUriToLocalAsync(Uri uri, string destination, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var source = ContentResolver!.OpenInputStream(uri)
            ?? throw new IOException("Không mở được file đã chọn.");
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(output, 1024 * 1024, token);
        await output.FlushAsync(token);
        if (new FileInfo(destination).Length == 0)
            throw new InvalidDataException("File được chọn rỗng.");
        return destination;
    }

    private async Task ExtractBundleSerializedFilesAsync()
    {
        if (buildBundleUri == null || assetOutputUri == null)
        {
            Toast.MakeText(this, "Chọn UnityFS gốc và thư mục xuất asset trước.", ToastLength.Long)!.Show();
            return;
        }
        await RunBusyAsync(async token =>
        {
            string work = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "unityfs_build");
            string sourceDir = Path.Combine(work, "source");
            string output = Path.Combine(work, "extracted");
            Directory.CreateDirectory(sourceDir);
            string sourceName = Path.GetFileName(buildBundleUri!.LastPathSegment ?? "source.bundle");
            string source = await CopyPickedUriToLocalAsync(buildBundleUri!, Path.Combine(sourceDir, sourceName), token);
            if (Directory.Exists(output)) Directory.Delete(output, true);
            Directory.CreateDirectory(output);
            WriteStatus("Đang đọc UnityFS và trích các SerializedFile bên trong…");
            var result = await Task.Run(() => UnityFsBundleBuildService.ExtractSerializedFiles(source, output), token);
            await SafTree.CopyLocalToTreeAsync(this, output, assetOutputUri!,
                new Progress<(int done, string name)>(p => WriteStatus($"Đang xuất .assets {p.done}: {p.name}")), token);
            WriteStatus($"Đã trích {result.SerializedFiles} SerializedFile ({FormatBytes(result.Bytes)}). Chỉnh sửa file trong thư mục xuất, rồi chọn đúng .assets ở mục BUILD.");
        });
    }

    private async Task BuildAssetBundleAsync()
    {
        if (buildBundleUri == null || buildSerializedUri == null || assetOutputUri == null)
        {
            Toast.MakeText(this, "Cần chọn UnityFS gốc, SerializedFile đã sửa và thư mục đầu ra.", ToastLength.Long)!.Show();
            return;
        }
        await RunBusyAsync(async token =>
        {
            string work = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "unityfs_build");
            string sourceDir = Path.Combine(work, "build_source");
            string replacementDir = Path.Combine(work, "replacement");
            string output = Path.Combine(work, "built");
            Directory.CreateDirectory(sourceDir);
            Directory.CreateDirectory(replacementDir);
            string bundleName = Path.GetFileName(buildBundleUri!.LastPathSegment ?? "source.bundle");
            string replacementName = Path.GetFileName(buildSerializedUri!.LastPathSegment ?? "replacement.assets");
            string source = await CopyPickedUriToLocalAsync(buildBundleUri!, Path.Combine(sourceDir, bundleName), token);
            string replacement = await CopyPickedUriToLocalAsync(buildSerializedUri!, Path.Combine(replacementDir, replacementName), token);
            if (Directory.Exists(output)) Directory.Delete(output, true);
            Directory.CreateDirectory(output);
            WriteStatus("Đang thay SerializedFile vào UnityFS, dựng lại block/directory và xác minh bundle sau build…");
            string built = await Task.Run(
                () => UnityFsBundleBuildService.BuildFromSerializedFile(source, replacement, output), token);
            await SafTree.CopyLocalToTreeAsync(this, output, assetOutputUri!,
                new Progress<(int done, string name)>(p => WriteStatus($"Đang xuất bundle đã build {p.done}: {p.name}")), token);
            WriteStatus("BUILD THÀNH CÔNG: " + Path.GetFileName(built) +
                ". Đã mở lại UnityFS, kiểm tra đủ entry/tên/kích thước và so sánh SerializedFile đã thay thế từng byte. File gốc không bị ghi đè.");
        });
    }

"""
if "private void PickTextureImage()" not in s:
    if s.count(method_anchor) != 1:
        raise SystemExit("Could not uniquely locate ShowSelectedAsset insertion point")
    s = s.replace(method_anchor, methods + method_anchor, 1)

p.write_text(s, encoding="utf-8")
print("Integrated safe Texture2D PNG import, Shader script editing, and Material float/color editing")
