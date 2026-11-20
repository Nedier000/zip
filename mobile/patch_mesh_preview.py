from pathlib import Path

p = Path("mobile/MainActivity.cs")
if not p.is_file():
    raise SystemExit("mobile/MainActivity.cs missing after source restore")
s = p.read_text(encoding="utf-8")

old = """            var bitmap = AssetBrowser.CreatePreviewBitmap(entry);
            if (bitmap != null)
            {
                var image = new ImageView(this) { AdjustViewBounds = true };
                image.SetScaleType(ImageView.ScaleType.FitCenter);
                image.SetImageBitmap(bitmap);
                layout.AddView(image, new LinearLayout.LayoutParams(-1, Dp(280)));
            }
            else
            {
                layout.AddView(Text("Loại asset này không có xem trước hình ảnh. Dùng nút Xuất để lưu dữ liệu.", 12, "#64748B"));
            }"""
new = """            if (entry.Instance is AssetStudio.AudioClip)
            {
                string audioFolder = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "audio_preview");
                Directory.CreateDirectory(audioFolder);
                string audioPath = AssetBrowser.Export(entry, audioFolder);
                var audioView = new StudioAudioPreviewView(this, audioPath);
                layout.AddView(audioView, new LinearLayout.LayoutParams(-1, Dp(300)));
            }
            else if (entry.Instance is AssetStudio.Texture2D texture)
            {
                var bitmap = AssetBrowser.CreatePreviewBitmap(entry);
                if (bitmap != null)
                {
                    var textureView = new StudioTexturePreviewView(this, bitmap, texture);
                    layout.AddView(textureView, new LinearLayout.LayoutParams(-1, Dp(390)));
                }
                else
                {
                    layout.AddView(Text("Texture format này chưa giải mã được để preview.", 12, "#64748B"));
                }
            }
            else
            {
                var previewMesh = ResolveMeshForPreview(entry.Instance);
                if (previewMesh != null)
                {
                    var modelView = new StudioMeshPreviewView(this, previewMesh);
                    layout.AddView(modelView, new LinearLayout.LayoutParams(-1, Dp(340)));
                    layout.AddView(Text("Xoay bằng một ngón tay · thu phóng bằng hai ngón tay · preview Mesh tĩnh.", 11, "#64748B"));
                }
                else
                {
                    var bitmap = AssetBrowser.CreatePreviewBitmap(entry);
                    if (bitmap != null)
                    {
                        var image = new ImageView(this);
                        image.SetAdjustViewBounds(true);
                        image.SetScaleType(ImageView.ScaleType.FitCenter);
                        image.SetImageBitmap(bitmap);
                        layout.AddView(image, new LinearLayout.LayoutParams(-1, Dp(280)));
                    }
                    else
                    {
                        layout.AddView(Text("Loại asset này chưa có preview trực quan. Dùng nút Xuất để lưu dữ liệu.", 12, "#64748B"));
                    }
                }
            }"""
if old in s:
    s = s.replace(old, new, 1)
elif "ResolveMeshForPreview(entry.Instance)" not in s:
    raise SystemExit("Could not locate image-only preview block")

anchor = "    private void ShowSelectedAsset()"
helper = r"""
    private static AssetStudio.Mesh? ResolveMeshForPreview(AssetStudio.Object asset)
    {
        if (asset is AssetStudio.Mesh directMesh) return directMesh;

        AssetStudio.GameObject? gameObject = asset switch
        {
            AssetStudio.GameObject go => go,
            AssetStudio.Animator animator when animator.m_GameObject.TryGet(out var go) => go,
            AssetStudio.Animation animation when animation.m_GameObject.TryGet(out var go) => go,
            _ => null
        };
        if (gameObject == null) return null;

        var visited = new HashSet<long>();
        return FindFirstMesh(gameObject.m_Transform, visited, 0);
    }

    private static AssetStudio.Mesh? FindFirstMesh(
        AssetStudio.Transform? transform,
        HashSet<long> visited,
        int depth)
    {
        if (transform == null || depth > 128 || !visited.Add(transform.m_PathID))
            return null;

        if (transform.m_GameObject.TryGet(out var gameObject))
        {
            if (gameObject.m_SkinnedMeshRenderer != null &&
                gameObject.m_SkinnedMeshRenderer.m_Mesh.TryGet(out var skinnedMesh))
                return skinnedMesh;

            if (gameObject.m_MeshFilter != null &&
                gameObject.m_MeshFilter.m_Mesh.TryGet(out var staticMesh))
                return staticMesh;
        }

        foreach (var childPointer in transform.m_Children)
        {
            if (childPointer.TryGet(out var child))
            {
                var mesh = FindFirstMesh(child, visited, depth + 1);
                if (mesh != null) return mesh;
            }
        }
        return null;
    }

"""
if "private static AssetStudio.Mesh? ResolveMeshForPreview(" not in s:
    if s.count(anchor) != 1:
        raise SystemExit("Could not locate ShowSelectedAsset insertion point")
    s = s.replace(anchor, helper + anchor, 1)

s = s.replace(
    'AddButton(assetCard, "XEM ẢNH / THÔNG TIN ASSET ĐÃ CHỌN"',
    'AddButton(assetCard, "XEM PREVIEW / THÔNG TIN ASSET ĐÃ CHỌN"'
)
p.write_text(s, encoding="utf-8")
print("Connected Android OpenGL ES Mesh preview to selected Mesh/GameObject/Animator assets.")
