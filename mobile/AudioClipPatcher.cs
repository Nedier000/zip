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
        byte[] original = audio.m_AudioData?.GetData() ?? Array.Empty<byte>();
        if (original.Length == 0 || original.LongLength != audio.m_Size)
            throw new InvalidDataException("Kích thước AudioClip payload không hợp lệ.");
        byte[] replacement = File.ReadAllBytes(replacementPath);
        if (replacement.Length != original.Length)
            throw new InvalidDataException($"Audio thay thế phải cùng kích thước: cần {original.Length} byte, nhận {replacement.Length} byte.");
        string oldFormat = DetectContainer(original);
        string newFormat = DetectContainer(replacement);
        if (oldFormat == "unknown" || newFormat != oldFormat)
            throw new InvalidDataException($"Định dạng audio không khớp: gốc={oldFormat}, thay thế={newFormat}.");
        if (!string.IsNullOrEmpty(audio.m_Source))
            return CreateExternalResourceCopy(audio, original, replacement, outputDirectory);

        byte[] raw = audio.GetRawData();
        if (raw.Length != audio.byteSize)
            throw new InvalidDataException("Raw AudioClip object không khớp byteSize.");
        byte[] patched = SerializedObjectPatcher.ReplaceFinalPayload(raw, original, replacement);
        return SerializedObjectPatcher.CreatePatchedCopy(audio, raw, patched, outputDirectory);
    }

    private static string CreateExternalResourceCopy(
        AudioClip audio, byte[] original, byte[] replacement, string outputDirectory)
    {
        string serializedPath = audio.assetsFile.fullName;
        if (string.IsNullOrWhiteSpace(serializedPath) || !File.Exists(serializedPath))
            throw new NotSupportedException("External AudioClip trong UnityFS/virtual bundle chưa được ghi trực tiếp; chỉ hỗ trợ SerializedFile độc lập có .resS bên ngoài.");

        string directory = Path.GetDirectoryName(Path.GetFullPath(serializedPath))!;
        string resourceName = Path.GetFileName(audio.m_Source.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(resourceName))
            throw new InvalidDataException("AudioClip external resource không có tên file hợp lệ.");
        var matches = Directory.GetFiles(directory, resourceName, SearchOption.AllDirectories);
        if (matches.Length != 1)
            throw new NotSupportedException($"Cần tìm thấy đúng một resource '{resourceName}' cạnh SerializedFile; đã tìm thấy {matches.Length}.");

        string resourcePath = matches[0];
        long resourceLength = new FileInfo(resourcePath).Length;
        if (audio.m_Offset < 0 || audio.m_Size != original.LongLength ||
            audio.m_Offset > resourceLength - original.LongLength)
            throw new InvalidDataException("AudioClip external offset/size nằm ngoài resource file.");

        using (var verifySource = new FileStream(resourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            verifySource.Position = audio.m_Offset;
            byte[] check = new byte[original.Length];
            verifySource.ReadExactly(check);
            if (!check.AsSpan().SequenceEqual(original))
                throw new InvalidDataException("External resource bytes không khớp AudioClip đã phân tích; từ chối patch.");
        }

        Directory.CreateDirectory(outputDirectory);
        string copiedAssets = Path.Combine(outputDirectory, Path.GetFileName(serializedPath));
        string copiedResource = Path.Combine(outputDirectory, resourceName);
        if (string.Equals(Path.GetFullPath(copiedAssets), Path.GetFullPath(copiedResource), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Tên SerializedFile và resource file bị trùng.");
        File.Copy(serializedPath, copiedAssets, overwrite: false);
        try
        {
            File.Copy(resourcePath, copiedResource, overwrite: false);
            using (var output = new FileStream(copiedResource, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                output.Position = audio.m_Offset;
                output.Write(replacement, 0, replacement.Length);
                output.Flush(flushToDisk: true);
            }
            using (var verify = new FileStream(copiedResource, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                verify.Position = audio.m_Offset;
                byte[] check = new byte[replacement.Length];
                verify.ReadExactly(check);
                if (!check.AsSpan().SequenceEqual(replacement))
                    throw new InvalidDataException("Kiểm tra external .resS sau ghi thất bại.");
            }
            return copiedResource;
        }
        catch
        {
            if (File.Exists(copiedResource)) File.Delete(copiedResource);
            if (File.Exists(copiedAssets)) File.Delete(copiedAssets);
            throw;
        }
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
