using System;
using System.IO;
using Android.Graphics;
using AssetStudio;

namespace HoKTool.Android;

public static class Texture2DImporter
{
    public static string CreatePatchedCopy(Texture2D texture, string pngPath, string outputDirectory)
    {
        if (texture == null) throw new ArgumentNullException(nameof(texture));
        if (texture.m_StreamData != null && !string.IsNullOrEmpty(texture.m_StreamData.path))
            throw new NotSupportedException("Texture dùng file .resS ngoài bundle; chưa thay thế external stream.");
        if (texture.m_MipMap || texture.m_MipCount > 1)
            throw new NotSupportedException("Chỉ hỗ trợ texture không mipmap (MipCount ≤ 1).");
        int bpp = texture.m_TextureFormat switch
        {
            TextureFormat.RGBA32 or TextureFormat.BGRA32 => 4,
            TextureFormat.RGB24 => 3,
            TextureFormat.RGB565 or TextureFormat.ARGB4444 or TextureFormat.RGBA4444 => 2,
            TextureFormat.Alpha8 => 1,
            _ => throw new NotSupportedException("Import PNG chưa hỗ trợ định dạng nén GPU (ASTC/ETC/DXT/BC) hoặc định dạng khác.")
        };
        using Bitmap bitmap = BitmapFactory.DecodeFile(pngPath)
            ?? throw new InvalidDataException("Không đọc được PNG đầu vào.");
        if (bitmap.Width != texture.m_Width || bitmap.Height != texture.m_Height)
            throw new InvalidDataException($"Kích thước PNG {bitmap.Width}x{bitmap.Height} phải trùng texture {texture.m_Width}x{texture.m_Height}.");
        long pixelCount = (long)bitmap.Width * bitmap.Height;
        if (pixelCount <= 0 || pixelCount > 16_777_216)
            throw new InvalidDataException("Texture vượt giới hạn xử lý an toàn 16M pixel.");

        byte[] originalData = texture.image_data.GetData();
        int expectedLength = checked((int)(pixelCount * bpp));
        if (originalData.Length != expectedLength)
            throw new NotSupportedException($"Texture gốc có {originalData.Length} byte; cần đúng {expectedLength} byte để thay cùng định dạng.");
        int[] pixels = new int[checked((int)pixelCount)];
        bitmap.GetPixels(pixels, 0, bitmap.Width, 0, 0, bitmap.Width, bitmap.Height);
        byte[] replacement = new byte[expectedLength];
        for (int i = 0, p = 0; i < pixels.Length; i++)
        {
            uint c = unchecked((uint)pixels[i]);
            byte a = (byte)(c >> 24), r = (byte)(c >> 16), g = (byte)(c >> 8), b = (byte)c;
            if (texture.m_TextureFormat == TextureFormat.RGB24)
            {
                replacement[p++] = r; replacement[p++] = g; replacement[p++] = b;
            }
            else if (texture.m_TextureFormat == TextureFormat.BGRA32)
            {
                replacement[p++] = b; replacement[p++] = g; replacement[p++] = r; replacement[p++] = a;
            }
            else if (texture.m_TextureFormat == TextureFormat.RGB565)
            {
                ushort packed = (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));
                replacement[p++] = (byte)packed; replacement[p++] = (byte)(packed >> 8);
            }
            else if (texture.m_TextureFormat == TextureFormat.RGBA4444)
            {
                ushort packed = (ushort)(((r >> 4) << 12) | ((g >> 4) << 8) | ((b >> 4) << 4) | (a >> 4));
                replacement[p++] = (byte)packed; replacement[p++] = (byte)(packed >> 8);
            }
            else if (texture.m_TextureFormat == TextureFormat.ARGB4444)
            {
                ushort packed = (ushort)(((a >> 4) << 12) | ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4));
                replacement[p++] = (byte)packed; replacement[p++] = (byte)(packed >> 8);
            }
            else if (texture.m_TextureFormat == TextureFormat.Alpha8)
            {
                replacement[p++] = a;
            }
            else
            {
                replacement[p++] = r; replacement[p++] = g; replacement[p++] = b; replacement[p++] = a;
            }
        }

        byte[] raw = texture.GetRawData();
        if (raw.Length != texture.byteSize)
            throw new InvalidDataException("Raw Texture2D object không khớp byteSize.");
        byte[] patched = SerializedObjectPatcher.ReplaceInlineByteArray(
            raw, originalData, replacement, texture.reader.Endian, allowShorter: false);
        return SerializedObjectPatcher.CreatePatchedCopy(texture, raw, patched, outputDirectory);
    }
}
