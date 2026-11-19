using System;
using System.IO;
using System.Text;
using AssetStudio;

namespace HoKTool.Android;

/// <summary>
/// Repoints an existing Material texture property to another Texture2D already
/// present in the same SerializedFile. No new external references are created.
/// </summary>
public static class MaterialTextureSlotPatcher
{
    public static string CreatePatchedCopy(
        Material material, string propertyName, Texture2D target, string outputDirectory)
    {
        if (material == null || target == null) throw new ArgumentNullException();
        if (string.IsNullOrWhiteSpace(propertyName)) throw new ArgumentException("Texture property is required.", nameof(propertyName));
        if (!ReferenceEquals(material.assetsFile, target.assetsFile) &&
            !string.Equals(material.assetsFile.fileName, target.assetsFile.fileName, StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Texture đích phải nằm trong cùng SerializedFile với Material.");

        var properties = material.m_SavedProperties.m_TexEnvs.FindAll(x => x.Key == propertyName);
        if (properties.Count != 1) throw new InvalidDataException($"Texture property '{propertyName}' không tồn tại hoặc bị trùng.");
        var oldPointer = properties[0].Value.m_Texture;
        if (oldPointer.m_FileID == 0 && oldPointer.m_PathID == target.m_PathID)
            throw new InvalidOperationException("Texture đích đã được gán cho property này.");

        bool pathId32 = material.reader.m_Version < SerializedFileFormatVersion.Unknown_14;
        byte[] oldPointerBytes = EncodePointer(oldPointer.m_FileID, oldPointer.m_PathID, pathId32, material.reader.Endian);
        byte[] newPointerBytes = EncodePointer(0, target.m_PathID, pathId32, material.reader.Endian);

        byte[] raw = material.GetRawData();
        if (raw.Length == 0 || raw.Length != material.byteSize)
            throw new InvalidDataException("Raw Material object không khớp byteSize.");

        byte[] nameBytes = Encoding.UTF8.GetBytes(propertyName);
        byte[] lengthBytes = BitConverter.GetBytes(nameBytes.Length);
        if (material.reader.Endian == EndianType.BigEndian) Array.Reverse(lengthBytes);

        int found = -1;
        for (int i = 0; i <= raw.Length - 4 - nameBytes.Length; i++)
        {
            if (!raw.AsSpan(i, 4).SequenceEqual(lengthBytes)) continue;
            if (!raw.AsSpan(i + 4, nameBytes.Length).SequenceEqual(nameBytes)) continue;

            long absoluteAfterName = material.reader.byteStart + i + 4L + nameBytes.Length;
            long alignedAbsolute = (absoluteAfterName + 3L) & ~3L;
            int pointerOffset = checked((int)(alignedAbsolute - material.reader.byteStart));
            if (pointerOffset < 0 || pointerOffset > raw.Length - oldPointerBytes.Length) continue;
            if (!raw.AsSpan(pointerOffset, oldPointerBytes.Length).SequenceEqual(oldPointerBytes)) continue;
            if (found >= 0) throw new InvalidDataException($"Texture property '{propertyName}' có nhiều vị trí PPtr khớp; từ chối patch mơ hồ.");
            found = pointerOffset;
        }
        if (found < 0)
            throw new InvalidDataException($"Không xác thực được PPtr của texture property '{propertyName}' trong Material.");

        byte[] patched = (byte[])raw.Clone();
        Buffer.BlockCopy(newPointerBytes, 0, patched, found, newPointerBytes.Length);
        return SerializedObjectPatcher.CreatePatchedCopy(material, raw, patched, outputDirectory);
    }

    private static byte[] EncodePointer(int fileId, long pathId, bool pathId32, EndianType endian)
    {
        byte[] file = BitConverter.GetBytes(fileId);
        byte[] path = pathId32 ? BitConverter.GetBytes(checked((int)pathId)) : BitConverter.GetBytes(pathId);
        if (endian == EndianType.BigEndian)
        {
            Array.Reverse(file);
            Array.Reverse(path);
        }
        byte[] result = new byte[file.Length + path.Length];
        Buffer.BlockCopy(file, 0, result, 0, file.Length);
        Buffer.BlockCopy(path, 0, result, file.Length, path.Length);
        return result;
    }
}
