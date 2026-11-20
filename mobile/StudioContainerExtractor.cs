using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using AssetStudio;

namespace HoKTool.Android;

/// <summary>
/// Portable counterpart of Studio.ExtractFile / ExtractFolder.
/// Handles the same stream containers without any WinForms dependencies.
/// Every entry path is normalized and checked before writing.
/// </summary>
public sealed class StudioContainerExtractor
{
    private readonly Game game;
    private readonly int maximumNestingDepth;

    public StudioContainerExtractor(Game? game = null, int maximumNestingDepth = 6)
    {
        this.game = game ?? GameManager.GetGame(GameType.HonorOfKings);
        this.maximumNestingDepth = Math.Clamp(maximumNestingDepth, 1, 12);
    }

    public ContainerExtractionResult ExtractFiles(
        IEnumerable<string> inputFiles,
        string outputFolder,
        IProgress<ContainerExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (inputFiles == null) throw new ArgumentNullException(nameof(inputFiles));
        if (string.IsNullOrWhiteSpace(outputFolder)) throw new ArgumentException(nameof(outputFolder));

        string outputRoot = Path.GetFullPath(outputFolder);
        Directory.CreateDirectory(outputRoot);
        var result = new MutableResult();

        string[] files = inputFiles
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (int i = 0; i < files.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string file = files[i];
            string destination = Path.Combine(outputRoot, SafeName(Path.GetFileName(file)) + "_unpacked");
            Directory.CreateDirectory(destination);
            try
            {
                using var reader = new FileReader(file);
                ExtractReader(reader.PreProcessing(game), destination, result, cancellationToken, 0);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result.Warnings.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }

            progress?.Report(new ContainerExtractionProgress(i + 1, files.Length, result.FilesWritten,
                $"Đã xử lý {i + 1}/{files.Length}: {Path.GetFileName(file)}"));
        }

        return result.ToResult();
    }

    public ContainerExtractionResult ExtractFolder(
        string inputFolder,
        string outputFolder,
        IProgress<ContainerExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(inputFolder) || !Directory.Exists(inputFolder))
            throw new DirectoryNotFoundException(inputFolder);

        string inputRoot = Path.GetFullPath(inputFolder);
        string outputRoot = Path.GetFullPath(outputFolder);
        Directory.CreateDirectory(outputRoot);

        var files = Directory.GetFiles(inputRoot, "*", SearchOption.AllDirectories)
            .Where(path => !IsInside(path, outputRoot))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return ExtractFiles(files, outputRoot, progress, cancellationToken);
    }

    private void ExtractReader(
        FileReader reader,
        string destination,
        MutableResult result,
        CancellationToken token,
        int depth)
    {
        token.ThrowIfCancellationRequested();
        if (depth > maximumNestingDepth)
            throw new InvalidDataException($"Container nesting exceeds {maximumNestingDepth} levels.");

        switch (reader.FileType)
        {
            case FileType.BundleFile:
            {
                var bundle = new BundleFile(reader, game);
                ExtractStreams(bundle.fileList, destination, result, token);
                break;
            }
            case FileType.WebFile:
            {
                var web = new WebFile(reader);
                ExtractStreams(web.fileList, destination, result, token);
                break;
            }
            case FileType.ZipFile:
                ExtractZip(reader, destination, result, token);
                break;
            case FileType.GZipFile:
                ExtractCompressed(reader, destination, result, token, depth, gzip: true);
                break;
            case FileType.BrotliFile:
                ExtractCompressed(reader, destination, result, token, depth, gzip: false);
                break;
            case FileType.BlockFile:
                ExtractBlock(reader, destination, result, token, depth);
                break;
            case FileType.BlkFile when game is Blk blk:
                ExtractBlk(reader, blk, destination, result, token, depth);
                break;
            case FileType.MhyFile when game is Mhy mhy:
            {
                var mhyFile = new MhyFile(reader, mhy);
                ExtractStreams(mhyFile.fileList, destination, result, token);
                break;
            }
            case FileType.BlkFile:
                result.Warnings.Add($"{reader.FileName}: BLK requires a matching Blk game profile.");
                break;
            case FileType.MhyFile:
                result.Warnings.Add($"{reader.FileName}: mhy0 requires a matching Mhy game profile.");
                break;
            case FileType.QtsVFSFile:
                result.Warnings.Add($"{reader.FileName}: QTS VFS must be processed by the dedicated HoK DB extractor.");
                break;
            case FileType.ENCRFile:
                result.Warnings.Add($"{reader.FileName}: encrypted ENCR container is not supported by this generic extractor.");
                break;
            default:
                result.Warnings.Add($"{reader.FileName}: {reader.FileType} is not an extractable container.");
                break;
        }
    }

    private void ExtractBlock(FileReader reader, string destination, MutableResult result, CancellationToken token, int depth)
    {
        using var stream = new OffsetStream(reader.BaseStream, 0);
        int index = 0;
        while (stream.Remaining > 0)
        {
            token.ThrowIfCancellationRequested();
            long absolute = stream.AbsolutePosition;
            stream.Offset = absolute;
            string pseudoName = Path.Combine(reader.FullPath, absolute.ToString("X8"));
            using var nested = new FileReader(pseudoName, stream, leaveOpen: true);
            string childFolder = Path.Combine(destination, $"block_{index++:D4}");
            Directory.CreateDirectory(childFolder);
            ExtractReader(nested, childFolder, result, token, depth + 1);

            if (stream.AbsolutePosition <= absolute)
                throw new InvalidDataException("Block container parser made no forward progress.");
        }
    }

    private void ExtractBlk(FileReader reader, Blk blk, string destination, MutableResult result, CancellationToken token, int depth)
    {
        using var stream = BlkUtils.Decrypt(reader, blk);
        int index = 0;
        while (stream.Remaining > 0)
        {
            token.ThrowIfCancellationRequested();
            long absolute = stream.AbsolutePosition;
            stream.Offset = absolute;
            string pseudoName = Path.Combine(reader.FullPath, absolute.ToString("X8"));
            using var nested = new FileReader(pseudoName, stream, leaveOpen: true);
            string childFolder = Path.Combine(destination, $"blk_{index++:D4}");
            Directory.CreateDirectory(childFolder);
            ExtractReader(nested, childFolder, result, token, depth + 1);

            if (stream.AbsolutePosition <= absolute)
                throw new InvalidDataException("BLK parser made no forward progress.");
        }
    }

    private void ExtractCompressed(
        FileReader reader,
        string destination,
        MutableResult result,
        CancellationToken token,
        int depth,
        bool gzip)
    {
        string temp = Path.Combine(destination, ".decompressed_" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            reader.BaseStream.Position = 0;
            using (Stream decoder = gzip
                ? new GZipStream(reader.BaseStream, CompressionMode.Decompress, leaveOpen: true)
                : new BrotliStream(reader.BaseStream, CompressionMode.Decompress, leaveOpen: true))
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                decoder.CopyTo(output);
            }

            using var nested = new FileReader(temp);
            ExtractReader(nested.PreProcessing(game), destination, result, token, depth + 1);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static void ExtractZip(FileReader reader, string destination, MutableResult result, CancellationToken token)
    {
        reader.BaseStream.Position = 0;
        using var archive = new ZipArchive(reader.BaseStream, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name)) continue;
            using var input = entry.Open();
            string outputPath = SafeEntryPath(destination, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using var output = new FileStream(UniquePath(outputPath), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
            result.AddFile(new FileInfo(output.Name).Length);
        }
    }

    private static void ExtractStreams(
        IEnumerable<StreamFile> files,
        string destination,
        MutableResult result,
        CancellationToken token)
    {
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            if (file.stream == null) continue;
            try
            {
                string relative = string.IsNullOrWhiteSpace(file.path) ? file.fileName : file.path;
                string outputPath = SafeEntryPath(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                outputPath = UniquePath(outputPath);
                file.stream.Position = 0;
                using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                file.stream.CopyTo(output);
                result.AddFile(output.Length);
            }
            finally
            {
                file.stream.Dispose();
            }
        }
    }

    private static string SafeEntryPath(string root, string entryPath)
    {
        if (string.IsNullOrWhiteSpace(entryPath))
            throw new InvalidDataException("Container entry has an empty path.");

        string normalized = entryPath.Replace('\\', '/');
        if (Path.IsPathRooted(normalized) || normalized.Contains(':'))
            throw new InvalidDataException($"Absolute container entry path rejected: {entryPath}");

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(part => part == "." || part == ".."))
            throw new InvalidDataException($"Unsafe container entry path rejected: {entryPath}");

        string fullRoot = Path.GetFullPath(root);
        string fullPath = Path.GetFullPath(Path.Combine(fullRoot, Path.Combine(parts)));
        string prefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Container entry escapes output folder: {entryPath}");
        return fullPath;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        string folder = Path.GetDirectoryName(path)!;
        string stem = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        for (int i = 1; i < int.MaxValue; i++)
        {
            string candidate = Path.Combine(folder, $"{stem} ({i}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
        throw new IOException("Unable to allocate a unique output file name.");
    }

    private static string SafeName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string result = new string(value.Select(ch => invalid.Contains(ch) || ch == '/' || ch == '\\' ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(result) ? "container" : result;
    }

    private static bool IsInside(string path, string root)
    {
        string fullPath = Path.GetFullPath(path);
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class MutableResult
    {
        public int FilesWritten;
        public long BytesWritten;
        public List<string> Warnings { get; } = new();

        public void AddFile(long bytes)
        {
            FilesWritten++;
            BytesWritten = checked(BytesWritten + bytes);
        }

        public ContainerExtractionResult ToResult() =>
            new(FilesWritten, BytesWritten, Warnings.ToArray());
    }
}

public sealed record ContainerExtractionProgress(int Current, int Total, int FilesWritten, string Message);

public sealed record ContainerExtractionResult(int FilesWritten, long BytesWritten, IReadOnlyList<string> Warnings);
