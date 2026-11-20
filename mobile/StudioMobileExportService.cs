using AssetStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Text;

namespace HoKTool.Android;

public enum StudioMobileExportMode
{
    Convert,
    Raw,
    Dump,
    Json
}

public sealed record StudioBulkExportResult(
    int Considered,
    int Exported,
    int Skipped,
    IReadOnlyList<string> Errors);

/// <summary>
/// Android counterpart of the desktop ExportAssets(All/Selected/Filtered)
/// loop, using the same per-object AssetBrowser exporter and the managed GLB
/// model path for GameObject/Animator entries.
/// </summary>
public static class StudioMobileExportService
{
    public static StudioBulkExportResult ExportRows(
        IEnumerable<StudioAssetRow> sourceRows,
        string outputFolder,
        StudioMobileSettings settings,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null,
        StudioMobileExportMode mode = StudioMobileExportMode.Convert)
    {
        if (sourceRows == null) throw new ArgumentNullException(nameof(sourceRows));
        if (string.IsNullOrWhiteSpace(outputFolder)) throw new ArgumentException("Output folder is empty.", nameof(outputFolder));
        settings ??= new StudioMobileSettings();
        Directory.CreateDirectory(outputFolder);

        var rows = sourceRows
            .Where(x => x != null && x.Instance != null)
            .ToArray();

        int considered = 0;
        int exported = 0;
        int skipped = 0;
        var errors = new List<string>();
        var nameKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exportedModels = new HashSet<GameObject>();

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            considered++;

            string groupFolder;
            try
            {
                groupFolder = GetGroupFolder(outputFolder, row, settings.assetGroupOption);
                Directory.CreateDirectory(groupFolder);
            }
            catch (Exception ex)
            {
                skipped++;
                errors.Add($"{row.Name} [{row.PathId}]: Invalid export folder: {ex.Message}");
                continue;
            }

            GameObject? model = ResolveModelObject(row.Instance);
            bool isModelEntry = row.Instance is AssetStudio.GameObject or AssetStudio.Animator;

            if (settings.modelsOnly)
            {
                if (!isModelEntry || model == null || !HasModelSafe(model))
                {
                    skipped++;
                    continue;
                }
            }

            if (mode == StudioMobileExportMode.Convert && isModelEntry && model != null && HasModelSafe(model))
            {
                if (!exportedModels.Add(model))
                {
                    skipped++;
                    continue;
                }

                string key = groupFolder + "|" + model.Name;
                if (!settings.allowDuplicates && !nameKeys.Add(key))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    string path = row.Instance is Animator animator
                        ? StudioModelExportService.ExportAnimator(animator, groupFolder, settings)
                        : StudioModelExportService.ExportGameObject(model, groupFolder, settings);
                    progress?.Report($"Model {exported + 1:N0}/{rows.Length:N0}: {Path.GetFileName(path)}");
                    exported++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{model.Name} [{model.m_PathID}]: {ex.Message}");
                    progress?.Report($"Model error: {model.Name} · {ex.Message}");
                }
                continue;
            }

            if (settings.modelsOnly)
            {
                skipped++;
                continue;
            }

            string assetName = string.IsNullOrWhiteSpace(row.Name)
                ? $"{row.TypeName}_{row.PathId}"
                : row.Name;
            string duplicateKey = groupFolder + "|" + row.TypeName + "|" + assetName;
            if (!settings.allowDuplicates && !nameKeys.Add(duplicateKey))
            {
                skipped++;
                continue;
            }

            if (settings.allowDuplicates && nameKeys.Contains(duplicateKey))
                assetName = $"{assetName}__{row.PathId}";
            nameKeys.Add(duplicateKey);

            try
            {
                string written = ExportSingleAsset(row, assetName, groupFolder, settings, mode);
                if (string.IsNullOrWhiteSpace(written) || !File.Exists(written))
                {
                    skipped++;
                    continue;
                }
                progress?.Report($"Asset {exported + 1:N0}/{rows.Length:N0}: {Path.GetFileName(written)}");
                exported++;
            }
            catch (Exception ex)
            {
                errors.Add($"{row.Name} [{row.PathId}]: {ex.Message}");
                progress?.Report($"Export error: {row.Name} · {ex.Message}");
            }
        }

        return new StudioBulkExportResult(considered, exported, skipped, errors);
    }

    private static string ExportSingleAsset(
        StudioAssetRow row,
        string assetName,
        string outputFolder,
        StudioMobileSettings settings,
        StudioMobileExportMode mode)
    {
        var asset = row.Instance;
        if (mode == StudioMobileExportMode.Convert)
        {
            // The desktop ShaderConverter imports Vortice/D3DCompiler native
            // libraries. Android exports available source text, or preserves
            // the original binary object instead of invoking that path.
            if (asset is Shader shader)
            {
                byte[] script = shader.m_Script ?? Array.Empty<byte>();
                string text = string.Empty;
                if (script.Length > 0)
                {
                    try { text = new UTF8Encoding(false, true).GetString(script); }
                    catch (DecoderFallbackException) { }
                }
                if (!string.IsNullOrWhiteSpace(text) && !text.Any(ch => ch == (char)0))
                    return WriteUnique(outputFolder, assetName, ".shader", Encoding.UTF8.GetBytes(text));
                return WriteUnique(outputFolder, assetName, ".dat", asset.GetRawData());
            }

            // FMOD's Windows native decoder is not loaded on Android. Preserve
            // the original encoded stream; Android MediaPlayer handles preview
            // when the codec is supported by the OS.
            if (asset is AudioClip audio)
            {
                byte[] data = audio.m_AudioData?.GetData() ?? Array.Empty<byte>();
                if (data.Length == 0) return string.Empty;
                string extension;
                try { extension = new AudioClipConverter(audio).GetExtensionName(); }
                catch { extension = DetectAudioExtension(data); }
                if (string.IsNullOrWhiteSpace(extension) || extension[0] != '.') extension = DetectAudioExtension(data);
                return WriteUnique(outputFolder, assetName, extension, data);
            }

            var entry = new AssetEntry
            {
                Instance = asset,
                Name = assetName,
                TypeName = row.TypeName,
                Source = row.Source,
                PathId = row.PathId
            };
            return AssetBrowser.Export(entry, outputFolder);
        }

        if (mode == StudioMobileExportMode.Raw)
            return WriteUnique(outputFolder, assetName, ".dat", asset.GetRawData());

        if (mode == StudioMobileExportMode.Dump)
        {
            string? dump = null;
            try { dump = asset.Dump(); }
            catch { }
            if (string.IsNullOrWhiteSpace(dump)) return string.Empty;
            return WriteUnique(outputFolder, assetName, ".txt", Encoding.UTF8.GetBytes(dump));
        }

        var jsonSettings = new JsonSerializerSettings { Formatting = Formatting.Indented };
        jsonSettings.Converters.Add(new StringEnumConverter());
        string json = JsonConvert.SerializeObject(asset, jsonSettings);
        return WriteUnique(outputFolder, assetName, ".json", Encoding.UTF8.GetBytes(json));
    }

    private static string WriteUnique(string folder, string name, string extension, byte[] data)
    {
        if (data == null || data.Length == 0) return string.Empty;
        string stem = SafeSegment(name);
        if (string.IsNullOrWhiteSpace(stem)) stem = "Asset";
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, stem + extension);
        if (File.Exists(path)) throw new IOException($"Export target already exists: {path}");
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        output.Write(data, 0, data.Length);
        return path;
    }

    private static string DetectAudioExtension(byte[] data)
    {
        if (data.Length >= 4)
        {
            if (data.AsSpan(0, 4).SequenceEqual(new byte[] { 0x4F, 0x67, 0x67, 0x53 })) return ".ogg";
            if (data.AsSpan(0, 4).SequenceEqual(new byte[] { 0x52, 0x49, 0x46, 0x46 })) return ".wav";
            if (data.AsSpan(0, 4).SequenceEqual(new byte[] { 0x46, 0x53, 0x42, 0x35 })) return ".fsb";
            if (data[0] == 0x49 && data[1] == 0x44 && data[2] == 0x33) return ".mp3";
            if (data.AsSpan(0, 4).SequenceEqual(new byte[] { 0x00, 0x00, 0x00, 0x18 }) &&
                data.AsSpan(4, 4).SequenceEqual(new byte[] { 0x66, 0x74, 0x79, 0x70 })) return ".mp4";
        }
        if (data.Length >= 2 && data[0] == 0xFF && (data[1] & 0xE0) == 0xE0) return ".mp3";
        return ".audio";
    }

    public static StudioBulkExportResult ExportSceneRoots(
        IEnumerable<StudioSceneNode> sourceRoots,
        string outputFolder,
        StudioMobileSettings settings,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        if (sourceRoots == null) throw new ArgumentNullException(nameof(sourceRoots));
        if (string.IsNullOrWhiteSpace(outputFolder)) throw new ArgumentException("Output folder is empty.", nameof(outputFolder));
        settings ??= new StudioMobileSettings();
        Directory.CreateDirectory(outputFolder);

        int considered = 0;
        int exported = 0;
        int skipped = 0;
        var errors = new List<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in sourceRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            considered++;
            if (root?.Instance == null || !HasModelSafe(root.Instance))
            {
                skipped++;
                continue;
            }

            string name = SafeSegment(string.IsNullOrWhiteSpace(root.Name) ? $"Scene_{root.PathId}" : root.Name);
            if (name.Length == 0) name = $"Scene_{root.PathId}";
            if (!settings.allowDuplicates && !names.Add(name))
            {
                skipped++;
                continue;
            }
            if (settings.allowDuplicates && !names.Add(name))
                name += "__" + root.PathId.ToString(System.Globalization.CultureInfo.InvariantCulture);

            try
            {
                string folder = Path.Combine(outputFolder, name);
                Directory.CreateDirectory(folder);
                string path = StudioModelExportService.ExportGameObject(root.Instance, folder, settings);
                exported++;
                progress?.Report($"Scene model {exported:N0}: {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                errors.Add($"{root.Name} [{root.PathId}]: {ex.Message}");
                progress?.Report($"Scene model error: {root.Name} · {ex.Message}");
            }
        }

        return new StudioBulkExportResult(considered, exported, skipped, errors);
    }

    private static string SafeSegment(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) || ch == '/' || ch == '\\' || char.IsControl(ch) ? '_' : ch).ToArray();
        return new string(chars).Trim().Trim('.');
    }

    private static GameObject? ResolveModelObject(AssetStudio.Object item)
    {
        try
        {
            return item switch
            {
                GameObject gameObject => gameObject,
                Animator animator when animator.m_GameObject.TryGet(out var target) => target,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool HasModelSafe(GameObject gameObject)
    {
        try { return gameObject.HasModel(); }
        catch { return false; }
    }

    private static string GetGroupFolder(string root, StudioAssetRow row, int grouping)
    {
        string relative = grouping switch
        {
            0 => row.TypeName,
            1 => ContainerFolder(row.Container),
            2 => SourceFolder(row.SourcePath, row.Source),
            _ => string.Empty
        };

        string current = Path.GetFullPath(root);
        if (string.IsNullOrWhiteSpace(relative)) return current;
        foreach (string segment in relative.Replace('\\', '/').Split('/'))
        {
            string safe = SafeSegment(segment);
            if (safe.Length == 0 || safe == "." || safe == "..") continue;
            current = Path.Combine(current, safe);
        }

        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string normalized = Path.GetFullPath(current);
        if (!normalized.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Export grouping path escaped the selected output folder.");
        return normalized;
    }

    private static string ContainerFolder(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string path = value.Replace('\\', '/').Trim('/');
        if (Path.HasExtension(path))
        {
            int slash = path.LastIndexOf('/');
            return slash <= 0 ? string.Empty : path.Substring(0, slash);
        }
        return path;
    }

    private static string SourceFolder(string originalPath, string sourceName)
    {
        if (!string.IsNullOrWhiteSpace(originalPath))
        {
            string file = Path.GetFileName(originalPath);
            string stem = string.IsNullOrWhiteSpace(file) ? sourceName : file;
            return SafeSegment(stem) + "_export/" + SafeSegment(sourceName);
        }
        return SafeSegment(sourceName) + "_export";
    }

    private static string SafeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var invalid = Path.GetInvalidFileNameChars();
        char[] chars = value.Select(ch =>
            invalid.Contains(ch) || ch == '/' || ch == '\\' || char.IsControl(ch) ? '_' : ch).ToArray();
        return new string(chars).Trim().Trim('.');
    }
}
