using GameManager = AssetStudio.GameManager;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using AssetStudio;

namespace HoKTool.Android;

/// <summary>
/// Explicit UnityFS build workflow:
/// 1) extract serialized-file entries (.assets/CAB-*) from a source bundle;
/// 2) replace one entry with an edited SerializedFile of identical size;
/// 3) rebuild all UnityFS blocks/directory, then reopen and byte-verify the result.
/// No source file is overwritten.
/// </summary>
public static class UnityFsBundleBuildService
{
    private const long MaxBundleBytes = 1024L * 1024L * 1024L;

    private sealed record Node(long Offset, long Size, uint Flags, string Path);

    public sealed record ExtractResult(int SerializedFiles, long Bytes, IReadOnlyList<string> Files);

    public static ExtractResult ExtractSerializedFiles(string bundlePath, string outputDirectory)
    {
        var game = GameManager.GetGame(GameType.HonorOfKings);
        using var reader = new FileReader(bundlePath);
        if (reader.FileType != FileType.BundleFile)
            throw new NotSupportedException("File không được nhận diện là Unity Bundle.");
        var bundle = new BundleFile(reader, game);
        if (!string.Equals(bundle.m_Header.signature, "UnityFS", StringComparison.Ordinal))
            throw new NotSupportedException("Chỉ trích .assets từ UnityFS; UnityWeb/UnityRaw chưa hỗ trợ.");
        Directory.CreateDirectory(outputDirectory);

        var outputs = new List<string>();
        long bytes = 0;
        foreach (var entry in bundle.fileList)
        {
            if (entry.stream == null || !entry.stream.CanRead || !entry.stream.CanSeek) continue;
            long prior = entry.stream.Position;
            try
            {
                if (entry.stream.Length < 20) continue;
                entry.stream.Position = 0;
                using var probe = new FileReader(entry.path, entry.stream, leaveOpen: true);
                if (probe.FileType != FileType.AssetsFile) continue;
            }
            catch { continue; }
            finally { entry.stream.Position = prior; }

            string relative = SafeRelativePath(entry.path);
            string destination = Path.Combine(outputDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (File.Exists(destination))
                throw new IOException("Trùng đường dẫn SerializedFile trong bundle: " + relative);
            long originalPosition = entry.stream.Position;
            entry.stream.Position = 0;
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024))
                CopyExactly(entry.stream, output, entry.stream.Length);
            entry.stream.Position = originalPosition;
            outputs.Add(destination);
            bytes = checked(bytes + new FileInfo(destination).Length);
        }
        if (outputs.Count == 0)
            throw new InvalidDataException("Không tìm thấy SerializedFile (.assets/CAB-*) bên trong UnityFS.");
        return new ExtractResult(outputs.Count, bytes, outputs);
    }

    public static string BuildFromSerializedFile(string bundlePath, string replacementSerializedFile, string outputDirectory)
    {
        if (!File.Exists(bundlePath)) throw new FileNotFoundException("Không tìm thấy UnityFS gốc.", bundlePath);
        if (!File.Exists(replacementSerializedFile)) throw new FileNotFoundException("Không tìm thấy SerializedFile đã sửa.", replacementSerializedFile);

        var game = GameManager.GetGame(GameType.HonorOfKings);
        BundleFile bundle;
        using (var reader = new FileReader(bundlePath))
        {
            if (reader.FileType != FileType.BundleFile)
                throw new NotSupportedException("File gốc không phải Unity Bundle.");
            bundle = new BundleFile(reader, game);
        }
        ValidateSupportedUnityFs(bundle);

        var directoryField = typeof(BundleFile).GetField("m_DirectoryInfo", BindingFlags.Instance | BindingFlags.NonPublic);
        if (directoryField?.GetValue(bundle) is not IList<BundleFile.Node> originalNodes ||
            originalNodes.Count != bundle.fileList.Count)
            throw new InvalidDataException("Không đọc được directory table; dừng build để tránh mất entry.");

        string replacementName = Path.GetFileName(replacementSerializedFile);
        var matches = bundle.fileList
            .Where(f => string.Equals(f.fileName, replacementName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(f.path, replacementName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count != 1)
            throw new InvalidDataException($"SerializedFile '{replacementName}' phải khớp đúng một entry trong UnityFS; tìm thấy {matches.Count}.");

        var target = matches[0];
        if (target.stream == null || !target.stream.CanWrite)
            throw new NotSupportedException("SerializedFile entry trong UnityFS không cho phép ghi; hãy dùng định dạng bundle được hỗ trợ.");
        var replacementInfo = new FileInfo(replacementSerializedFile);
        if (replacementInfo.Length <= 0 || replacementInfo.Length > MaxBundleBytes)
            throw new InvalidDataException($"SerializedFile thay thế vượt kích thước an toàn: {replacementInfo.Length} byte.");
        var targetBytes = File.ReadAllBytes(replacementSerializedFile);
        if (targetBytes.LongLength != target.stream.Length)
            throw new InvalidDataException($"Không thể build khi kích thước SerializedFile thay đổi: gốc {target.stream.Length}, mới {targetBytes.LongLength} byte. Cần giữ nguyên kích thước để mọi offset trong bundle không bị dịch.");

        long oldTargetPosition = target.stream.Position;
        target.stream.Position = 0;
        byte[] originalBytes = new byte[targetBytes.Length];
        target.stream.ReadExactly(originalBytes);
        target.stream.Position = 0;
        target.stream.Write(targetBytes, 0, targetBytes.Length);
        target.stream.Flush();
        target.stream.Position = oldTargetPosition;

        Directory.CreateDirectory(outputDirectory);
        string ext = Path.GetExtension(bundlePath);
        if (string.IsNullOrEmpty(ext)) ext = ".bundle";
        string outputPath = Path.Combine(outputDirectory,
            Path.GetFileNameWithoutExtension(bundlePath) + ".built." +
            DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ext);
        string tempData = outputPath + ".data.tmp";

        var nodes = new List<Node>(bundle.fileList.Count);
        var expected = new List<(string Path, long Size)>(bundle.fileList.Count);
        long dataLength = 0;
        try
        {
            using (var dataFile = new FileStream(tempData, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1024 * 1024, FileOptions.SequentialScan))
            {
                for (int i = 0; i < bundle.fileList.Count; i++)
                {
                    var entry = bundle.fileList[i];
                    var originalNode = originalNodes[i];
                    if (entry.stream == null || !entry.stream.CanRead || !entry.stream.CanSeek)
                        throw new NotSupportedException($"Entry {i} không thể đọc; không build thiếu dữ liệu.");
                    long length = entry.stream.Length;
                    if (length != originalNode.size)
                        throw new InvalidDataException($"Entry {i} có kích thước khác directory table gốc.");
                    if (length < 0 || dataLength > MaxBundleBytes - length || dataLength > uint.MaxValue - length)
                        throw new NotSupportedException("Tổng dữ liệu giải nén UnityFS vượt giới hạn build 1 GiB/32-bit.");
                    string path = string.IsNullOrEmpty(originalNode.path) ? entry.path : originalNode.path;
                    if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException($"Entry {i} không có đường dẫn.");
                    long prior = entry.stream.Position;
                    entry.stream.Position = 0;
                    CopyExactly(entry.stream, dataFile, length);
                    entry.stream.Position = prior;
                    nodes.Add(new Node(dataLength, length, originalNode.flags, path));
                    expected.Add((path, length));
                    dataLength = checked(dataLength + length);
                }

                if (dataLength == 0) throw new InvalidDataException("UnityFS rỗng.");
                dataFile.Flush(true);
                dataFile.Position = 0;
                byte[] dataHash;
                using (var md5 = MD5.Create()) dataHash = md5.ComputeHash(dataFile);
                dataFile.Position = 0;
                byte[] blocksInfo = BuildBlocksInfo(dataHash, dataLength, nodes);
                WriteUnityFs(outputPath, bundle.m_Header, blocksInfo, dataFile);
            }

            VerifyBuiltBundle(outputPath, game, expected, target.path, targetBytes, originalNodes.Count);
            return outputPath;
        }
        catch
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
            throw;
        }
        finally
        {
            if (File.Exists(tempData)) File.Delete(tempData);
        }
    }

    private static void ValidateSupportedUnityFs(BundleFile bundle)
    {
        if (!string.Equals(bundle.m_Header.signature, "UnityFS", StringComparison.Ordinal))
            throw new NotSupportedException("Build chỉ hỗ trợ UnityFS.");
        if (bundle.m_Header.version < 6 || bundle.m_Header.version > 7)
            throw new NotSupportedException($"UnityFS version {bundle.m_Header.version} chưa được xác nhận.");
        uint knownFlags = 0x7FF;
        if ((((uint)bundle.m_Header.flags) & ~knownFlags) != 0)
            throw new NotSupportedException("UnityFS có header flag không được hỗ trợ.");
        if ((bundle.m_Header.flags & ArchiveFlags.UnityCNEncryption) != 0)
            throw new NotSupportedException("UnityFS có UnityCNEncryption; không thể build bằng repacker này.");
    }

    private static byte[] BuildBlocksInfo(byte[] hash, long dataLength, IList<Node> nodes)
    {
        if (dataLength > uint.MaxValue) throw new NotSupportedException("UnityFS block vượt 32-bit.");
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory, Encoding.UTF8, leaveOpen: true);
        writer.Write(hash);
        WriteInt32BE(writer, 1);
        WriteUInt32BE(writer, (uint)dataLength);
        WriteUInt32BE(writer, (uint)dataLength);
        WriteUInt16BE(writer, 0);
        WriteInt32BE(writer, nodes.Count);
        foreach (var node in nodes)
        {
            WriteInt64BE(writer, node.Offset);
            WriteInt64BE(writer, node.Size);
            WriteUInt32BE(writer, node.Flags);
            WriteNullTerminated(writer, node.Path);
        }
        writer.Flush();
        return memory.ToArray();
    }

    private static void WriteUnityFs(string output, BundleFile.Header header, byte[] blocksInfo, Stream data)
    {
        ArchiveFlags flags = ArchiveFlags.BlocksAndDirectoryInfoCombined |
            (header.flags & ArchiveFlags.OldWebPluginCompatibility);
        using var target = new FileStream(output, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1024 * 1024, FileOptions.SequentialScan);
        using var writer = new BinaryWriter(target, Encoding.UTF8, leaveOpen: true);
        WriteNullTerminated(writer, "UnityFS");
        WriteUInt32BE(writer, header.version);
        WriteNullTerminated(writer, header.unityVersion ?? string.Empty);
        WriteNullTerminated(writer, header.unityRevision ?? string.Empty);
        long sizePosition = target.Position;
        WriteInt64BE(writer, 0);
        WriteUInt32BE(writer, (uint)blocksInfo.Length);
        WriteUInt32BE(writer, (uint)blocksInfo.Length);
        WriteUInt32BE(writer, (uint)flags);
        if (header.version >= 7) while ((target.Position & 15) != 0) writer.Write((byte)0);
        writer.Write(blocksInfo);
        data.Position = 0;
        data.CopyTo(target, 1024 * 1024);
        long finalLength = target.Length;
        target.Position = sizePosition;
        WriteInt64BE(writer, finalLength);
        writer.Flush();
        target.Flush(true);
    }

    private static void VerifyBuiltBundle(
        string output, Game game, IList<(string Path, long Size)> expected,
        string targetPath, byte[] replacement, int expectedCount)
    {
        using var reader = new FileReader(output);
        if (reader.FileType != FileType.BundleFile) throw new InvalidDataException("Build output không có Unity Bundle signature.");
        var rebuilt = new BundleFile(reader, game);
        if (rebuilt.fileList.Count != expectedCount || rebuilt.fileList.Count != expected.Count)
            throw new InvalidDataException("Số entry sau build không khớp nguồn.");
        for (int i = 0; i < expected.Count; i++)
        {
            var item = rebuilt.fileList[i];
            if (!string.Equals(item.path, expected[i].Path, StringComparison.Ordinal) || item.stream.Length != expected[i].Size)
                throw new InvalidDataException($"Entry {i} sau build không khớp tên/kích thước.");
        }
        var matches = rebuilt.fileList.Where(x => string.Equals(x.path, targetPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(x.fileName, Path.GetFileName(targetPath), StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count != 1) throw new InvalidDataException("Không tìm thấy đúng SerializedFile đã thay thế trong bundle mới.");
        var stream = matches[0].stream;
        if (stream.Length != replacement.LongLength) throw new InvalidDataException("SerializedFile sau build sai kích thước.");
        stream.Position = 0;
        byte[] check = new byte[replacement.Length];
        stream.ReadExactly(check);
        if (!check.AsSpan().SequenceEqual(replacement))
            throw new InvalidDataException("Byte SerializedFile trong Bundle mới không khớp file thay thế.");
    }

    private static string SafeRelativePath(string path)
    {
        string normalized = path.Replace('\\', '/').TrimStart('/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(p => p == "." || p == ".." || p.Contains(':')))
            throw new InvalidDataException("Đường dẫn entry UnityFS không an toàn.");
        return Path.Combine(parts);
    }

    private static void CopyExactly(Stream input, Stream output, long size)
    {
        byte[] buffer = new byte[1024 * 1024];
        long remaining = size;
        while (remaining > 0)
        {
            int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read <= 0) throw new EndOfStreamException("Entry UnityFS bị cắt cụt khi copy.");
            output.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static void WriteNullTerminated(BinaryWriter writer, string value)
    {
        writer.Write(Encoding.UTF8.GetBytes(value));
        writer.Write((byte)0);
    }
    private static void WriteUInt16BE(BinaryWriter writer, ushort value) { var b=BitConverter.GetBytes(value); if(BitConverter.IsLittleEndian)Array.Reverse(b); writer.Write(b); }
    private static void WriteUInt32BE(BinaryWriter writer, uint value) { var b=BitConverter.GetBytes(value); if(BitConverter.IsLittleEndian)Array.Reverse(b); writer.Write(b); }
    private static void WriteInt32BE(BinaryWriter writer, int value) { var b=BitConverter.GetBytes(value); if(BitConverter.IsLittleEndian)Array.Reverse(b); writer.Write(b); }
    private static void WriteInt64BE(BinaryWriter writer, long value) { var b=BitConverter.GetBytes(value); if(BitConverter.IsLittleEndian)Array.Reverse(b); writer.Write(b); }
}
