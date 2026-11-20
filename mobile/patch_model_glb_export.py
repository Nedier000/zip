from pathlib import Path
import re

browser_path = Path("mobile/AssetBrowser.cs")
main_path = Path("mobile/MainActivity.cs")
project_path = Path("mobile/HOKTool.Android.csproj")
for path in (browser_path, main_path, project_path):
    if not path.is_file():
        raise SystemExit(f"Required Android port source file is missing: {path}")

browser = browser_path.read_text(encoding="utf-8")
if "public string ExportModel(" not in browser:
    anchor = "    public void Clear() => workspace.Reset();"
    if browser.count(anchor) != 1:
        raise SystemExit("Could not locate AssetBrowser workspace Clear method")
    browser = browser.replace(
        anchor,
        anchor + """
    public string ExportModel(AssetEntry entry, string outputFolder, StudioMobileSettings settings)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));
        var row = new StudioAssetRow(
            entry.Instance, entry.Name, string.Empty, entry.TypeName,
            entry.PathId, entry.Instance.byteSize, entry.Source, entry.Source, string.Empty);
        return workspace.ExportModel(row, outputFolder, settings);
    }
""",
        1)

main = main_path.read_text(encoding="utf-8")
if "ExportSelectedModelGlbAsync(entry)" not in main:
    pattern = re.compile(
        r"(private void ShowSelectedAsset\(\)\s*\{[\s\S]*?)(var layout\s*=\s*new LinearLayout\(this\)[^;]*;)",
        re.M
    )
    match = pattern.search(main)
    if not match:
        # Some versions name the local preview container differently. Insert
        # immediately before the first preview branch, where both entry and
        # layout are already in scope.
        branch = "            if (entry.Instance is AssetStudio.AudioClip)"
        if main.count(branch) != 1:
            raise SystemExit("Could not locate selected-asset preview layout for the GLB export action")
        main = main.replace(
            branch,
            """            AddButton(layout, "EXPORT MODEL GLB", "#0F766E", "#FFFFFF",
                async () => await ExportSelectedModelGlbAsync(entry));

""" + branch,
            1)
    else:
        insertion = match.group(2) + """
            AddButton(layout, "EXPORT MODEL GLB", "#0F766E", "#FFFFFF",
                async () => await ExportSelectedModelGlbAsync(entry));"""
        main = main[:match.start(2)] + insertion + main[match.end(2):]

if "private async Task ExportSelectedModelGlbAsync(AssetEntry entry)" not in main:
    anchor = "    private void ShowSelectedAsset()"
    if main.count(anchor) != 1:
        raise SystemExit("Could not find ShowSelectedAsset method to add GLB export handler")
    method = r"""
    private async Task ExportSelectedModelGlbAsync(AssetEntry entry)
    {
        if (assetOutputUri == null)
        {
            Toast.MakeText(this, "Chọn thư mục xuất trước.", ToastLength.Long)!.Show();
            return;
        }
        if (entry?.Instance == null)
        {
            Toast.MakeText(this, "Chưa chọn asset hợp lệ.", ToastLength.Long)!.Show();
            return;
        }

        await RunBusyAsync(async token =>
        {
            string output = Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "studio_glb_export");
            if (Directory.Exists(output)) Directory.Delete(output, true);
            Directory.CreateDirectory(output);
            mobileSettings.ApplyCoreSettings();
            string file = await Task.Run(() => assetBrowser.ExportModel(entry, output, mobileSettings), token);
            await SafTree.CopyLocalToTreeAsync(
                this, output, assetOutputUri!,
                new Progress<(int done, string name)>(p => WriteStatus($"Đang chép GLB {p.done}: {p.name}")),
                token);
            WriteStatus($"Đã xuất model GLB: {Path.GetFileName(file)}");
            Toast.MakeText(this, "Đã xuất model GLB.", ToastLength.Long)!.Show();
        });
    }

"""
    main = main.replace(anchor, method + anchor, 1)

# AIVersionManager needs HTTPS access; only add the Android permission if absent.
project = project_path.read_text(encoding="utf-8")
if "android.permission.INTERNET" not in project:
    insertion = "<ItemGroup>\n    <UsesPermission Include=\"android.permission.INTERNET\" />\n  </ItemGroup>\n"
    marker = "</Project>"
    if project.count(marker) != 1:
        raise SystemExit("Could not locate Android project closing element")
    project = project.replace(marker, insertion + marker, 1)

browser_path.write_text(browser, encoding="utf-8")
main_path.write_text(main, encoding="utf-8")
project_path.write_text(project, encoding="utf-8")
print("Added the managed GLB action to the selected-asset preview and enabled internet permission.")
