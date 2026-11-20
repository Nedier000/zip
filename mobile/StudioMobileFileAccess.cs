using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Net;
using Android.Provider;

namespace HoKTool.Android;

/// <summary>
/// SAF document picker bridge for the desktop Load file(s) workflow.
/// Copies selected documents into app-private working storage before parsing.
/// </summary>
public static class StudioMobileFileAccess
{
    public static async Task<IReadOnlyList<string>> CopyUrisToLocalAsync(
        Activity activity,
        IReadOnlyList<Uri> uris,
        string destinationFolder,
        IProgress<(int done, string name)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (uris == null || uris.Count == 0)
            throw new ArgumentException("No Android document URIs were selected.", nameof(uris));

        Directory.CreateDirectory(destinationFolder);
        var written = new List<string>(uris.Count);

        for (int i = 0; i < uris.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Uri uri = uris[i];
            string name = GetDisplayName(activity, uri);
            string safeName = SafeFileName(name);
            string outputPath = UniquePath(destinationFolder, safeName);

            using var input = activity.ContentResolver?.OpenInputStream(uri)
                ?? throw new IOException($"Không mở được tệp {name} từ Storage Access Framework.");
            await using (var output = new FileStream(
                outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, 1024 * 1024, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            written.Add(outputPath);
            progress?.Report((i + 1, name));
        }

        return written;
    }

    private static string GetDisplayName(Activity activity, Uri uri)
    {
        try
        {
            using var cursor = activity.ContentResolver?.Query(
                uri, new[] { OpenableColumns.DisplayName }, null, null, null);
            if (cursor != null && cursor.MoveToFirst())
            {
                int column = cursor.GetColumnIndex(OpenableColumns.DisplayName);
                if (column >= 0)
                {
                    string? value = cursor.GetString(column);
                    if (!string.IsNullOrWhiteSpace(value)) return value;
                }
            }
        }
        catch { }
        return uri.LastPathSegment ?? "selected_file.bin";
    }

    private static string UniquePath(string folder, string fileName)
    {
        string path = Path.Combine(folder, fileName);
        if (!File.Exists(path)) return path;
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        for (int i = 1; i < int.MaxValue; i++)
        {
            path = Path.Combine(folder, $"{stem} ({i}){ext}");
            if (!File.Exists(path)) return path;
        }
        throw new IOException("Không thể tạo tên file duy nhất cho tài liệu đã chọn.");
    }

    private static string SafeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string result = new string(value.Select(ch =>
            invalid.Contains(ch) || ch == '/' || ch == '\\' ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(result) ? "selected_file.bin" : result;
    }
}
