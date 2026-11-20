using AssetStudio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace HoKTool.Android;

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
        IProgress<string>? progress = null)
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

            if (isModelEntry && model != null && HasModelSafe(model))
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
                    string path = StudioModelExportService.ExportGameObject(
                        model, groupFolder, settings);
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
                var entry = new AssetEntry
                {
                    Instance = row.Instance,
                    Name = assetName,
                    TypeName = row.TypeName,
                    Source = row.Source,
                    PathId = row.PathId
                };
                string written = AssetBrowser.Export(entry, groupFolder);
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
