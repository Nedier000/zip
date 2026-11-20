using GameManager = AssetStudio.GameManager;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Threading;
using AssetStudio;

namespace HoKTool.Android;

/// <summary>
/// UI-independent workspace model for the Android Studio-HoK port.
/// It centralizes the same loading, dependency, asset-list, scene-tree and
/// class-tree data that the Windows Forms MainForm currently assembles.
/// </summary>
public sealed class StudioWorkspaceService : IDisposable
{
    private AssetsManager? manager;
    private readonly object sync = new();

    public bool IsLoaded => manager != null && manager.assetsFileList.Count > 0;
    public IReadOnlyList<SerializedFile> SerializedFiles =>
        manager?.assetsFileList.ToArray() ?? Array.Empty<SerializedFile>();

    public void LoadFiles(
        IEnumerable<string> paths,
        string? unityVersion = null,
        bool resolveDependencies = true,
        bool skipProcess = false,
        Action<string>? log = null,
        int selectedGame = (int)GameType.HonorOfKings)
    {
        string[] files = NormalizeFiles(paths);
        if (files.Length == 0) throw new ArgumentException("No input files were selected.", nameof(paths));

        lock (sync)
        {
            ResetCore();
            manager = CreateManager(unityVersion, resolveDependencies, skipProcess, selectedGame);
            log?.Invoke($"Loading {files.Length} input file(s) with the Honor of Kings profile.");
            manager.LoadFiles(files);
            log?.Invoke($"Loaded {manager.assetsFileList.Count} SerializedFile(s).");
        }
    }

    public void LoadFolder(
        string folder,
        string? unityVersion = null,
        bool resolveDependencies = true,
        bool skipProcess = false,
        Action<string>? log = null,
        int selectedGame = (int)GameType.HonorOfKings)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            throw new DirectoryNotFoundException(folder);

        lock (sync)
        {
            ResetCore();
            manager = CreateManager(unityVersion, resolveDependencies, skipProcess, selectedGame);
            log?.Invoke("Loading folder recursively with the Honor of Kings profile.");
            manager.LoadFolder(Path.GetFullPath(folder));
            log?.Invoke($"Loaded {manager.assetsFileList.Count} SerializedFile(s).");
        }
    }

    public void Cancel()
    {
        lock (sync)
        {
            try { manager?.tokenSource.Cancel(); }
            catch (ObjectDisposedException) { }
        }
    }

    public void Reset()
    {
        lock (sync) ResetCore();
    }

    public IReadOnlyList<StudioAssetRow> GetAssetRows(bool includeAllObjects = true, bool skipContainer = false)
    {
        lock (sync)
        {
            EnsureLoaded();
            var containerNames = BuildContainerMap(skipContainer);
            var objectNames = BuildObjectNameMap(containerNames);
            var rows = new List<StudioAssetRow>();

            foreach (var file in manager!.assetsFileList)
            {
                foreach (var obj in file.Objects ?? new List<AssetStudio.Object>())
                {
                    if (obj == null) continue;
                    if (!includeAllObjects && !obj.type.CanExport()) continue;

                    containerNames.TryGetValue(obj, out string? container);
                    objectNames.TryGetValue(obj, out string? resolvedName);
                    string sourcePath = file.originalPath ?? file.fullName;
                    string sourceStem = Path.GetFileNameWithoutExtension(sourcePath);
                    if (!skipContainer && !string.IsNullOrEmpty(container) &&
                        int.TryParse(container, NumberStyles.Integer, CultureInfo.InvariantCulture, out int containerHash) &&
                        uint.TryParse(sourceStem, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint resourceId))
                    {
                        string mapped = ResourceIndex.GetContainer(resourceId, unchecked((uint)containerHash));
                        if (!string.IsNullOrEmpty(mapped))
                        {
                            container = mapped;
                            if (obj is MiHoYoBinData) resolvedName = Path.GetFileNameWithoutExtension(mapped);
                        }
                    }

                    long size = GetFullAssetSize(obj);
                    rows.Add(new StudioAssetRow(
                        obj,
                        !string.IsNullOrWhiteSpace(resolvedName)
                            ? resolvedName
                            : string.IsNullOrWhiteSpace(obj.Name) ? $"{obj.type}_{obj.m_PathID}" : obj.Name,
                        container ?? string.Empty,
                        obj.type.ToString(),
                        obj.m_PathID,
                        size,
                        file.fileName,
                        sourcePath,
                        file.unityVersion));
                }
            }

            return rows
                .OrderBy(x => x.TypeName, StringComparer.Ordinal)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.PathId)
                .ToArray();
        }
    }

    public IReadOnlyList<StudioClassGroup> GetClassGroups()
    {
        lock (sync)
        {
            EnsureLoaded();
            return manager!.assetsFileList
                .GroupBy(file => string.IsNullOrWhiteSpace(file.unityVersion) ? "Unknown Unity version" : file.unityVersion)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new StudioClassGroup(
                    group.Key,
                    group.SelectMany(file => file.m_Types.Select(type => new StudioClassRow(
                        type.classID,
                        Enum.IsDefined(typeof(ClassIDType), type.classID)
                            ? ((ClassIDType)type.classID).ToString()
                            : $"ClassID {type.classID}",
                        type.m_KlassName ?? string.Empty,
                        type.m_NameSpace ?? string.Empty,
                        type.m_AsmName ?? string.Empty,
                        type.m_Type?.m_Nodes?.Select(node => new StudioTypeTreeRow(
                            node.m_Level,
                            node.m_Type ?? string.Empty,
                            node.m_Name ?? string.Empty,
                            node.m_ByteSize,
                            node.m_Version,
                            node.m_MetaFlag)).ToArray() ?? Array.Empty<StudioTypeTreeRow>())))
                        .GroupBy(row => (row.ClassId, row.KlassName, row.Namespace, row.Assembly))
                        .Select(g => g.First())
                        .OrderBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
                        .ToArray()))
                .ToArray();
        }
    }

    public IReadOnlyList<StudioSceneNode> GetSceneHierarchy()
    {
        lock (sync)
        {
            EnsureLoaded();

            var gameObjects = manager!.assetsFileList
                .SelectMany(file => file.Objects ?? new List<AssetStudio.Object>())
                .OfType<GameObject>()
                .ToArray();

            var nodes = gameObjects.ToDictionary(
                gameObject => gameObject,
                gameObject => new StudioSceneNode(
                    gameObject,
                    string.IsNullOrWhiteSpace(gameObject.Name) ? $"GameObject {gameObject.m_PathID}" : gameObject.Name,
                    gameObject.m_PathID,
                    gameObject.assetsFile?.fileName ?? string.Empty));

            var roots = new List<StudioSceneNode>();
            foreach (var gameObject in gameObjects)
            {
                var node = nodes[gameObject];
                GameObject? parent = null;

                try
                {
                    var transform = gameObject.m_Transform;
                    if (transform != null &&
                        !transform.m_Father.IsNull &&
                        transform.m_Father.TryGet(out var parentTransform) &&
                        parentTransform.m_GameObject.TryGet(out var parentObject))
                    {
                        parent = parentObject;
                    }
                }
                catch
                {
                    // An unresolved external transform becomes a root node;
                    // the original asset remains available in the asset list.
                }

                if (parent != null && !ReferenceEquals(parent, gameObject) && nodes.TryGetValue(parent, out var parentNode))
                    parentNode.Children.Add(node);
                else
                    roots.Add(node);
            }

            // A malformed parent cycle must not make the Android tree renderer recurse forever.
            foreach (var root in roots) TrimCycles(root, new HashSet<StudioSceneNode>());
            return roots.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    public string ExportModel(StudioAssetRow row, string outputFolder, StudioMobileSettings settings)
    {
        if (row == null) throw new ArgumentNullException(nameof(row));
        lock (sync)
        {
            EnsureLoaded();
            return StudioModelExportService.ExportSelected(row.Instance, manager!.assetsFileList.ToArray(), outputFolder, settings);
        }
    }

    public void ExportClassGroups(string outputFolder)
    {
        if (string.IsNullOrWhiteSpace(outputFolder)) throw new ArgumentException(nameof(outputFolder));
        Directory.CreateDirectory(outputFolder);

        foreach (var group in GetClassGroups())
        {
            string versionFolder = Path.Combine(outputFolder, SafeName(group.UnityVersion));
            Directory.CreateDirectory(versionFolder);
            foreach (var item in group.Classes)
            {
                string fileName = $"{item.ClassId} {SafeName(item.DisplayName)}.txt";
                string path = Path.Combine(versionFolder, fileName);
                using var writer = new StreamWriter(path, false, new System.Text.UTF8Encoding(false));
                writer.WriteLine($"Unity version: {group.UnityVersion}");
                writer.WriteLine(item.ToString());
                foreach (var node in item.Nodes)
                {
                    writer.WriteLine(
                        $"{new string(' ', Math.Clamp(node.Level, 0, 64) * 2)}{node.Type} {node.Name} " +
                        $"[size={node.ByteSize}, version={node.Version}, meta=0x{node.MetaFlags:X}]");
                }
            }
        }
    }

    public static string ExportAsset(StudioAssetRow row, string outputFolder)
    {
        if (row == null) throw new ArgumentNullException(nameof(row));
        return AssetBrowser.Export(
            new AssetEntry
            {
                Instance = row.Instance,
                Name = row.Name,
                TypeName = row.TypeName,
                Source = row.Source,
                PathId = row.PathId
            },
            outputFolder);
    }

    private AssetsManager CreateManager(string? unityVersion, bool resolveDependencies, bool skipProcess, int selectedGame)
    {
        return new AssetsManager
        {
            Game = GameManager.GetGame(selectedGame),
            SpecifyUnityVersion = unityVersion ?? string.Empty,
            Silent = true,
            SkipProcess = skipProcess,
            ResolveDependencies = resolveDependencies
        };
    }

    private Dictionary<AssetStudio.Object, string> BuildContainerMap(bool skipContainer)
    {
        var map = new Dictionary<AssetStudio.Object, string>();
        if (skipContainer) return map;

        foreach (var file in manager!.assetsFileList)
        {
            foreach (var obj in file.Objects ?? new List<AssetStudio.Object>())
            {
                switch (obj)
                {
                    case AssetBundle bundle:
                        foreach (var entry in bundle.m_Container)
                        {
                            int begin = Math.Max(0, entry.Value.preloadIndex);
                            int end = Math.Min(bundle.m_PreloadTable.Count, begin + Math.Max(0, entry.Value.preloadSize));
                            for (int i = begin; i < end; i++)
                            {
                                if (bundle.m_PreloadTable[i].TryGet(out var target) && !map.ContainsKey(target))
                                    map.Add(target, entry.Key);
                            }
                        }
                        break;
                    case ResourceManager resourceManager:
                        foreach (var entry in resourceManager.m_Container)
                        {
                            if (entry.Value.TryGet(out var target) && !map.ContainsKey(target))
                                map.Add(target, entry.Key);
                        }
                        break;
                }
            }
        }
        return map;
    }

    private Dictionary<AssetStudio.Object, string> BuildObjectNameMap(Dictionary<AssetStudio.Object, string> containerMap)
    {
        var names = new Dictionary<AssetStudio.Object, string>();
        foreach (var file in manager!.assetsFileList)
        {
            foreach (var indexObject in (file.Objects ?? new List<AssetStudio.Object>()).OfType<IndexObject>())
            {
                foreach (var entry in indexObject.AssetMap)
                {
                    if (!entry.Value.Object.TryGet<MiHoYoBinData>(out var binData)) continue;
                    if (int.TryParse(entry.Key, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hash))
                    {
                        names[binData] = entry.Key;
                        containerMap.TryAdd(binData, hash.ToString(CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        names[binData] = $"BinFile #{binData.m_PathID}";
                    }
                }
            }
        }
        return names;
    }

    private static long GetFullAssetSize(AssetStudio.Object obj)
    {
        long size = obj.byteSize;
        switch (obj)
        {
            case Texture2D texture when !string.IsNullOrEmpty(texture.m_StreamData?.path):
                size += texture.m_StreamData.size;
                break;
            case AudioClip audio when !string.IsNullOrEmpty(audio.m_Source):
                size += audio.m_Size;
                break;
            case VideoClip video when !string.IsNullOrEmpty(video.m_OriginalPath):
                size += video.m_ExternalResources.m_Size;
                break;
        }
        return size;
    }

    private static string[] NormalizeFiles(IEnumerable<string> paths)
    {
        if (paths == null) throw new ArgumentNullException(nameof(paths));
        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void EnsureLoaded()
    {
        if (manager == null) throw new InvalidOperationException("No Unity workspace is loaded.");
    }

    private void ResetCore()
    {
        try { manager?.Clear(); }
        finally { manager = null; }
    }

    private static void TrimCycles(StudioSceneNode node, HashSet<StudioSceneNode> ancestors)
    {
        if (!ancestors.Add(node)) return;
        for (int i = node.Children.Count - 1; i >= 0; i--)
        {
            if (ancestors.Contains(node.Children[i]))
                node.Children.RemoveAt(i);
            else
                TrimCycles(node.Children[i], new HashSet<StudioSceneNode>(ancestors));
        }
    }

    private static string SafeName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) || ch == '/' || ch == '\\' ? '_' : ch).ToArray();
        string result = new string(chars).Trim();
        return string.IsNullOrEmpty(result) ? "unnamed" : result;
    }

    public void Dispose() => Reset();
}

public sealed record StudioAssetRow(
    AssetStudio.Object Instance,
    string Name,
    string Container,
    string TypeName,
    long PathId,
    long Size,
    string Source,
    string SourcePath,
    string UnityVersion)
{
    public string Display => $"{Name}  ·  {TypeName}  ·  {PathId}";
}

public sealed record StudioClassGroup(string UnityVersion, IReadOnlyList<StudioClassRow> Classes);

public sealed record StudioClassRow(
    int ClassId,
    string TypeName,
    string KlassName,
    string Namespace,
    string Assembly,
    IReadOnlyList<StudioTypeTreeRow> Nodes)
{
    public string DisplayName => string.IsNullOrWhiteSpace(KlassName) ? TypeName : KlassName;
    public override string ToString() => $"{ClassId} | {TypeName} | {KlassName} | {Namespace} | {Assembly}";
}

public sealed record StudioTypeTreeRow(
    int Level,
    string Type,
    string Name,
    int ByteSize,
    int Version,
    int MetaFlags);

public sealed class StudioSceneNode
{
    public AssetStudio.GameObject Instance { get; }
    public string Name { get; }
    public long PathId { get; }
    public string Source { get; }
    public List<StudioSceneNode> Children { get; } = new();

    public StudioSceneNode(AssetStudio.GameObject instance, string name, long pathId, string source)
    {
        Instance = instance;
        Name = name;
        PathId = pathId;
        Source = source;
    }
}
