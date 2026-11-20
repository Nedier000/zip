from pathlib import Path
import re

browser_path = Path("mobile/AssetBrowser.cs")
main_path = Path("mobile/MainActivity.cs")
if not browser_path.is_file() or not main_path.is_file():
    raise SystemExit("Restored Studio-HoK Android source is incomplete")

browser = browser_path.read_text(encoding="utf-8")
main = main_path.read_text(encoding="utf-8")

# Route the existing Android asset browser through the shared workspace service.
# This keeps one AssetsManager alive for the asset list, scene tree and type trees.
pattern = re.compile(
    r"    private AssetsManager\? manager;\s*"
    r"    public ScanResult Scan\(.*?\n    public static Bitmap\? CreatePreviewBitmap",
    re.S
)
replacement = r'''    private readonly StudioWorkspaceService workspace = new();

    public ScanResult Scan(
        string inputFolder,
        Action<string>? log = null,
        string? unityVersion = null,
        bool resolveDependencies = true,
        int selectedGame = (int)AssetStudio.GameType.HonorOfKings)
    {
        if (!Directory.Exists(inputFolder))
            throw new DirectoryNotFoundException(inputFolder);

        workspace.LoadFolder(
            inputFolder,
            unityVersion,
            resolveDependencies,
            skipProcess: false,
            log: log,
            selectedGame: selectedGame);

        var rows = workspace.GetAssetRows(includeAllObjects: true);
        var objects = rows.Select(row => new AssetEntry
        {
            Instance = row.Instance,
            Name = row.Name,
            TypeName = row.TypeName,
            Source = row.Source,
            PathId = row.PathId
        }).ToList();

        int files = workspace.SerializedFiles.Count;
        string summary = string.Join("\n", objects.GroupBy(x => x.TypeName)
            .OrderByDescending(g => g.Count()).Take(14)
            .Select(g => $"{g.Key}: {g.Count():N0}"));

        log?.Invoke($"Đã đọc {files} file Unity, {objects.Count} object.");
        return new ScanResult(files, objects.Count, objects, summary);
    }

    public IReadOnlyList<StudioSceneNode> GetSceneHierarchy() => workspace.GetSceneHierarchy();
    public IReadOnlyList<StudioClassGroup> GetClassGroups() => workspace.GetClassGroups();
    public void ExportClassGroups(string outputFolder) => workspace.ExportClassGroups(outputFolder);
    public void Clear() => workspace.Reset();

    public static Bitmap? CreatePreviewBitmap'''
browser_new, n = pattern.subn(replacement, browser, count=1)
if n != 1:
    raise SystemExit("Could not uniquely replace AssetBrowser.Scan with the shared workspace")

# Add workspace state to the Android Activity.
if "private IReadOnlyList<StudioSceneNode> sceneRoots" not in main:
    anchor = "    private readonly AssetBrowser assetBrowser = new();"
    if main.count(anchor) != 1:
        raise SystemExit("Could not locate AssetBrowser field in MainActivity")
    main = main.replace(
        anchor,
        anchor + "\n"
        "    private readonly StudioMobileSettings mobileSettings = StudioMobileSettings.Load();\n"
        "    private IReadOnlyList<StudioSceneNode> sceneRoots = Array.Empty<StudioSceneNode>();\n"
        "    private IReadOnlyList<StudioClassGroup> classGroups = Array.Empty<StudioClassGroup>();",
        1
    )

# Add Android equivalents for desktop Export Options and the two desktop data browsers.
settings_anchor = "        body.AddView(statusCard);"
settings_ui = settings_anchor + '''
        AddButton(statusCard, "STUDIO SETTINGS / EXPORT OPTIONS", "#E8EEF8", "#1E3A5F",
            () => { ShowStudioSettings(); return Task.CompletedTask; });'''
if "ShowStudioSettings();" not in main:
    if main.count(settings_anchor) != 1:
        raise SystemExit("Could not locate status card in MainActivity")
    main = main.replace(settings_anchor, settings_ui, 1)

summary_anchor = "            assetCard.AddView(summaryText);"
workspace_ui = summary_anchor + '''
            AddButton(assetCard, "SCENE HIERARCHY", "#E8EEF8", "#1E3A5F",
                () => { ShowSceneHierarchy(); return Task.CompletedTask; });
            AddButton(assetCard, "ASSET CLASSES / TYPE TREE", "#E8EEF8", "#1E3A5F",
                () => { ShowAssetClasses(); return Task.CompletedTask; });
            AddButton(assetCard, "EXPORT CLASS STRUCTURES", "#0F766E", "#FFFFFF",
                async () => await ExportClassStructuresAsync());'''
if "ShowAssetClasses();" not in main:
    if main.count(summary_anchor) != 1:
        raise SystemExit("Could not locate asset summary area in MainActivity")
    main = main.replace(summary_anchor, workspace_ui, 1)

# Apply the persisted Unity version and dependency-resolution preferences.
old_scan = "var result = await Task.Run(() => assetBrowser.Scan(input), token);"
new_scan = """mobileSettings.ApplyCoreSettings();
            var result = await Task.Run(
                () => assetBrowser.Scan(input, WriteStatus, mobileSettings.specifyUnityVersion,
                    mobileSettings.enableResolveDependencies, mobileSettings.selectedGame), token);
            var workspacePanels = await Task.Run(
                () => (assetBrowser.GetSceneHierarchy(), assetBrowser.GetClassGroups()), token);
            sceneRoots = workspacePanels.Item1;
            classGroups = workspacePanels.Item2;"""
if old_scan in main:
    main = main.replace(old_scan, new_scan, 1)
elif "mobileSettings.specifyUnityVersion" not in main:
    raise SystemExit("Could not locate ScanAssetsAsync AssetBrowser.Scan call")

# Add connected handlers before the asset preview method.
method_anchor = "    private void ShowSelectedAsset()"
methods = r"""
    private void ShowStudioSettings()
    {
        StudioMobilePanels.ShowSettingsDialog(this, mobileSettings, () =>
            WriteStatus("Cấu hình đã lưu. Các tùy chọn nạp file sẽ áp dụng từ lần quét tiếp theo."));
    }

    private void ShowSceneHierarchy()
    {
        StudioMobilePanels.ShowSceneHierarchy(this, sceneRoots);
    }

    private void ShowAssetClasses()
    {
        StudioMobilePanels.ShowClassBrowser(this, classGroups);
    }

    private async Task ExportClassStructuresAsync()
    {
        if (classGroups.Count == 0)
        {
            Toast.MakeText(this, "Chưa có Class Structures. Hãy quét dữ liệu Unity trước.", ToastLength.Long)!.Show();
            return;
        }
        if (assetOutputUri == null)
        {
            Toast.MakeText(this, "Chọn thư mục xuất asset trước.", ToastLength.Long)!.Show();
            return;
        }

        await RunBusyAsync(async token =>
        {
            string output = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "class_structures");
            if (Directory.Exists(output)) Directory.Delete(output, true);
            Directory.CreateDirectory(output);
            await Task.Run(() => assetBrowser.ExportClassGroups(output), token);
            await SafTree.CopyLocalToTreeAsync(
                this, output, assetOutputUri!,
                new Progress<(int done, string name)>(p => WriteStatus($"Đang xuất Class Structure {p.done}: {p.name}")),
                token);
            WriteStatus($"Đã xuất TypeTree của {classGroups.Sum(x => x.Classes.Count):N0} class.");
        });
    }

"""
if "private void ShowStudioSettings()" not in main:
    if main.count(method_anchor) != 1:
        raise SystemExit("Could not locate ShowSelectedAsset insertion point")
    main = main.replace(method_anchor, methods + method_anchor, 1)

browser_path.write_text(browser_new, encoding="utf-8")
main_path.write_text(main, encoding="utf-8")
print("Integrated shared workspace, Scene Hierarchy, Asset Classes and Android Export Options.")
