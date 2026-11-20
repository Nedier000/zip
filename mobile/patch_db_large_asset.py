from pathlib import Path

path = Path("mobile/DbIndexExtractor.cs")
if not path.is_file():
    raise SystemExit("mobile/DbIndexExtractor.cs not found after restoring Android source payload")

source = path.read_text(encoding="utf-8")

old_index_guard = '''                    if (fullSize64 > int.MaxValue * 4L)
                        throw new InvalidDataException($"Asset quá lớn ({fullSize64} bytes) tại 0x{entryStart:X}");'''
new_index_guard = '''                    // FULLSIZE is a 64-bit field in the HoK index. Do not impose an
                    // arbitrary 8-GiB asset cap: extraction writes decoded chunks to disk.
                    // FileStream uses signed 64-bit offsets, so reject only values it cannot represent.
                    if (fullSize64 > (ulong)long.MaxValue)
                        throw new InvalidDataException($"FULLSIZE vượt giới hạn file 64-bit tại 0x{entryStart:X}: {fullSize64} bytes");'''

old_extract_guard = '''        if (fullSize > int.MaxValue * 4L) throw new InvalidDataException("Asset vượt giới hạn giải nén an toàn.");'''
new_extract_guard = '''        // Assets are streamed chunk-by-chunk to a file; total size must not be
        // constrained by Int32 or an arbitrary RAM-based cap.
        if (fullSize > (ulong)long.MaxValue)
            throw new InvalidDataException($"FULLSIZE vượt giới hạn file 64-bit: {fullSize} bytes");'''

if old_index_guard in source:
    source = source.replace(old_index_guard, new_index_guard, 1)
elif new_index_guard not in source:
    raise SystemExit("Could not uniquely locate the 8-GiB index FULLSIZE guard")

if old_extract_guard in source:
    source = source.replace(old_extract_guard, new_extract_guard, 1)
elif new_extract_guard not in source:
    raise SystemExit("Could not uniquely locate the 8-GiB ExtractOneAsync FULLSIZE guard")

old_decompress = '''    private static byte[] Decompress(int codec, byte[] src, int outputSize)
    {
        if (outputSize <= 0) throw new InvalidDataException("Uncompressed chunk size is invalid.");
        if (codec == 0 || src.Length == outputSize) return src.Length == outputSize ? src : throw new InvalidDataException("Unknown compression marker 0.");
        var dst = new byte[outputSize];
        switch (codec)
        {
            case 1:
                int n = LZ4Codec.Decode(src, 0, src.Length, dst, 0, dst.Length);
                if (n != dst.Length) throw new InvalidDataException($"LZ4 output size mismatch: {n}/{dst.Length}");
                return dst;
            case 4:
                int o = OozHelper.Decompress(src, dst);
                if (o != dst.Length) throw new InvalidDataException($"Oodle output size mismatch: {o}/{dst.Length}");
                return dst;
            case 9:
                using (var dec = new Decompressor())
                {
                    byte[] result = dec.Unwrap(src).ToArray();
                    if (result.Length != dst.Length) throw new InvalidDataException($"Zstd output size mismatch: {result.Length}/{dst.Length}");
                    return result;
                }
            default:
                throw new NotSupportedException($"Compression type {codec} chưa được hỗ trợ.");
        }
    }'''

new_decompress = '''    private static byte[] Decompress(int codec, byte[] src, int outputSize)
    {
        if (outputSize <= 0) throw new InvalidDataException("Uncompressed chunk size is invalid.");
        if (codec == 0 || src.Length == outputSize)
            return src.Length == outputSize ? src : throw new InvalidDataException("Unknown compression marker 0.");

        switch (codec)
        {
            case 1:
            {
                var dst = new byte[outputSize];
                int n = LZ4Codec.Decode(src, 0, src.Length, dst, 0, dst.Length);
                if (n != dst.Length) throw new InvalidDataException($"LZ4 output size mismatch: {n}/{dst.Length}");
                return dst;
            }
            case 4:
            {
                var dst = new byte[outputSize];
                int o = OozHelper.Decompress(src, dst);
                if (o != dst.Length) throw new InvalidDataException($"Oodle output size mismatch: {o}/{dst.Length}");
                return dst;
            }
            case 9:
                // ZstdSharp already allocates the decoded result. Avoid allocating
                // a second outputSize-sized buffer just to compare its length.
                using (var dec = new Decompressor())
                {
                    byte[] result = dec.Unwrap(src).ToArray();
                    if (result.Length != outputSize)
                        throw new InvalidDataException($"Zstd output size mismatch: {result.Length}/{outputSize}");
                    return result;
                }
            default:
                throw new NotSupportedException($"Compression type {codec} chưa được hỗ trợ.");
        }
    }'''

if old_decompress in source:
    source = source.replace(old_decompress, new_decompress, 1)
elif new_decompress not in source:
    raise SystemExit("Could not locate the exact DB chunk decompressor implementation")


# Detect modern QTS VFS before the legacy fixed-offset index reader. Both
# formats start with UInt32 0x02000001, but QTS has an additional 4-byte marker.
old_root = """        CurrentInputRoot.Value = Path.GetFullPath(inputDirectory);
        try { return await ExtractAsync(inputDirectory, outputDirectory, progress, cancellationToken); }
        finally { CurrentInputRoot.Value = null; }"""
new_root = """        CurrentInputRoot.Value = Path.GetFullPath(inputDirectory);
        try
        {
            if (QtsVfsDbExtractor.ContainsQtsVfsFile(inputDirectory))
                return await QtsVfsDbExtractor.ExtractAsync(inputDirectory, outputDirectory, progress, cancellationToken);
            return await ExtractAsync(inputDirectory, outputDirectory, progress, cancellationToken);
        }
        finally { CurrentInputRoot.Value = null; }"""
if old_root in source:
    source = source.replace(old_root, new_root, 1)
elif new_root not in source:
    raise SystemExit("Could not uniquely locate ExtractFromRootAsync dispatch point")

old_index_probe = """    private static bool IsIndexFile(string path)
    {
        try { using var fs = File.OpenRead(path); using var br = new BinaryReader(fs); return fs.Length >= 4 && br.ReadUInt32() == 0x02000001; }
        catch { return false; }
    }"""
new_index_probe = """    private static bool IsIndexFile(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            if (fs.Length < 4) return false;
            if (fs.Length >= 8)
            {
                Span<byte> signature = stackalloc byte[8];
                fs.ReadExactly(signature);
                if (signature.SequenceEqual(new byte[] { 0x01, 0x00, 0x00, 0x02, 0x01, 0x02, 0x03, 0x04 }))
                    return false;
                fs.Position = 0;
            }
            using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);
            return br.ReadUInt32() == 0x02000001;
        }
        catch { return false; }
    }"""
if old_index_probe in source:
    source = source.replace(old_index_probe, new_index_probe, 1)
elif new_index_probe not in source:
    raise SystemExit("Could not uniquely locate IsIndexFile probe")

if "int.MaxValue * 4L" in source or "Asset quá lớn" in source:
    raise SystemExit("Large-asset Int32/8-GiB guard remains after patch")
if "fullSize64 > (ulong)long.MaxValue" not in source or "fullSize > (ulong)long.MaxValue" not in source:
    raise SystemExit("64-bit FULLSIZE validation was not applied")
if "compressedSize > 256 * 1024 * 1024" not in source:
    raise SystemExit("Per-chunk allocation safety cap is missing")


# Modern QTS VFS signature shares the first UInt32 with the legacy index magic.
# Dispatch QTS files before the legacy reader uses BaseOffset=0x1A630.
old_root = '''        CurrentInputRoot.Value = Path.GetFullPath(inputDirectory);
        try { return await ExtractAsync(inputDirectory, outputDirectory, progress, cancellationToken); }
        finally { CurrentInputRoot.Value = null; }'''
new_root = '''        CurrentInputRoot.Value = Path.GetFullPath(inputDirectory);
        try
        {
            if (QtsVfsDbExtractor.ContainsQtsVfsFile(inputDirectory))
                return await QtsVfsDbExtractor.ExtractAsync(inputDirectory, outputDirectory, progress, cancellationToken);
            return await ExtractAsync(inputDirectory, outputDirectory, progress, cancellationToken);
        }
        finally { CurrentInputRoot.Value = null; }'''
if old_root in source:
    source = source.replace(old_root, new_root, 1)
elif new_root not in source:
    raise SystemExit("Could not uniquely locate ExtractFromRootAsync dispatch point")

old_index_probe = '''    private static bool IsIndexFile(string path)
    {
        try { using var fs = File.OpenRead(path); using var br = new BinaryReader(fs); return fs.Length >= 4 && br.ReadUInt32() == 0x02000001; }
        catch { return false; }
    }'''
new_index_probe = '''    private static bool IsIndexFile(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            if (fs.Length < 4) return false;
            if (fs.Length >= 8)
            {
                Span<byte> signature = stackalloc byte[8];
                fs.ReadExactly(signature);
                if (signature.SequenceEqual(new byte[] { 0x01, 0x00, 0x00, 0x02, 0x01, 0x02, 0x03, 0x04 }))
                    return false;
                fs.Position = 0;
            }
            using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);
            return br.ReadUInt32() == 0x02000001;
        }
        catch { return false; }
    }'''
if old_index_probe in source:
    source = source.replace(old_index_probe, new_index_probe, 1)
elif new_index_probe not in source:
    raise SystemExit("Could not uniquely locate IsIndexFile probe")


# Expose the legacy extractor's already-linked Oodle helper to the dedicated
# QTS reader without duplicating native loading or importing a second decoder.
oodle_bridge = """    public static int DecompressOodleChunk(byte[] source, byte[] destination)
    {
        return OozHelper.Decompress(source, destination);
    }

"""
if "public static int DecompressOodleChunk(" not in source:
    method_anchor = "    private static string FormatBytes(long bytes)"
    if source.count(method_anchor) != 1:
        raise SystemExit("Could not uniquely locate DbIndexExtractor method insertion point for Oodle bridge")
    source = source.replace(method_anchor, oodle_bridge + method_anchor, 1)

path.write_text(source, encoding="utf-8")
print("Removed arbitrary 8-GiB total-asset cap; retained 64-bit file-offset validation.")
print("Reduced Zstd peak memory by eliminating the unused duplicate output buffer.")
