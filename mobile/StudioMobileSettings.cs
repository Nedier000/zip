using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using AssetStudio;
using Newtonsoft.Json;

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
    public string uvs { get; set; } = @"{""UV0"":{""Item1"":true,""Item2"":0},""UV1"":{""Item1"":true,""Item2"":1},""UV2"":{""Item1"":false,""Item2"":0},""UV3"":{""Item1"":false,""Item2"":0},""UV4"":{""Item1"":false,""Item2"":0},""UV5"":{""Item1"":false,""Item2"":0},""UV6"":{""Item1"":false,""Item2"":0},""UV7"":{""Item1"":false,""Item2"":0}}";
    public bool allowDuplicates { get; set; } = false;
    public int loggerEventType { get; set; } = 0;
    public string types { get; set; } = @"{""Animation"":{""Item1"":true,""Item2"":false},""AnimationClip"":{""Item1"":true,""Item2"":true},""Animator"":{""Item1"":true,""Item2"":true},""AnimatorController"":{""Item1"":true,""Item2"":false},""AnimatorOverrideController"":{""Item1"":true,""Item2"":false},""AssetBundle"":{""Item1"":true,""Item2"":false},""AudioClip"":{""Item1"":true,""Item2"":true},""Avatar"":{""Item1"":true,""Item2"":false},""Font"":{""Item1"":true,""Item2"":true},""GameObject"":{""Item1"":true,""Item2"":false},""IndexObject"":{""Item1"":true,""Item2"":false},""Material"":{""Item1"":true,""Item2"":true},""Mesh"":{""Item1"":true,""Item2"":true},""MeshFilter"":{""Item1"":true,""Item2"":false},""MeshRenderer"":{""Item1"":true,""Item2"":false},""MiHoYoBinData"":{""Item1"":true,""Item2"":true},""MonoBehaviour"":{""Item1"":true,""Item2"":true},""MonoScript"":{""Item1"":true,""Item2"":false},""MovieTexture"":{""Item1"":true,""Item2"":true},""PlayerSettings"":{""Item1"":true,""Item2"":false},""RectTransform"":{""Item1"":true,""Item2"":false},""Shader"":{""Item1"":true,""Item2"":true},""SkinnedMeshRenderer"":{""Item1"":true,""Item2"":false},""Sprite"":{""Item1"":true,""Item2"":true},""SpriteAtlas"":{""Item1"":true,""Item2"":false},""TextAsset"":{""Item1"":true,""Item2"":true},""Texture2D"":{""Item1"":true,""Item2"":true},""Transform"":{""Item1"":true,""Item2"":false},""VideoClip"":{""Item1"":true,""Item2"":true},""ResourceManager"":{""Item1"":true,""Item2"":false}}";
    public string texs { get; set; } = "{}";
    public bool exportMaterials { get; set; } = true;
    public string specifyUnityVersion { get; set; } = string.Empty;
    public string selectedAIVersion { get; set; } = string.Empty;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public void ApplyCoreSettings()
    {
        try
        {
            var map = JsonConvert.DeserializeObject<Dictionary<ClassIDType, (bool, bool)>>(types);
            TypeFlags.SetTypes(map);
        }
        catch
        {
            TypeFlags.SetTypes(null);
        }

        MiHoYoBinData.Encrypted = encrypted;
        MiHoYoBinData.Key = key;
        AssetsHelper.Minimal = minimalAssetMap;
        try
        {
            var keys = UnityCNManager.GetEntries();
            if (selectedUnityCNKey >= 0 && selectedUnityCNKey < keys.Length)
                UnityCN.SetKey(keys[selectedUnityCNKey]);
        }
        catch
        {
            // Missing key catalog must not prevent ordinary Unity files from loading.
        }
    }

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
