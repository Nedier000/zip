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
/// Rebuilds a standard, unencrypted UnityFS container after a same-size-or-smaller
/// SerializedFile object patch. All files inside the bundle are preserved; one
/// uncompressed data block is emitted and the result is parsed again before return.
/// </summary>
public static class UnityFsTextAssetRepacker
{
    private const long MaximumBundleDataBytes = 1024L * 1024L * 1024L;

    private sealed record OutputNode(long Offset, long Size, uint Flags, string Path);

    public static string CreatePatchedBundleCopy(
        AssetStudio.Object asset,
        byte[] originalObjectBytes,
        byte[] patchedObjectBytes,
        string outputDirectory)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));
        if (originalObjectBytes == null) throw new ArgumentNullException(nameof(originalObjectBytes));
        if (patchedObjectBytes == null) throw new ArgumentNullException(nameof(patchedObjectBytes));
        if (patchedObjectBytes.Length != originalObjectBytes.Length)
            throw new InvalidDataException("Object patch changed the serialized object size.");

        string bundlePath = asset.assetsFile.originalPath;
        if (string.IsNullOrWhiteSpace(bundlePath) || !File.Exists(bundlePath))
            throw new NotSupportedException("Không xác định được file UnityFS gốc chứa SerializedFile này.");

        Directory.CreateDirectory(outputDirectory);
        string extension = Path.GetExtension(bundlePath);
        if (string.IsNullOrEmpty(extension)) extension = ".bundle";
        string baseName = Path.GetFileNameWithoutExtension(bundlePath);
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        string outputPath = Path.Combine(outputDirectory, baseName + ".edited." + stamp + extension);
        string dataTempPath = outputPath + ".data.tmp";

        var game = asset.reader.Game ?? GameManager.GetGame(GameType.HonorOfKings);
        BundleFile bundle;
        using (var sourceReader = new FileReader(bundlePath))
        {
            if (sourceReader.FileType != FileType.BundleFile)
                throw new NotSupportedException("File nguồn không được nhận dạng là Unity Bundle.");
            bundle = new BundleFile(sourceReader, game);
        }

        if (!string.Equals(bundle.m_Header.signature, "UnityFS", StringComparison.Ordinal))
            throw new NotSupportedException("Chỉ hỗ trợ UnityFS chuẩn; UnityWeb/UnityRaw/ENCR không được ghi ngược bằng chế độ này.");
        if (bundle.m_Header.version < 6 || bundle.m_Header.version > 7)
            throw new NotSupportedException($"UnityFS version {bundle.m_Header.version} chưa được xác nhận cho repack.");
        uint knownHeaderFlags = 0x7FF;
        if ((((uint)bundle.m_Header.flags) & ~knownHeaderFlags) != 0)
            throw new NotSupportedException("UnityFS chứa header flag chưa được hỗ trợ; từ chối thay đổi container.");
        if ((bundle.m_Header.flags & ArchiveFlags.UnityCNEncryption) != 0)
            throw new NotSupportedException("Bundle có cờ UnityCNEncryption; không ghi lại container.");

        var matches = bundle.fileList
            .Where(f => string.Equals(f.fileName, asset.assetsFile.fileName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count != 1)
            throw new NotSupportedException(
                matches.Count == 0
                    ? "Không tìm thấy SerializedFile của TextAsset trong UnityFS gốc."
                    : "Có nhiều entry trùng tên SerializedFile; từ chối chọn nhầm entry.");

        var directoryField = typeof(BundleFile).GetField("m_DirectoryInfo", BindingFlags.Instance | BindingFlags.NonPublic);
        if (directoryField?.GetValue(bundle) is not IList<BundleFile.Node> originalNodes ||
            originalNodes.Count != bundle.fileList.Count)
            throw new InvalidDataException("Không đọc được directory table của UnityFS; dừng để tránh làm mất entry.");

        var targetEntry = matches[0];
        long objectOffset = asset.reader.byteStart;
        if (!targetEntry.stream.CanWrite)
            throw new NotSupportedException("SerializedFile entry trong UnityFS không cho phép ghi.");
        if (objectOffset < 0 || objectOffset + originalObjectBytes.Length > targetEntry.stream.Length)
            throw new InvalidDataException("Object offset nằm ngoài SerializedFile trong UnityFS.");

        byte[] currentObject = new byte[originalObjectBytes.Length];
        targetEntry.stream.Position = objectOffset;
        targetEntry.stream.ReadExactly(currentObject);
        if (!currentObject.AsSpan().SequenceEqual(originalObjectBytes))
            throw new InvalidDataException("SerializedFile trong bundle thay đổi so với dữ liệu đã quét; hủy ghi để tránh patch nhầm.");
        targetEntry.stream.Position = objectOffset;
        targetEntry.stream.Write(patchedObjectBytes, 0, patchedObjectBytes.Length);
        targetEntry.stream.Flush();

        var expected = new List<(string Path, long Size)>(bundle.fileList.Count);
        var outputNodes = new List<OutputNode>(bundle.fileList.Count);
        long dataLength = 0;

        try
        {
            using (var dataFile = new FileStream(dataTempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1024 * 1024, FileOptions.SequentialScan))
            {
                for (int i = 0; i < bundle.fileList.Count; i++)
                {
                    var file = bundle.fileList[i];
                    var originalNode = originalNodes[i];
                    if (file.stream == null || !file.stream.CanSeek || !file.stream.CanRead)
                        throw new NotSupportedException("Một entry trong bundle không thể đọc tuần tự; không tạo bundle thiếu dữ liệu.");

                    long size = file.stream.Length;
                    if (size != originalNode.size)
                        throw new InvalidDataException($"Kích thước entry {i} khác directory table gốc.");
                    if (size < 0 || dataLength > MaximumBundleDataBytes - size)
                        throw new NotSupportedException("Dữ liệu giải nén của bundle vượt giới hạn an toàn 1 GiB cho thao tác repack Android.");
                    if (dataLength > uint.MaxValue - size)
                        throw new NotSupportedException("UnityFS data block vượt giới hạn kích thước 32-bit.");

                    string path = string.IsNullOrEmpty(originalNode.path) ? file.path : originalNode.path;
                    if (string.IsNullOrEmpty(path))
                        throw new InvalidDataException("UnityFS directory entry không có đường dẫn.");

                    long oldPosition = file.stream.Position;
                    file.stream.Position = 0;
                    CopyExactly(file.stream, dataFile, size);
                    file.stream.Position = oldPosition;

                    outputNodes.Add(new OutputNode(dataLength, size, originalNode.flags, path));
                    expected.Add((path, size));
                    dataLength = checked(dataLength + size);
                }

                if (dataLength == 0)
                    throw new InvalidDataException("UnityFS không có payload; không thể tạo data block rỗng.");

                dataFile.Flush(flushToDisk: true);
                dataFile.Position = 0;
                byte[] dataHash;
                using (var md5 = MD5.Create())
                    dataHash = md5.ComputeHash(dataFile);
                dataFile.Position = 0;

                byte[] blocksInfo = BuildBlocksInfo(dataHash, dataLength, outputNodes);
                WriteUnityFs(outputPath, bundle.m_Header, blocksInfo, dataFile);
            }

            VerifyOutput(outputPath, game, expected, asset.assetsFile.fileName, objectOffset, patchedObjectBytes);
            return outputPath;
        }
        catch
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
            throw;
        }
        finally
        {
            if (File.Exists(dataTempPath)) File.Delete(dataTempPath);
        }
    }

    private static byte[] BuildBlocksInfo(byte[] dataHash, long dataLength, IList<OutputNode> nodes)
    {
        if (dataLength > uint.MaxValue)
            throw new NotSupportedException("UnityFS block size vượt giới hạn 32-bit.");

        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory, Encoding.UTF8, leaveOpen: true);
        writer.Write(dataHash);
        WriteInt32BE(writer, 1);
        WriteUInt32BE(writer, checked((uint)dataLength));
        WriteUInt32BE(writer, checked((uint)dataLength));
        WriteUInt16BE(writer, 0); // StorageBlockFlags.None
        WriteInt32BE(writer, nodes.Count);

        foreach (var node in nodes)
        {
            WriteInt64BE(writer, node.Offset);
            WriteInt64BE(writer, node.Size);
            WriteUInt32BE(writer, node.Flags);
            WriteNullTerminatedUtf8(writer, node.Path);
        }

        writer.Flush();
        return memory.ToArray();
    }

    private static void WriteUnityFs(string outputPath, BundleFile.Header originalHeader, byte[] blocksInfo, Stream dataFile)
    {
        ArchiveFlags outputFlags =
            ArchiveFlags.BlocksAndDirectoryInfoCombined |
            (originalHeader.flags & ArchiveFlags.OldWebPluginCompatibility);
        using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1024 * 1024, FileOptions.SequentialScan);
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);

        WriteNullTerminatedUtf8(writer, "UnityFS");
        WriteUInt32BE(writer, originalHeader.version);
        WriteNullTerminatedUtf8(writer, originalHeader.unityVersion ?? string.Empty);
        WriteNullTerminatedUtf8(writer, originalHeader.unityRevision ?? string.Empty);

        long sizeFieldPosition = output.Position;
        WriteInt64BE(writer, 0);
        WriteUInt32BE(writer, checked((uint)blocksInfo.Length));
        WriteUInt32BE(writer, checked((uint)blocksInfo.Length));
        WriteUInt32BE(writer, (uint)outputFlags);

        if (originalHeader.version >= 7)
        {
            while ((output.Position & 15) != 0)
                writer.Write((byte)0);
        }

        writer.Write(blocksInfo);
        dataFile.Position = 0;
        dataFile.CopyTo(output, 1024 * 1024);

        long finalSize = output.Length;
        output.Position = sizeFieldPosition;
        WriteInt64BE(writer, finalSize);
        writer.Flush();
        output.Flush(flushToDisk: true);
    }

    private static void VerifyOutput(
        string outputPath,
        Game game,
        IList<(string Path, long Size)> expected,
        string targetFileName,
        long objectOffset,
        byte[] patchedObjectBytes)
    {
        using var verifyReader = new FileReader(outputPath);
        if (verifyReader.FileType != FileType.BundleFile)
            throw new InvalidDataException("Bundle vừa tạo không vượt qua nhận diện chữ ký UnityFS.");

        var verifyBundle = new BundleFile(verifyReader, game);
        if (verifyBundle.fileList.Count != expected.Count)
            throw new InvalidDataException("Số lượng entry sau repack không khớp bundle nguồn.");

        for (int i = 0; i < expected.Count; i++)
        {
            if (!string.Equals(verifyBundle.fileList[i].path, expected[i].Path, StringComparison.Ordinal) ||
                verifyBundle.fileList[i].stream.Length != expected[i].Size)
                throw new InvalidDataException($"Entry {i} sau repack không khớp tên hoặc kích thước.");
        }

        var targetMatches = verifyBundle.fileList
            .Where(f => string.Equals(f.fileName, targetFileName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (targetMatches.Count != 1)
            throw new InvalidDataException("Không xác minh được SerializedFile đích trong bundle sau repack.");

        var stream = targetMatches[0].stream;
        if (objectOffset < 0 || objectOffset + patchedObjectBytes.Length > stream.Length)
            throw new InvalidDataException("Object nằm ngoài SerializedFile sau repack.");

        byte[] check = new byte[patchedObjectBytes.Length];
        stream.Position = objectOffset;
        stream.ReadExactly(check);
        if (!check.AsSpan().SequenceEqual(patchedObjectBytes))
            throw new InvalidDataException("Dữ liệu TextAsset sau repack không khớp bản sửa đã yêu cầu.");
    }

    private static void CopyExactly(Stream input, Stream output, long count)
    {
        byte[] buffer = new byte[1024 * 1024];
        long remaining = count;
        while (remaining > 0)
        {
            int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read <= 0) throw new EndOfStreamException("Entry trong UnityFS bị ngắn khi copy.");
            output.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static void WriteNullTerminatedUtf8(BinaryWriter writer, string value)
    {
        writer.Write(Encoding.UTF8.GetBytes(value));
        writer.Write((byte)0);
    }

    private static void WriteUInt16BE(BinaryWriter writer, ushort value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        writer.Write(bytes);
    }

    private static void WriteUInt32BE(BinaryWriter writer, uint value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        writer.Write(bytes);
    }

    private static void WriteInt32BE(BinaryWriter writer, int value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        writer.Write(bytes);
    }

    private static void WriteInt64BE(BinaryWriter writer, long value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        writer.Write(bytes);
    }
}
