using System.IO;
using GameManager = AssetStudio.GameManager;
using System;
using System.Collections.Generic;
using System.Linq;
using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;

namespace HoKTool.Android;

/// <summary>
/// Android-native replacements for the desktop Scene Hierarchy, Asset Classes,
/// Export Options and settings dialogs.
/// </summary>
public static class StudioMobilePanels
{
    public static void ShowSceneHierarchy(
        Activity activity,
        IReadOnlyList<StudioSceneNode> roots,
        Action<IReadOnlyList<StudioSceneNode>>? selectionChanged = null)
    {
        if (roots == null || roots.Count == 0)
        {
            Toast.MakeText(activity, "Không tìm thấy GameObject trong dữ liệu đã quét.", ToastLength.Long)!.Show();
            return;
        }

        var rows = new List<StudioSceneNodeRow>();
        foreach (var root in roots) Flatten(root, 0, rows, 0, 100_000);
        var list = new ListView(activity)
        {
            ChoiceMode = ChoiceMode.Multiple,
            DividerHeight = 1
        };
        list.Adapter = new ArrayAdapter<string>(
            activity,
            Android.Resource.Layout.SimpleListItemMultipleChoice,
            rows.Select(row => row.Display).ToArray());

        var wrapper = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        wrapper.SetPadding(8, 8, 8, 8);
        wrapper.AddView(new TextView(activity)
        {
            Text = $"Scene Hierarchy · {rows.Count:N0} GameObject. Đánh dấu các node cần xuất GLB.",
            TextSize = 13
        });
        wrapper.AddView(list, new LinearLayout.LayoutParams(-1, activity.Resources!.DisplayMetrics!.HeightPixels * 2 / 3));

        new AlertDialog.Builder(activity)
            .SetTitle("SCENE HIERARCHY")
            .SetView(wrapper)
            .SetNegativeButton("ĐÓNG", (_, _) => { })
            .SetPositiveButton("DÙNG NODE ĐÃ CHỌN", (_, _) =>
            {
                var selected = new List<StudioSceneNode>();
                for (int i = 0; i < rows.Count; i++)
                    if (list.IsItemChecked(i)) selected.Add(rows[i].Node);
                selectionChanged?.Invoke(selected);
                Toast.MakeText(activity, $"Đã chọn {selected.Count:N0} node.", ToastLength.Short)!.Show();
            })
            .Show();
    }

    public static void ShowClassBrowser(Activity activity, IReadOnlyList<StudioClassGroup> groups)
    {
        if (groups == null || groups.Count == 0)
        {
            Toast.MakeText(activity, "Không có TypeTree / class structure trong dữ liệu đã quét.", ToastLength.Long)!.Show();
            return;
        }

        var versionNames = groups.Select(x => x.UnityVersion).ToArray();
        new AlertDialog.Builder(activity)
            .SetTitle("CHỌN UNITY VERSION")
            .SetItems(versionNames, (_, args) =>
            {
                int index = args.Which;
                if (index < 0 || index >= groups.Count) return;
                ShowClassList(activity, groups[index]);
            })
            .SetNegativeButton("HỦY", (_, _) => { })
            .Show();
    }

    public static void ExportClassStructures(
        Activity activity,
        IReadOnlyList<StudioClassGroup> groups,
        string outputFolder,
        Action<string>? completed = null)
    {
        try
        {
            // Export is driven from the same class-group snapshot used by the
            // browser; this method writes the exact TypeTree rows, not a stub.
            System.IO.Directory.CreateDirectory(outputFolder);
            foreach (var group in groups)
            {
                string versionFolder = System.IO.Path.Combine(outputFolder, SafeName(group.UnityVersion));
                System.IO.Directory.CreateDirectory(versionFolder);
                foreach (var item in group.Classes)
                {
                    string path = System.IO.Path.Combine(versionFolder, $"{item.ClassId} {SafeName(item.DisplayName)}.txt");
                    using var writer = new System.IO.StreamWriter(path, false, new System.Text.UTF8Encoding(false));
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
            completed?.Invoke($"Đã xuất cấu trúc {groups.Sum(x => x.Classes.Count):N0} class.");
        }
        catch (Exception ex)
        {
            new AlertDialog.Builder(activity)
                .SetTitle("Lỗi xuất Class Structures")
                .SetMessage(ex.Message)
                .SetPositiveButton("ĐÓNG", (_, _) => { })
                .Show();
        }
    }

    public static void ShowSettingsDialog(
        Activity activity,
        StudioMobileSettings settings,
        Action? saved = null)
    {
        var scroll = new ScrollView(activity);
        var content = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        content.SetPadding(18, 8, 18, 8);
        scroll.AddView(content);

        var checks = new List<(CheckBox Box, Action<bool> Apply)>();
        AddCheck(content, checks, "Display all assets", settings.displayAll, v => settings.displayAll = v);
        AddCheck(content, checks, "Enable preview", settings.enablePreview, v => settings.enablePreview = v);
        AddCheck(content, checks, "Display asset information", settings.displayInfo, v => settings.displayInfo = v);
        AddCheck(content, checks, "Resolve dependencies", settings.enableResolveDependencies, v => settings.enableResolveDependencies = v);
        AddCheck(content, checks, "Allow duplicate export names", settings.allowDuplicates, v => settings.allowDuplicates = v);
        AddCheck(content, checks, "Skip container recovery", settings.skipContainer, v => settings.skipContainer = v);
        AddCheck(content, checks, "Filter models only", settings.modelsOnly, v => settings.modelsOnly = v);
        AddCheck(content, checks, "Enable model preview", settings.enableModelPreview, v => settings.enableModelPreview = v);
        AddCheck(content, checks, "Convert textures", settings.convertTexture, v => settings.convertTexture = v);
        AddCheck(content, checks, "Convert audio when supported", settings.convertAudio, v => settings.convertAudio = v);
        AddCheck(content, checks, "Restore original file extensions", settings.restoreExtensionName, v => settings.restoreExtensionName = v);
        AddCheck(content, checks, "Export skins", settings.exportSkins, v => settings.exportSkins = v);
        AddCheck(content, checks, "Export materials", settings.exportMaterials, v => settings.exportMaterials = v);
        AddCheck(content, checks, "Export animations", settings.exportAnimations, v => settings.exportAnimations = v);
        AddCheck(content, checks, "Export blend shapes", settings.exportBlendShape, v => settings.exportBlendShape = v);
        AddCheck(content, checks, "Export all model nodes", settings.exportAllNodes, v => settings.exportAllNodes = v);
        AddCheck(content, checks, "Collect animations", settings.collectAnimations, v => settings.collectAnimations = v);
        AddCheck(content, checks, "Euler filter", settings.eulerFilter, v => settings.eulerFilter = v);
        AddCheck(content, checks, "Cast missing bones to bone", settings.castToBone, v => settings.castToBone = v);
        AddCheck(content, checks, "Enable file logging", settings.enableFileLogging, v => settings.enableFileLogging = v);
        AddCheck(content, checks, "Enable debug console", settings.enableConsole, v => settings.enableConsole = v);
        AddCheck(content, checks, "Minimal AssetMap", settings.minimalAssetMap, v => settings.minimalAssetMap = v);
        AddCheck(content, checks, "Encrypted MiHoYo BinData", settings.encrypted, v => settings.encrypted = v);

        AddLabel(content, "Game profile");
        string[] gameNames = GameManager.GetGameNames();
        var gameSpinner = new Spinner(activity);
        gameSpinner.Adapter = new ArrayAdapter<string>(activity, Android.Resource.Layout.SimpleSpinnerItem, gameNames);
        gameSpinner.SetSelection(Math.Clamp(settings.selectedGame, 0, Math.Max(0, gameNames.Length - 1)));
        content.AddView(gameSpinner, MatchWrap());

        var typeFlagsButton = new Button(activity) { Text = "TYPE PARSE / EXPORT FLAGS", TextSize = 12 };
        typeFlagsButton.SetAllCaps(false);
        typeFlagsButton.Click += (_, _) => ShowTypeFlagsDialog(activity, settings, saved);
        content.AddView(typeFlagsButton, MatchWrap());

        AddActionButton(content, "UV CHANNEL MAPPING (UV0–UV7)", () =>
            ShowUvMappingDialog(activity, settings));
        AddActionButton(content, "TEXTURE NAME / SEMANTIC MAPPING", () =>
            ShowTextureMappingDialog(activity, settings));
        AddActionButton(content, "AI VERSION / CABMAP / UNITYCN", () =>
            ShowMapToolsDialog(activity, settings));

        AddLabel(content, "Unity version override");
        var unityVersion = new EditText(activity) { Text = settings.specifyUnityVersion, Hint = "Ví dụ: 2021.3.XXf1" };
        content.AddView(unityVersion, MatchWrap());

        AddLabel(content, "Texture output format");
        var format = new Spinner(activity);
        var formats = new[] { "Png", "Jpeg", "Bmp", "Tga" };
        format.Adapter = new ArrayAdapter<string>(activity, Android.Resource.Layout.SimpleSpinnerItem, formats);
        format.SetSelection(Math.Max(0, Array.IndexOf(formats, settings.convertType)));
        content.AddView(format, MatchWrap());

        AddLabel(content, "Asset grouping");
        var grouping = new Spinner(activity);
        string[] groups = { "Type name", "Container path", "Source file name", "Do not group" };
        grouping.Adapter = new ArrayAdapter<string>(activity, Android.Resource.Layout.SimpleSpinnerItem, groups);
        grouping.SetSelection(Math.Clamp(settings.assetGroupOption, 0, groups.Length - 1));
        content.AddView(grouping, MatchWrap());

        AddLabel(content, "Model scale factor");
        var scale = new EditText(activity) { Text = settings.scaleFactor.ToString(System.Globalization.CultureInfo.InvariantCulture), InputType = Android.Text.InputTypes.ClassNumber | Android.Text.InputTypes.NumberFlagDecimal };
        content.AddView(scale, MatchWrap());

        AddLabel(content, "Bone size");
        var bone = new EditText(activity) { Text = settings.boneSize.ToString(System.Globalization.CultureInfo.InvariantCulture), InputType = Android.Text.InputTypes.ClassNumber | Android.Text.InputTypes.NumberFlagDecimal };
        content.AddView(bone, MatchWrap());

        AddLabel(content, "Filter precision");
        var precision = new EditText(activity) { Text = settings.filterPrecision.ToString(System.Globalization.CultureInfo.InvariantCulture), InputType = Android.Text.InputTypes.ClassNumber | Android.Text.InputTypes.NumberFlagDecimal };
        content.AddView(precision, MatchWrap());

        AddLabel(content, "MiHoYo key (byte)");
        var key = new EditText(activity) { Text = settings.key.ToString(), InputType = Android.Text.InputTypes.ClassNumber };
        content.AddView(key, MatchWrap());

        new AlertDialog.Builder(activity)
            .SetTitle("STUDIO-HOK · EXPORT OPTIONS")
            .SetView(scroll)
            .SetNegativeButton("HỦY", (_, _) => { })
            .SetNeutralButton("RESET DEFAULTS", (_, _) =>
            {
                var defaults = new StudioMobileSettings();
                CopySettings(defaults, settings);
                settings.Save();
                saved?.Invoke();
            })
            .SetPositiveButton("LƯU", (_, _) =>
            {
                foreach (var item in checks) item.Apply(item.Box.Checked);
                settings.selectedGame = Math.Max(0, gameSpinner.SelectedItemPosition);
                settings.specifyUnityVersion = unityVersion.Text ?? string.Empty;
                settings.convertType = format.SelectedItem?.ToString() ?? "Png";
                settings.assetGroupOption = Math.Max(0, grouping.SelectedItemPosition);
                if (decimal.TryParse(scale.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scaleValue))
                    settings.scaleFactor = Math.Clamp(scaleValue, 0.001m, 1000m);
                if (decimal.TryParse(bone.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var boneValue))
                    settings.boneSize = Math.Clamp(boneValue, 0.01m, 10000m);
                if (decimal.TryParse(precision.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var precisionValue))
                    settings.filterPrecision = Math.Clamp(precisionValue, 0.0001m, 100m);
                if (byte.TryParse(key.Text, out var keyValue)) settings.key = keyValue;
                settings.Save();
                saved?.Invoke();
                Toast.MakeText(activity, "Đã lưu cấu hình Studio-HoK.", ToastLength.Short)!.Show();
            })
            .Show();
    }


    private static void AddActionButton(LinearLayout parent, string title, Action action)
    {
        var button = new Button(parent.Context) { Text = title, TextSize = 12 };
        button.SetAllCaps(false);
        button.Click += (_, _) => action();
        parent.AddView(button, MatchWrap());
    }

    private static readonly string[] TextureSemantics =
    {
        "Diffuse", "NormalMap", "Specular", "Bump",
        "Ambient", "Emissive", "Reflection", "Displacement"
    };

    private static Dictionary<string, (bool, int)> ReadUvSettings(StudioMobileSettings settings)
    {
        Dictionary<string, (bool, int)> result;
        try
        {
            result = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, (bool, int)>>(settings.uvs)
                ?? new Dictionary<string, (bool, int)>();
        }
        catch { result = new Dictionary<string, (bool, int)>(); }

        for (int i = 0; i < 8; i++)
        {
            string name = "UV" + i;
            if (!result.ContainsKey(name)) result[name] = (i < 2, i == 1 ? 1 : 0);
            var item = result[name];
            result[name] = (item.Item1, Math.Clamp(item.Item2, 0, TextureSemantics.Length - 1));
        }
        return result;
    }

    private static void ShowUvMappingDialog(Activity activity, StudioMobileSettings settings)
    {
        var values = ReadUvSettings(settings);
        var scroll = new ScrollView(activity);
        var content = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        content.SetPadding(14, 8, 14, 8);
        scroll.AddView(content);
        var editors = new Dictionary<string, (CheckBox Enabled, Spinner Semantic)>();

        for (int i = 0; i < 8; i++)
        {
            string name = "UV" + i;
            var row = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
            row.SetGravity(GravityFlags.CenterVertical);
            var enabled = new CheckBox(activity) { Text = name, Checked = values[name].Item1, TextSize = 13 };
            row.AddView(enabled, new LinearLayout.LayoutParams(Dp(activity, 76), Dp(activity, 48)));
            var semantic = new Spinner(activity);
            semantic.Adapter = new ArrayAdapter<string>(activity, Android.Resource.Layout.SimpleSpinnerItem, TextureSemantics);
            semantic.SetSelection(values[name].Item2);
            row.AddView(semantic, new LinearLayout.LayoutParams(0, Dp(activity, 48), 1));
            content.AddView(row, MatchWrap());
            editors[name] = (enabled, semantic);
        }

        new AlertDialog.Builder(activity)
            .SetTitle("UV CHANNEL MAPPING")
            .SetView(scroll)
            .SetNegativeButton("HỦY", (_, _) => { })
            .SetPositiveButton("LƯU", (_, _) =>
            {
                foreach (var item in editors)
                    values[item.Key] = (item.Value.Enabled.Checked, Math.Max(0, item.Value.Semantic.SelectedItemPosition));
                settings.uvs = Newtonsoft.Json.JsonConvert.SerializeObject(values);
                settings.Save();
                Toast.MakeText(activity, "Đã lưu UV0–UV7.", ToastLength.Short)!.Show();
            })
            .Show();
    }

    private static void ShowTextureMappingDialog(Activity activity, StudioMobileSettings settings)
    {
        Dictionary<string, int> mappings;
        try
        {
            mappings = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, int>>(settings.texs)
                ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
        catch { mappings = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); }

        var root = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        root.SetPadding(16, 8, 16, 8);
        var name = new EditText(activity) { Hint = "Tên texture / chuỗi cần nhận diện" };
        root.AddView(name, MatchWrap());
        var semantic = new Spinner(activity);
        semantic.Adapter = new ArrayAdapter<string>(activity, Android.Resource.Layout.SimpleSpinnerItem, TextureSemantics);
        root.AddView(semantic, MatchWrap());

        var existing = new ListView(activity);
        void Refresh()
        {
            var labels = mappings.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Key + "  →  " + TextureSemantics[Math.Clamp(x.Value, 0, TextureSemantics.Length - 1)])
                .ToArray();
            existing.Adapter = new ArrayAdapter<string>(activity, Android.Resource.Layout.SimpleListItem1, labels);
        }
        void RemoveSelected(object? sender, AdapterView.ItemClickEventArgs args)
        {
            var key = mappings.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ElementAtOrDefault(args.Position).Key;
            if (key != null) mappings.Remove(key);
            Refresh();
        }
        existing.ItemClick += RemoveSelected;
        Refresh();

        var add = new Button(activity) { Text = "THÊM / CẬP NHẬT MAPPING" };
        add.SetAllCaps(false);
        add.Click += (_, _) =>
        {
            string key = (name.Text ?? string.Empty).Trim();
            if (key.Length == 0)
            {
                Toast.MakeText(activity, "Nhập tên texture trước.", ToastLength.Short)!.Show();
                return;
            }
            mappings[key] = Math.Clamp(semantic.SelectedItemPosition, 0, TextureSemantics.Length - 1);
            name.Text = string.Empty;
            Refresh();
        };
        root.AddView(add, MatchWrap());
        root.AddView(new TextView(activity) { Text = "Chạm vào một dòng để xóa mapping.", TextSize = 11 });
        root.AddView(existing, new LinearLayout.LayoutParams(-1, Dp(activity, 210)));

        new AlertDialog.Builder(activity)
            .SetTitle("TEXTURE NAME / SEMANTIC")
            .SetView(root)
            .SetNegativeButton("HỦY", (_, _) => { })
            .SetPositiveButton("LƯU", (_, _) =>
            {
                settings.texs = Newtonsoft.Json.JsonConvert.SerializeObject(mappings);
                settings.Save();
                Toast.MakeText(activity, $"Đã lưu {mappings.Count:N0} texture mapping.", ToastLength.Short)!.Show();
            })
            .Show();
    }

    private static void ShowMapToolsDialog(Activity activity, StudioMobileSettings settings)
    {
        try
        {
            StudioMobileMapService.Initialize(System.IO.Path.Combine(activity.FilesDir!.AbsolutePath, "Studio-HoK"));
        }
        catch (Exception ex)
        {
            ShowError(activity, "Không khởi tạo được thư mục AI / Maps", ex);
            return;
        }

        string[] tools =
        {
            "Cập nhật danh sách AI Version",
            "Tải / chọn AI Version",
            "Tải CABMap đã có",
            "Chọn UnityCN key"
        };

        new AlertDialog.Builder(activity)
            .SetTitle("STUDIO-HOK · MAP / AI TOOLS")
            .SetItems(tools, async (_, args) =>
            {
                try
                {
                    switch (args.Which)
                    {
                        case 0:
                        {
                            bool ok = await StudioMobileMapService.RefreshAIVersionsAsync();
                            Toast.MakeText(activity, ok ? "Đã cập nhật danh sách AI Version." : "Không lấy được danh sách AI Version.", ToastLength.Long)!.Show();
                            break;
                        }
                        case 1:
                        {
                            var versions = StudioMobileMapService.GetAIVersions();
                            if (versions.Count == 0)
                            {
                                Toast.MakeText(activity, "Danh sách AI Version trống. Hãy cập nhật trước.", ToastLength.Long)!.Show();
                                break;
                            }
                            string[] labels = versions.Select(x => x.Version + (x.Downloaded ? "  [đã tải]" : "  [chưa tải]")).ToArray();
                            new AlertDialog.Builder(activity)
                                .SetTitle("CHỌN AI VERSION")
                                .SetItems(labels, async (_, selected) =>
                                {
                                    try
                                    {
                                        var loaded = await StudioMobileMapService.DownloadAndLoadAIAsync(versions[selected.Which].Version);
                                        settings.selectedAIVersion = versions[selected.Which].Version;
                                        settings.Save();
                                        Toast.MakeText(activity, $"AI đã tải và nạp: {Path.GetFileName(loaded.Path)} · {loaded.AssetCount:N0} asset index entries.", ToastLength.Long)!.Show();
                                    }
                                    catch (Exception ex) { ShowError(activity, "Lỗi tải AI", ex); }
                                })
                                .SetNegativeButton("ĐÓNG", (_, _) => { })
                                .Show();
                            break;
                        }
                        case 2:
                        {
                            var maps = StudioMobileMapService.GetCABMaps();
                            if (maps.Count == 0)
                            {
                                Toast.MakeText(activity, "Chưa có CABMap trong thư mục Maps.", ToastLength.Long)!.Show();
                                break;
                            }
                            new AlertDialog.Builder(activity)
                                .SetTitle("CHỌN CABMAP")
                                .SetItems(maps.ToArray(), (_, selected) =>
                                {
                                    bool ok = StudioMobileMapService.LoadCABMap(maps[selected.Which]);
                                    if (ok)
                                    {
                                        settings.selectedCABMapName = maps[selected.Which];
                                        settings.Save();
                                    }
                                    Toast.MakeText(activity, ok ? "Đã nạp CABMap: " + maps[selected.Which] : "CABMap không nạp được.", ToastLength.Long)!.Show();
                                })
                                .SetNegativeButton("ĐÓNG", (_, _) => { })
                                .Show();
                            break;
                        }
                        case 3:
                        {
                            var keys = StudioMobileMapService.GetUnityCNKeys();
                            if (keys.Count == 0)
                            {
                                Toast.MakeText(activity, "Không có UnityCN key. Kiểm tra Keys.json.", ToastLength.Long)!.Show();
                                break;
                            }
                            new AlertDialog.Builder(activity)
                                .SetTitle("CHỌN UNITYCN KEY")
                                .SetItems(keys.ToArray(), (_, selected) =>
                                {
                                    bool ok = StudioMobileMapService.SetUnityCNKey(selected.Which);
                                    if (ok)
                                    {
                                        settings.selectedUnityCNKey = selected.Which;
                                        settings.Save();
                                    }
                                    Toast.MakeText(activity, ok ? "Đã chọn UnityCN key." : "UnityCN key không hợp lệ.", ToastLength.Long)!.Show();
                                })
                                .SetNegativeButton("ĐÓNG", (_, _) => { })
                                .Show();
                            break;
                        }
                    }
                }
                catch (Exception ex) { ShowError(activity, "Map / AI tool error", ex); }
            })
            .SetNegativeButton("ĐÓNG", (_, _) => { })
            .Show();
    }

    private static void ShowError(Activity activity, string title, Exception ex)
    {
        new AlertDialog.Builder(activity)
            .SetTitle(title)
            .SetMessage(ex.Message)
            .SetPositiveButton("ĐÓNG", (_, _) => { })
            .Show();
    }

    private static void ShowTypeFlagsDialog(Activity activity, StudioMobileSettings settings, Action? saved)
    {
        Dictionary<AssetStudio.ClassIDType, (bool, bool)> map;
        try
        {
            map = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<AssetStudio.ClassIDType, (bool, bool)>>(settings.types)
                ?? new Dictionary<AssetStudio.ClassIDType, (bool, bool)>();
        }
        catch
        {
            map = new Dictionary<AssetStudio.ClassIDType, (bool, bool)>();
        }

        var scroll = new ScrollView(activity);
        var content = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        content.SetPadding(12, 8, 12, 8);
        scroll.AddView(content);

        var editors = new Dictionary<AssetStudio.ClassIDType, (CheckBox Parse, CheckBox Export)>();
        foreach (var item in map.OrderBy(x => x.Key.ToString(), StringComparer.Ordinal))
        {
            var row = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
            var label = new TextView(activity)
            {
                Text = item.Key.ToString(),
                TextSize = 12,
                Gravity = GravityFlags.CenterVertical
            };
            row.AddView(label, new LinearLayout.LayoutParams(0, Dp(activity, 42), 1));

            var parse = new CheckBox(activity) { Text = "Parse", TextSize = 11, Checked = item.Value.Item1 };
            var export = new CheckBox(activity) { Text = "Export", TextSize = 11, Checked = item.Value.Item2 };
            row.AddView(parse, new LinearLayout.LayoutParams(Dp(activity, 76), Dp(activity, 42)));
            row.AddView(export, new LinearLayout.LayoutParams(Dp(activity, 82), Dp(activity, 42)));
            content.AddView(row);
            editors[item.Key] = (parse, export);
        }

        new AlertDialog.Builder(activity)
            .SetTitle("TYPE FLAGS · PARSE / EXPORT")
            .SetView(scroll)
            .SetNegativeButton("HỦY", (_, _) => { })
            .SetPositiveButton("LƯU", (_, _) =>
            {
                foreach (var item in editors)
                    map[item.Key] = (item.Value.Parse.Checked, item.Value.Export.Checked);
                settings.types = Newtonsoft.Json.JsonConvert.SerializeObject(map);
                settings.ApplyCoreSettings();
                settings.Save();
                saved?.Invoke();
                Toast.MakeText(activity, "Đã lưu Type Parse/Export flags.", ToastLength.Short)!.Show();
            })
            .Show();
    }

    private static int Dp(Activity activity, int value) =>
        (int)(value * activity.Resources!.DisplayMetrics!.Density + 0.5f);

    private static void ShowClassList(Activity activity, StudioClassGroup group)
    {
        var classes = group.Classes;
        var names = classes.Select(item => item.DisplayName + $"  ·  ID {item.ClassId}").ToArray();
        new AlertDialog.Builder(activity)
            .SetTitle($"{group.UnityVersion} · {classes.Count:N0} classes")
            .SetItems(names, (_, args) =>
            {
                if (args.Which < 0 || args.Which >= classes.Count) return;
                var item = classes[args.Which];
                var body = new System.Text.StringBuilder();
                body.AppendLine(item.ToString());
                body.AppendLine();
                foreach (var node in item.Nodes)
                    body.AppendLine($"{new string(' ', Math.Clamp(node.Level, 0, 64) * 2)}{node.Type} {node.Name}  [size={node.ByteSize}, version={node.Version}, meta=0x{node.MetaFlags:X}]");

                var text = new TextView(activity)
                {
                    Text = body.ToString(),
                    TextSize = 12,
                    Typeface = Typeface.Monospace
                };
                text.SetTextIsSelectable(true);
                var scroller = new ScrollView(activity);
                scroller.SetPadding(16, 12, 16, 12);
                scroller.AddView(text);
                new AlertDialog.Builder(activity)
                    .SetTitle(item.DisplayName)
                    .SetView(scroller)
                    .SetPositiveButton("ĐÓNG", (_, _) => { })
                    .Show();
            })
            .SetNegativeButton("ĐÓNG", (_, _) => { })
            .Show();
    }

    private static void Flatten(StudioSceneNode node, int depth, List<StudioSceneNodeRow> rows, int count, int limit)
    {
        if (rows.Count >= limit) return;
        rows.Add(new StudioSceneNodeRow(node, depth));
        foreach (var child in node.Children)
        {
            if (rows.Count >= limit) break;
            Flatten(child, depth + 1, rows, count + 1, limit);
        }
    }

    private static void AddCheck(LinearLayout parent, List<(CheckBox Box, Action<bool> Apply)> items, string label, bool value, Action<bool> apply)
    {
        var box = new CheckBox(parent.Context) { Text = label, Checked = value, TextSize = 14 };
        parent.AddView(box, MatchWrap());
        items.Add((box, apply));
    }

    private static void AddLabel(LinearLayout parent, string label)
    {
        var view = new TextView(parent.Context) { Text = label, TextSize = 12 };
        view.SetTextColor(Color.Rgb(90, 100, 116));
        view.SetPadding(0, 10, 0, 2);
        parent.AddView(view, MatchWrap());
    }

    private static LinearLayout.LayoutParams MatchWrap() => new(-1, -2);

    private static string SafeName(string value)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) || ch == '/' || ch == '\\' ? '_' : ch).ToArray();
        string name = new string(chars).Trim();
        return string.IsNullOrEmpty(name) ? "Unknown" : name;
    }

    private static void CopySettings(StudioMobileSettings source, StudioMobileSettings target)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(source);
        var reset = System.Text.Json.JsonSerializer.Deserialize<StudioMobileSettings>(json)!;
        foreach (var property in typeof(StudioMobileSettings).GetProperties())
        {
            if (property.CanWrite) property.SetValue(target, property.GetValue(reset));
        }
    }

    private sealed record StudioSceneNodeRow(StudioSceneNode Node, int Depth)
    {
        public string Display => new string(' ', Math.Min(Depth, 16) * 2) +
            (Depth == 0 ? "▾ " : "• ") + Node.Name + $"  [{Node.PathId}]";
    }
}
