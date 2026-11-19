using System;
using System.IO;
using System.Linq;
using AssetStudio;

namespace HoKTool.Android;

public static class SerializedObjectPatcher
{
    public static string CreatePatchedCopy(
        AssetStudio.Object asset, byte[] originalRaw, byte[] patchedRaw, string outputDirectory)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));
        if (originalRaw == null || patchedRaw == null) throw new ArgumentNullException();
        if (originalRaw.Length == 0 || originalRaw.Length != patchedRaw.Length ||
            originalRaw.Length != asset.byteSize)
            throw new InvalidDataException("Object patch phải giữ nguyên kích thước SerializedFile.");

        Directory.CreateDirectory(outputDirectory);
        string sourcePath = Path.GetFullPath(asset.assetsFile.fullName);
        if (asset.reader.BaseStream is FileStream sourceStream)
        {
            string streamPath = Path.GetFullPath(sourceStream.Name);
            if (string.Equals(sourcePath, streamPath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(sourcePath))
                return CreateStandaloneCopy(asset, sourcePath, sourceStream, patchedRaw, outputDirectory);
        }

        string bundlePath = asset.assetsFile.originalPath;
        if (!string.IsNullOrWhiteSpace(bundlePath) && File.Exists(bundlePath))
            return UnityFsTextAssetRepacker.CreatePatchedBundleCopy(asset, originalRaw, patchedRaw, outputDirectory);

        throw new NotSupportedException("Không xác định được SerializedFile hoặc UnityFS gốc; từ chối ghi.");
    }

    public static byte[] ReplaceInlineByteArray(
        byte[] rawObject, byte[] originalData, byte[] replacementData, EndianType endian, bool allowShorter)
    {
        if (rawObject == null || originalData == null || replacementData == null)
            throw new ArgumentNullException();
        if (replacementData.Length > originalData.Length || (!allowShorter && replacementData.Length != originalData.Length))
            throw new InvalidOperationException($"Dữ liệu mới ({replacementData.Length} byte) vượt kích thước trường gốc ({originalData.Length} byte).");

        byte[] lengthBytes = BitConverter.GetBytes(originalData.Length);
        if (endian == EndianType.BigEndian) Array.Reverse(lengthBytes);

        int found = -1;
        for (int i = 0; i <= rawObject.Length - 4 - originalData.Length; i++)
        {
            if (rawObject[i] != lengthBytes[0] || rawObject[i + 1] != lengthBytes[1] ||
                rawObject[i + 2] != lengthBytes[2] || rawObject[i + 3] != lengthBytes[3])
                continue;
            if (!rawObject.AsSpan(i + 4, originalData.Length).SequenceEqual(originalData))
                continue;
            int trailing = rawObject.Length - (i + 4 + originalData.Length);
            if (trailing > 3) continue; // inline byte array must be the final serialized field
            if (found >= 0) throw new InvalidDataException("Trường byte array xuất hiện nhiều vị trí; từ chối patch mơ hồ.");
            found = i;
        }
        if (found < 0) throw new InvalidDataException("Không tìm thấy byte array inline có length-prefix khớp trong object.");
        byte[] patched = (byte[])rawObject.Clone();
        byte[] newLength = BitConverter.GetBytes(replacementData.Length);
        if (endian == EndianType.BigEndian) Array.Reverse(newLength);
        Buffer.BlockCopy(newLength, 0, patched, found, 4);
        int payloadOffset = found + 4;
        Array.Clear(patched, payloadOffset, originalData.Length);
        if (replacementData.Length > 0)
            Buffer.BlockCopy(replacementData, 0, patched, payloadOffset, replacementData.Length);
        return patched;
    }

    public static byte[] ReplaceFinalPayload(byte[] rawObject, byte[] originalData, byte[] replacementData)
    {
        if (rawObject == null || originalData == null || replacementData == null)
            throw new ArgumentNullException();
        if (originalData.Length == 0 || originalData.Length != replacementData.Length)
            throw new InvalidDataException("Payload replacement must preserve its exact size.");
        int found = -1;
        for (int i = 0; i <= rawObject.Length - originalData.Length; i++)
        {
            if (!rawObject.AsSpan(i, originalData.Length).SequenceEqual(originalData)) continue;
            int trailing = rawObject.Length - (i + originalData.Length);
            if (trailing > 3) continue;
            if (found >= 0) throw new InvalidDataException("Inline payload appears at multiple offsets; refusing ambiguous patch.");
            found = i;
        }
        if (found < 0) throw new InvalidDataException("Could not locate the verified final inline payload in the object.");
        byte[] patched = (byte[])rawObject.Clone();
        Buffer.BlockCopy(replacementData, 0, patched, found, replacementData.Length);
        return patched;
    }

    private static string CreateStandaloneCopy(
        AssetStudio.Object asset, string sourcePath, FileStream sourceStream, byte[] patchedRaw, string outputDirectory)
    {
        string outputPath = Path.Combine(outputDirectory,
            Path.GetFileNameWithoutExtension(sourcePath) + ".edited." +
            DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + Path.GetExtension(sourcePath));
        File.Copy(sourcePath, outputPath, overwrite: false);
        try
        {
            using (var output = new FileStream(outputPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                long start = asset.reader.byteStart;
                if (start < 0 || start + patchedRaw.Length > output.Length)
                    throw new InvalidDataException("Object nằm ngoài SerializedFile.");
                output.Position = start;
                output.Write(patchedRaw, 0, patchedRaw.Length);
                output.Flush(flushToDisk: true);
            }
            using (var verify = new FileStream(outputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (verify.Length != sourceStream.Length) throw new InvalidDataException("Kích thước SerializedFile thay đổi.");
                verify.Position = asset.reader.byteStart;
                byte[] check = new byte[patchedRaw.Length];
                verify.ReadExactly(check);
                if (!check.AsSpan().SequenceEqual(patchedRaw))
                    throw new InvalidDataException("Kiểm tra lại object sau ghi thất bại.");
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
