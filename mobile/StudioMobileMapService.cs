using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AssetStudio;

namespace HoKTool.Android;

/// <summary>
/// Android storage adapter for the original Studio-HoK AI version, CABMap,
/// AssetMap and UnityCN key managers. Its working directory is app-private,
/// so the desktop relative paths never write into a read-only install folder.
/// </summary>
public static class StudioMobileMapService
{
    private static string? dataRoot;

    public static string Initialize(string privateDataDirectory)
    {
        if (string.IsNullOrWhiteSpace(privateDataDirectory))
            throw new ArgumentException("An app-private data directory is required.", nameof(privateDataDirectory));
        string root = Path.GetFullPath(privateDataDirectory);
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "AI"));
        Directory.CreateDirectory(Path.Combine(root, AssetsHelper.MapName));
        Environment.CurrentDirectory = root;
        dataRoot = root;
        return root;
    }

    private static void EnsureInitialized()
    {
        if (dataRoot == null) throw new InvalidOperationException("Call Initialize() before using Android map managers.");
    }

    public static async Task<bool> RefreshAIVersionsAsync()
    {
        EnsureInitialized();
        return await AIVersionManager.FetchVersions().ConfigureAwait(false);
    }

    public static IReadOnlyList<(string Version, bool Downloaded)> GetAIVersions()
    {
        EnsureInitialized();
        return AIVersionManager.GetVersions()
            .Select(x => (Version: x.Item1, Downloaded: x.Item2))
            .OrderByDescending(x => x.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static async Task<string> DownloadAIAsync(string version)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException(nameof(version));
        string path = await AIVersionManager.FetchAI(version).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new IOException($"AI data for version '{version}' could not be downloaded or found in cache.");
        return path;
    }

    public static async Task<(string Path, int AssetCount)> DownloadAndLoadAIAsync(string version)
    {
        string path = await DownloadAIAsync(version).ConfigureAwait(false);
        await Task.Run(() => ResourceIndex.FromFile(path)).ConfigureAwait(false);
        int count = ResourceIndex.GetAssetCount();
        if (count <= 0)
            throw new InvalidDataException($"AI index '{version}' downloaded but contained no readable asset entries.");
        return (path, count);
    }

    public static int GetLoadedAIAssetCount()
    {
        EnsureInitialized();
        return ResourceIndex.GetAssetCount();
    }

    public static async Task<string> BuildCABMapAsync(string inputFolder, string mapName, int selectedGame)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(inputFolder) || !Directory.Exists(inputFolder))
            throw new DirectoryNotFoundException(inputFolder);
        if (string.IsNullOrWhiteSpace(mapName)) throw new ArgumentException("Map name is empty.", nameof(mapName));

        string safeName = SafeName(mapName);
        string output = Path.Combine(dataRoot!, AssetsHelper.MapName, safeName + ".bin");
        if (File.Exists(output)) File.Delete(output);

        string[] files = Directory.EnumerateFiles(inputFolder, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0) throw new InvalidDataException("The selected game folder contains no files.");

        await Task.Run(() => AssetsHelper.BuildCABMap(
            files, safeName, Path.GetFullPath(inputFolder), GameManager.GetGame(selectedGame))).ConfigureAwait(false);

        if (!File.Exists(output) || new FileInfo(output).Length < sizeof(int))
            throw new InvalidDataException("CABMap builder did not produce a valid output file.");
        return output;
    }

    public static async Task<IReadOnlyList<string>> BuildBothAsync(
        string inputFolder, string mapName, int selectedGame, string assetMapOutput, ExportListType exportType)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(inputFolder) || !Directory.Exists(inputFolder))
            throw new DirectoryNotFoundException(inputFolder);
        if (string.IsNullOrWhiteSpace(mapName)) throw new ArgumentException("Map name is empty.", nameof(mapName));
        if (string.IsNullOrWhiteSpace(assetMapOutput)) throw new ArgumentException("Output folder is empty.", nameof(assetMapOutput));

        string safeName = SafeName(mapName);
        Directory.CreateDirectory(assetMapOutput);
        string cabPath = Path.Combine(dataRoot!, AssetsHelper.MapName, safeName + ".bin");
        if (File.Exists(cabPath)) File.Delete(cabPath);

        string[] files = Directory.EnumerateFiles(inputFolder, "*", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0) throw new InvalidDataException("The selected game folder contains no files.");

        await AssetsHelper.BuildBoth(
            files, safeName, Path.GetFullPath(inputFolder), GameManager.GetGame(selectedGame),
            assetMapOutput, exportType).ConfigureAwait(false);

        if (!File.Exists(cabPath))
            throw new InvalidDataException("BuildBoth did not produce the CABMap file.");
        var results = Directory.EnumerateFiles(assetMapOutput, "*", SearchOption.AllDirectories).ToList();
        if (results.Count == 0)
            throw new InvalidDataException("BuildBoth created the CABMap but did not export AssetMap files.");
        results.Add(cabPath);
        return results;
    }

    public static IReadOnlyList<string> GetCABMaps()
    {
        EnsureInitialized();
        return AssetsHelper.GetMaps().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool LoadCABMap(string mapName)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(mapName)) return false;
        return AssetsHelper.LoadCABMapInternal(Path.GetFileNameWithoutExtension(mapName));
    }

    public static IReadOnlyList<string> GetUnityCNKeys()
    {
        EnsureInitialized();
        var entries = UnityCNManager.GetEntries() ?? Array.Empty<UnityCN.Entry>();
        return entries.Select((x, i) => $"{i}: {x.Name}").ToArray();
    }

    public static bool SetUnityCNKey(int index)
    {
        EnsureInitialized();
        var entries = UnityCNManager.GetEntries() ?? Array.Empty<UnityCN.Entry>();
        if (index < 0 || index >= entries.Length) return false;
        return UnityCN.SetKey(entries[index]);
    }

    public static void SaveUnityCNKeys(IEnumerable<(string Name, string Key)> entries)
    {
        EnsureInitialized();
        if (entries == null) throw new ArgumentNullException(nameof(entries));
        var output = new List<UnityCN.Entry>();
        foreach (var (name, key) in entries)
        {
            var entry = new UnityCN.Entry((name ?? string.Empty).Trim(), (key ?? string.Empty).Trim());
            if (string.IsNullOrWhiteSpace(entry.Name) || !entry.Validate())
                throw new InvalidDataException($"Invalid UnityCN key entry: {name}");
            output.Add(entry);
        }
        UnityCNManager.SaveEntries(output);
    }

    public static async Task BuildAssetMapAsync(
        string[] inputFiles,
        string mapName,
        int selectedGame,
        string outputFolder,
        ExportListType outputType)
    {
        EnsureInitialized();
        if (inputFiles == null || inputFiles.Length == 0) throw new ArgumentException("No Unity input files were supplied.", nameof(inputFiles));
        if (string.IsNullOrWhiteSpace(mapName)) throw new ArgumentException("Map name is empty.", nameof(mapName));
        if (string.IsNullOrWhiteSpace(outputFolder)) throw new ArgumentException("Output folder is empty.", nameof(outputFolder));
        Directory.CreateDirectory(outputFolder);

        var normalized = inputFiles
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length == 0) throw new FileNotFoundException("No valid Unity asset files were found.");

        await AssetsHelper.BuildAssetMap(
            normalized,
            SafeName(mapName),
            GameManager.GetGame(selectedGame),
            outputFolder,
            outputType).ConfigureAwait(false);
    }

    public static string GetDataRoot()
    {
        EnsureInitialized();
        return dataRoot!;
    }

    private static string SafeName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) || ch == '/' || ch == '\\' ? '_' : ch).ToArray();
        string result = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(result) ? "AssetMap" : result;
    }
}
