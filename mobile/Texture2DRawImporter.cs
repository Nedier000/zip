using System;
using System.IO;
using AssetStudio;

namespace HoKTool.Android;

/// <summary>
/// Replaces the complete inline Texture2D image payload without decoding or
/// recompressing it. This supports ASTC/ETC/DXT and other formats when the user
/// supplies already-encoded bytes of exactly the original length.
/// </summary>
public static class Texture2DRawImporter
{
    public static string CreatePatchedCopy(Texture2D texture, string replacementPath, string outputDirectory)
    {
        if (texture == null) throw new ArgumentNullException(nameof(texture));
        if (texture.m_StreamData != null && !string.IsNullOrEmpty(texture.m_StreamData.path))
            throw new NotSupportedException("Texture dùng dữ liệu .resS ngoài bundle; chưa thay thế external stream.");

        byte[] original = texture.image_data?.GetData() ?? Array.Empty<byte>();
        if (original.Length == 0)
            throw new InvalidDataException("Texture2D không có image payload inline.");
        var info = new FileInfo(replacementPath);
        if (!info.Exists || info.Length != original.LongLength)
            throw new InvalidDataException($"Raw texture phải đúng {original.Length} byte; nhận {(info.Exists ? info.Length : 0)} byte.");

        byte[] replacement = File.ReadAllBytes(replacementPath);
        byte[] raw = texture.GetRawData();
        if (raw.Length != texture.byteSize)
            throw new InvalidDataException("Raw Texture2D object không khớp byteSize.");

        byte[] patched = SerializedObjectPatcher.ReplaceInlineByteArray(
            raw, original, replacement, texture.reader.Endian, allowShorter: false);
        return SerializedObjectPatcher.CreatePatchedCopy(texture, raw, patched, outputDirectory);
    }
}
