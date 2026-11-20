using System;
using System.IO;
using System.Text.Json;

namespace HoKTool.Android;

/// <summary>
/// Android-persisted equivalent of Studio-HoK Properties.Settings.Default.
/// Property names mirror the upstream Settings.settings contract so desktop
/// preferences can be mapped without silently dropping options.
/// </summary>
public sealed class StudioMobileSettings
{
    public bool displayAll { get; set; } = false;
    public bool enablePreview { get; set; } = true;
    public bool displayInfo { get; set; } = true;
    public bool openAfterExport { get; set; } = true;
    public int assetGroupOption { get; set; } = 0;
    public bool convertTexture { get; set; } = true;
    public bool convertAudio { get; set; } = true;
    public string convertType { get; set; } = "Png";
    public bool eulerFilter { get; set; } = true;
    public decimal filterPrecision { get; set; } = 0.25m;
    public bool exportAllNodes { get; set; } = true;
    public bool exportSkins { get; set; } = true;
    public bool exportAnimations { get; set; } = true;
    public decimal boneSize { get; set; } = 10m;
    public int fbxVersion { get; set; } = 3;
    public int fbxFormat { get; set; } = 0;
    public decimal scaleFactor { get; set; } = 1m;
    public bool exportBlendShape { get; set; } = true;
    public bool castToBone { get; set; } = false;
    public bool restoreExtensionName { get; set; } = false;
    public byte key { get; set; } = 0;
    public bool enableConsole { get; set; } = false;
    public bool encrypted { get; set; } = false;
    public int selectedGame { get; set; } = (int)AssetStudio.GameType.HonorOfKings;
    public bool enableResolveDependencies { get; set; } = true;
    public int assetMapType { get; set; } = 0;
    public bool collectAnimations { get; set; } = false;
    public bool skipContainer { get; set; } = false;
    public bool minimalAssetMap { get; set; } = false;
    public bool modelsOnly { get; set; } = false;
    public bool enableModelPreview { get; set; } = true;
    public int selectedUnityCNKey { get; set; } = 0;
    public string selectedCABMapName { get; set; } = string.Empty;
    public bool enableFileLogging { get; set; } = false;
    public string uvs { get; set; } = "{}";
    public bool allowDuplicates { get; set; } = false;
    public int loggerEventType { get; set; } = 0;
    public string types { get; set; } = "{}";
    public string texs { get; set; } = "{}";
    public bool exportMaterials { get; set; } = true;
    public string specifyUnityVersion { get; set; } = string.Empty;
    public string selectedAIVersion { get; set; } = string.Empty;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string SettingsPath
    {
        get
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root)) root = AppContext.BaseDirectory;
            return Path.Combine(root, "Studio-HoK-Android", "settings.json");
        }
    }

    public static StudioMobileSettings Load(string? path = null)
    {
        path ??= SettingsPath;
        try
        {
            if (!File.Exists(path)) return new StudioMobileSettings();
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<StudioMobileSettings>(json, JsonOptions)
                ?? new StudioMobileSettings();
        }
        catch
        {
            // A corrupt preference file must not prevent the application from opening.
            // The damaged file is retained for diagnosis; defaults are used in memory.
            return new StudioMobileSettings();
        }
    }

    public void Save(string? path = null)
    {
        path ??= SettingsPath;
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temp = fullPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
        if (File.Exists(fullPath))
        {
            string backup = fullPath + ".bak";
            File.Copy(fullPath, backup, overwrite: true);
            File.Move(temp, fullPath, overwrite: true);
        }
        else
        {
            File.Move(temp, fullPath);
        }
    }
}
