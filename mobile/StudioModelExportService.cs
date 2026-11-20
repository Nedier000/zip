using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using AssetStudio;

namespace HoKTool.Android;

/// <summary>
/// Reuses the original Studio-HoK ModelConverter and routes its IImported scene
/// through the managed GLB writer instead of the desktop-only FBX P/Invoke layer.
/// </summary>
public static class StudioModelExportService
{
    public static string ExportGameObject(GameObject gameObject, string outputFolder, StudioMobileSettings settings)
    {
        if (gameObject == null) throw new ArgumentNullException(nameof(gameObject));
        if (string.IsNullOrWhiteSpace(outputFolder)) throw new ArgumentException("Output folder is empty.", nameof(outputFolder));
        Directory.CreateDirectory(outputFolder);

        settings ??= new StudioMobileSettings();
        settings.ApplyCoreSettings();
        var options = CreateOptions(settings);
        var converted = new ModelConverter(gameObject, options);
        string name = SafeName(string.IsNullOrWhiteSpace(gameObject.Name) ? "GameObject" : gameObject.Name);
        string outputPath = GetUniquePath(outputFolder, name + ".glb");
        var exporter = new StudioGlbExporter();
        exporter.Export(outputPath, converted, settings.exportSkins, settings.exportAnimations, settings.exportMaterials);
        return outputPath;
    }

    public static string ExportAnimator(Animator animator, string outputFolder, StudioMobileSettings settings)
    {
        if (animator == null) throw new ArgumentNullException(nameof(animator));
        if (!animator.m_GameObject.TryGet(out var gameObject))
            throw new InvalidDataException("Animator does not resolve to a GameObject.");
        return ExportGameObject(gameObject, outputFolder, settings);
    }

    public static string ExportSelected(
        AssetStudio.Object selected,
        IReadOnlyList<SerializedFile> loadedFiles,
        string outputFolder,
        StudioMobileSettings settings)
    {
        if (selected == null) throw new ArgumentNullException(nameof(selected));
        GameObject? gameObject = selected switch
        {
            GameObject go => go,
            Animator animator when animator.m_GameObject.TryGet(out var go) => go,
            MeshRenderer renderer when renderer.m_GameObject.TryGet(out var go) => go,
            SkinnedMeshRenderer renderer when renderer.m_GameObject.TryGet(out var go) => go,
            MeshFilter filter when filter.m_GameObject.TryGet(out var go) => go,
            _ => null
        };

        if (gameObject == null && selected is Mesh mesh)
        {
            foreach (var file in loadedFiles ?? Array.Empty<SerializedFile>())
            {
                foreach (var candidate in file.Objects?.OfType<GameObject>() ?? Enumerable.Empty<GameObject>())
                {
                    try
                    {
                        if (candidate.m_MeshFilter?.m_Mesh.TryGet(out var staticMesh) == true && ReferenceEquals(staticMesh, mesh))
                        {
                            gameObject = candidate;
                            break;
                        }
                        if (candidate.m_SkinnedMeshRenderer?.m_Mesh.TryGet(out var skinnedMesh) == true && ReferenceEquals(skinnedMesh, mesh))
                        {
                            gameObject = candidate;
                            break;
                        }
                    }
                    catch { }
                }
                if (gameObject != null) break;
            }
        }

        if (gameObject == null)
            throw new NotSupportedException($"Không thể dựng model GLB từ loại asset {selected.type}. Hãy chọn GameObject, Animator, Renderer hoặc Mesh đang được gắn vào scene.");

        return ExportGameObject(gameObject, outputFolder, settings);
    }

    private static ModelConverter.Options CreateOptions(StudioMobileSettings settings)
    {
        Dictionary<string, (bool, int)> uvs;
        Dictionary<string, int> texs;
        try
        {
            uvs = JsonConvert.DeserializeObject<Dictionary<string, (bool, int)>>(settings.uvs)
                ?? DefaultUvs();
        }
        catch { uvs = DefaultUvs(); }

        try
        {
            texs = JsonConvert.DeserializeObject<Dictionary<string, int>>(settings.texs)
                ?? new Dictionary<string, int>();
        }
        catch { texs = new Dictionary<string, int>(); }

        for (int i = 0; i < 8; i++)
        {
            string key = "UV" + i;
            if (!uvs.ContainsKey(key)) uvs[key] = (i < 2, i == 1 ? 1 : 0);
        }

        if (!Enum.TryParse<ImageFormat>(settings.convertType, true, out var imageFormat))
            imageFormat = ImageFormat.Png;

        return new ModelConverter.Options
        {
            imageFormat = imageFormat,
            game = GameManager.GetGame(settings.selectedGame),
            collectAnimations = settings.collectAnimations,
            exportMaterials = settings.exportMaterials,
            materials = new HashSet<Material>(),
            uvs = uvs,
            texs = texs
        };
    }

    private static Dictionary<string, (bool, int)> DefaultUvs()
    {
        var result = new Dictionary<string, (bool, int)>();
        for (int i = 0; i < 8; i++) result["UV" + i] = (i < 2, i == 1 ? 1 : 0);
        return result;
    }

    private static string GetUniquePath(string folder, string fileName)
    {
        string path = Path.Combine(folder, fileName);
        if (!File.Exists(path)) return path;
        string stem = Path.GetFileNameWithoutExtension(fileName);
        for (int i = 1; i < 10000; i++)
        {
            path = Path.Combine(folder, $"{stem} ({i}).glb");
            if (!File.Exists(path)) return path;
        }
        throw new IOException("Could not create a unique model export name.");
    }

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) || ch == '/' || ch == '\\' ? '_' : ch).ToArray();
        string name = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(name) ? "Model" : name;
    }
}
