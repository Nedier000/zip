using System;
using System.IO;
using System.Linq;
using System.Text;
using AssetStudio;

namespace HoKTool.Android;

public static class AudioClipPatcher
{
    public static string CreatePatchedCopy(AudioClip audio, string replacementPath, string outputDirectory)
    {
        if (audio == null) throw new ArgumentNullException(nameof(audio));
        if (!string.IsNullOrEmpty(audio.m_Source))
            throw new NotSupportedException("AudioClip dùng resource .resS ngoài SerializedFile; chưa thay thế external stream.");
        byte[] original = audio.m_AudioData?.GetData() ?? Array.Empty<byte>();
        if (original.Length == 0 || original.LongLength != audio.m_Size)
            throw new InvalidDataException("Kích thước AudioClip inline không hợp lệ.");
        byte[] replacement = File.ReadAllBytes(replacementPath);
        if (replacement.Length != original.Length)
            throw new InvalidDataException($"Audio thay thế phải cùng kích thước: cần {original.Length} byte, nhận {replacement.Length} byte.");
        string oldFormat = DetectContainer(original);
        string newFormat = DetectContainer(replacement);
        if (oldFormat == "unknown" || newFormat != oldFormat)
            throw new InvalidDataException($"Định dạng audio không khớp: gốc={oldFormat}, thay thế={newFormat}.");
        byte[] raw = audio.GetRawData();
        if (raw.Length != audio.byteSize)
            throw new InvalidDataException("Raw AudioClip object không khớp byteSize.");
        byte[] patched = SerializedObjectPatcher.ReplaceFinalPayload(raw, original, replacement);
        return SerializedObjectPatcher.CreatePatchedCopy(audio, raw, patched, outputDirectory);
    }

    private static string DetectContainer(byte[] data)
    {
        if (Starts(data, "OggS")) return "ogg";
        if (Starts(data, "FSB5")) return "fsb5";
        if (Starts(data, "RIFF")) return "riff";
        if (Starts(data, "ID3") || (data.Length >= 2 && data[0] == 0xFF && (data[1] & 0xE0) == 0xE0)) return "mp3";
        if (data.Length >= 8 && Encoding.ASCII.GetString(data, 4, 4) == "ftyp") return "mp4/aac";
        return "unknown";
    }

    private static bool Starts(byte[] data, string signature)
    {
        if (data.Length < signature.Length) return false;
        for (int i = 0; i < signature.Length; i++)
            if (data[i] != (byte)signature[i]) return false;
        return true;
    }
}
