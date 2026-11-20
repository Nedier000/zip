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
    public static void ShowSceneHierarchy(Activity activity, IReadOnlyList<StudioSceneNode> roots)
    {
        if (roots == null || roots.Count == 0)
        {
            Toast.MakeText(activity, "Không tìm thấy GameObject trong dữ liệu đã quét.", ToastLength.Long)!.Show();
            return;
        }

        var rows = new List<StudioSceneNodeRow>();
        foreach (var root in roots) Flatten(root, 0, rows, 0, 100_000);

        var list = new ListView(activity);
        list.Adapter = new ArrayAdapter<string>(
            activity,
            Android.Resource.Layout.SimpleListItem1,
            rows.Select(row => row.Display).ToArray());
        list.DividerHeight = 1;

        list.ItemClick += (_, args) =>
        {
            if (args.Position < 0 || args.Position >= rows.Count) return;
            var row = rows[args.Position];
            new AlertDialog.Builder(activity)
                .SetTitle(row.Node.Name)
                .SetMessage($"PathID: {row.Node.PathId}\nSource: {row.Node.Source}\nChildren: {row.Node.Children.Count}")
                .SetPositiveButton("ĐÓNG", (_, _) => { })
                .Show();
        };

        var wrapper = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        wrapper.SetPadding(8, 8, 8, 8);
        wrapper.AddView(new TextView(activity)
        {
            Text = $"Scene Hierarchy · {rows.Count:N0} GameObject",
            TextSize = 13
        });
        wrapper.AddView(list, new LinearLayout.LayoutParams(-1, activity.Resources!.DisplayMetrics!.HeightPixels * 2 / 3));

        new AlertDialog.Builder(activity)
            .SetTitle("SCENE HIERARCHY")
            .SetView(wrapper)
            .SetPositiveButton("ĐÓNG", (_, _) => { })
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

        AddLabel(content, "Unity version override");
        var unityVersion = new EditText(activity) { Text = settings.specifyUnityVersion, Hint = "Ví dụ: 2021.3.XXf1" };
        content.AddView(unityVersion, MatchWrap());

        AddLabel(content, "Texture output format");
        var format = new Spinner(activity);
        var formats = new[] { "Png", "Jpeg", "Bmp", "Tga", "Webp" };
        format.Adapter = new ArrayAdapter<string>(activity, Android.Resource.Layout.SimpleSpinnerItem, formats);
        format.SetSelection(Math.Max(0, Array.IndexOf(formats, settings.convertType)));
        content.AddView(format, MatchWrap());

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
            row.AddView(label, new LayoutParams(0, Dp(activity, 42), 1));

            var parse = new CheckBox(activity) { Text = "Parse", TextSize = 11, Checked = item.Value.Item1 };
            var export = new CheckBox(activity) { Text = "Export", TextSize = 11, Checked = item.Value.Item2 };
            row.AddView(parse, new LayoutParams(Dp(activity, 76), Dp(activity, 42)));
            row.AddView(export, new LayoutParams(Dp(activity, 82), Dp(activity, 42)));
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
