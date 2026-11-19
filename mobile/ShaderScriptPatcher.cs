using System;
using System.IO;
using System.Text;
using AssetStudio;

namespace HoKTool.Android;

public static class ShaderScriptPatcher
{
    public static string CreatePatchedCopy(Shader shader, string replacementText, string outputDirectory)
    {
        if (shader == null) throw new ArgumentNullException(nameof(shader));
        byte[] original = shader.m_Script ?? Array.Empty<byte>();
        if (original.Length == 0)
            throw new NotSupportedException("Shader này chỉ có compiled blob/parsed form; không có m_Script dạng văn bản để sửa.");
        byte[] replacement = new UTF8Encoding(false, true).GetBytes(replacementText ?? string.Empty);
        if (replacement.Length > original.Length)
            throw new InvalidOperationException($"Shader mới dài {replacement.Length} byte, vượt trường m_Script {original.Length} byte.");
        byte[] raw = shader.GetRawData();
        if (raw.Length != shader.byteSize) throw new InvalidDataException("Raw Shader object không khớp byteSize.");
        byte[] patched = SerializedObjectPatcher.ReplaceInlineByteArray(
            raw, original, replacement, shader.reader.Endian, allowShorter: true);
        return SerializedObjectPatcher.CreatePatchedCopy(shader, raw, patched, outputDirectory);
    }
}
