using System;
using System.IO;
using System.Linq;
using System.Text;
using AssetStudio;

namespace HoKTool.Android;

/// <summary>
/// Validates and applies a same-size-or-smaller UTF-8 TextAsset payload replacement.
/// Standalone SerializedFiles are copied in place; standard unencrypted UnityFS
/// containers are rebuilt by UnityFsTextAssetRepacker and verified after parsing.
/// </summary>
public static class SerializedTextAssetPatcher
{
    public static string CreatePatchedCopy(TextAsset asset, string replacementText, string outputDirectory)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));
        if (replacementText == null) replacementText = string.Empty;

        byte[] originalRaw = asset.GetRawData();
        if (originalRaw.Length == 0 || originalRaw.Length != asset.byteSize)
            throw new InvalidDataException("Kích thước object TextAsset không hợp lệ.");

        byte[] patchedRaw = BuildPatchedObjectBytes(asset, originalRaw, replacementText);
        Directory.CreateDirectory(outputDirectory);

        string sourcePath = Path.GetFullPath(asset.assetsFile.fullName);
        if (asset.reader.BaseStream is FileStream sourceStream)
        {
            string streamPath = Path.GetFullPath(sourceStream.Name);
            if (string.Equals(sourcePath, streamPath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(sourcePath))
            {
                return CreateStandaloneCopy(asset, sourcePath, sourceStream, patchedRaw, outputDirectory);
            }
        }

        string bundlePath = asset.assetsFile.originalPath;
        if (!string.IsNullOrWhiteSpace(bundlePath) && File.Exists(bundlePath))
        {
            return UnityFsTextAssetRepacker.CreatePatchedBundleCopy(
                asset, originalRaw, patchedRaw, outputDirectory);
        }

        throw new NotSupportedException(
            "Không xác định được SerializedFile độc lập hoặc UnityFS gốc; từ chối ghi để tránh sửa nhầm dữ liệu.");
    }

    private static byte[] BuildPatchedObjectBytes(TextAsset asset, byte[] raw, string replacementText)
    {
        // Recreate the object's original absolute alignment for ReadAlignedString().
        int alignmentPrefix = (int)(asset.reader.byteStart & 3);
        byte[] alignedRaw = new byte[checked(alignmentPrefix + raw.Length)];
        Buffer.BlockCopy(raw, 0, alignedRaw, alignmentPrefix, raw.Length);
        using var memory = new MemoryStream(alignedRaw, writable: false);
        using var reader = new EndianBinaryReader(memory, asset.reader.Endian);
        reader.Position = alignmentPrefix;

        // Unity's NoTarget layout includes hide flags and two EditorExtension PPtrs.
        if (asset.platform == BuildTarget.NoTarget)
        {
            reader.ReadUInt32();
            long pointerSize = asset.reader.m_Version < SerializedFileFormatVersion.Unknown_14 ? 8L : 12L;
            reader.Position = checked(reader.Position + pointerSize * 2);
        }

        string serializedName = reader.ReadAlignedString();
        if (!string.Equals(serializedName, asset.m_Name, StringComparison.Ordinal))
            throw new InvalidDataException("Tên TextAsset trong dữ liệu nhị phân không trùng với object đã phân tích.");

        long lengthOffset = reader.Position - alignmentPrefix;
        int oldLength = reader.ReadInt32();
        if (oldLength < 0 || oldLength > reader.Remaining)
            throw new InvalidDataException("Trường m_Script có độ dài không hợp lệ.");

        long dataOffset = reader.Position - alignmentPrefix;
        byte[] oldData = reader.ReadBytes(oldLength);
        if (oldData.Length != oldLength || !oldData.SequenceEqual(asset.m_Script ?? Array.Empty<byte>()))
            throw new InvalidDataException("Dữ liệu m_Script không khớp; từ chối sửa object chưa được xác thực.");

        byte[] newData = Encoding.UTF8.GetBytes(replacementText);
        if (newData.Length > oldLength)
            throw new InvalidOperationException(
                $"Nội dung mới dài {newData.Length} byte, vượt dung lượng hiện tại {oldLength} byte. " +
                "Chế độ an toàn chỉ nhận nội dung có số byte UTF-8 không vượt bản gốc để giữ nguyên mọi offset.");

        byte[] lengthBytes = BitConverter.GetBytes(newData.Length);
        if (asset.reader.Endian == EndianType.BigEndian)
            Array.Reverse(lengthBytes);
        Buffer.BlockCopy(lengthBytes, 0, raw, checked((int)lengthOffset), 4);

        // Replace only the original m_Script payload. Preserve any trailing
        // object bytes or game-specific extension data exactly as found.
        int dataStart = checked((int)dataOffset);
        if (oldLength > raw.Length - dataStart)
            throw new InvalidDataException("m_Script vượt quá ranh giới object.");
        Array.Clear(raw, dataStart, oldLength);
        if (newData.Length > 0)
            Buffer.BlockCopy(newData, 0, raw, dataStart, newData.Length);

        return raw;
    }

    private static string CreateStandaloneCopy(
        TextAsset asset,
        string sourcePath,
        FileStream sourceStream,
        byte[] patchedRaw,
        string outputDirectory)
    {
        string outputPath = Path.Combine(
            outputDirectory,
            Path.GetFileNameWithoutExtension(sourcePath) + ".edited." +
            DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".assets");

        File.Copy(sourcePath, outputPath, overwrite: false);
        try
        {
            using (var output = new FileStream(outputPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                long objectStart = asset.reader.byteStart;
                if (objectStart < 0 || objectStart + patchedRaw.Length > output.Length)
                    throw new InvalidDataException("Vị trí object nằm ngoài file SerializedFile.");
                output.Position = objectStart;
                output.Write(patchedRaw, 0, patchedRaw.Length);
                output.Flush(flushToDisk: true);
            }

            using (var verify = new FileStream(outputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (verify.Length != sourceStream.Length)
                    throw new InvalidDataException("Độ dài file đầu ra thay đổi ngoài dự kiến.");
                verify.Position = asset.reader.byteStart;
                byte[] check = new byte[patchedRaw.Length];
                verify.ReadExactly(check);
                if (!check.SequenceEqual(patchedRaw))
                    throw new InvalidDataException("Kiểm tra sau ghi thất bại; bản đầu ra không được chấp nhận.");
            }

            return outputPath;
        }
        catch
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
            throw;
        }
    }
}
