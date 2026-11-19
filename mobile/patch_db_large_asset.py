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

path.write_text(source, encoding="utf-8")
print("Removed arbitrary 8-GiB total-asset cap; retained 64-bit file-offset validation.")
print("Reduced Zstd peak memory by eliminating the unused duplicate output buffer.")
