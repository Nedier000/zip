using System;
using System.IO;
using System.Linq;
using System.Text;
using AssetStudio;

namespace HoKTool.Android;

/// <summary>
/// Conservative TextAsset editor for standalone Unity SerializedFiles.
/// It keeps every object offset and the total file length unchanged.
/// Bundle-backed objects are rejected until a full UnityFS writer is available.
/// </summary>
public static class SerializedTextAssetPatcher
{
    public static string CreatePatchedCopy(TextAsset asset, string replacementText, string outputDirectory)
    {
        if (asset == null) throw new ArgumentNullException(nameof(asset));
        if (replacementText == null) replacementText = string.Empty;

        string sourcePath = Path.GetFullPath(asset.assetsFile.fullName);
        if (!File.Exists(sourcePath))
            throw new NotSupportedException("Asset này được nạp từ bên trong AssetBundle. Chưa thể ghi ngược container UnityFS an toàn.");

        if (asset.reader.BaseStream is not FileStream sourceStream)
            throw new NotSupportedException("Chỉ sửa được TextAsset thuộc SerializedFile độc lập; dữ liệu đang nằm trong stream của AssetBundle.");

        string streamPath = Path.GetFullPath(sourceStream.Name);
        if (!string.Equals(sourcePath, streamPath, StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Nguồn object không khớp file SerializedFile gốc; từ chối ghi để tránh sửa nhầm file.");

        byte[] raw = asset.GetRawData();
        if (raw.Length == 0 || raw.Length != asset.byteSize)
            throw new InvalidDataException("Kích thước object TextAsset không hợp lệ.");

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
                "Bản này chỉ hỗ trợ thay nội dung ngắn hơn hoặc bằng bản gốc để giữ nguyên mọi offset.");

        byte[] lengthBytes = BitConverter.GetBytes(newData.Length);
        if (asset.reader.Endian == EndianType.BigEndian)
            Array.Reverse(lengthBytes);
        Buffer.BlockCopy(lengthBytes, 0, raw, checked((int)lengthOffset), 4);

        // m_Script is the final TextAsset field. Zero-fill its old tail so no
        // stale bytes remain while the serialized object size stays identical.
        int dataStart = checked((int)dataOffset);
        Array.Clear(raw, dataStart, raw.Length - dataStart);
        if (newData.Length > 0)
            Buffer.BlockCopy(newData, 0, raw, dataStart, newData.Length);

        Directory.CreateDirectory(outputDirectory);
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
                if (objectStart < 0 || objectStart + raw.Length > output.Length)
                    throw new InvalidDataException("Vị trí object nằm ngoài file SerializedFile.");
                output.Position = objectStart;
                output.Write(raw, 0, raw.Length);
                output.Flush(flushToDisk: true);
            }

            // Verify that the output contains the exact replacement at the
            // original object offset and retains the source file's total size.
            using (var verify = new FileStream(outputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (verify.Length != sourceStream.Length)
                    throw new InvalidDataException("Độ dài file đầu ra thay đổi ngoài dự kiến.");
                verify.Position = asset.reader.byteStart;
                byte[] check = new byte[raw.Length];
                verify.ReadExactly(check);
                if (!check.SequenceEqual(raw))
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
