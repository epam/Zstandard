package com.epam.deltix.zstd;

import org.junit.Test;

public class TestDecompress {
    @Test
    public void RunDecompression() {
        final byte[] compressedData = {40, -75, 47, -3, -92, -96, -122, 1, 0, 29, 1, 0, -40, 97, 98, 99, 100, 101, 102, 103,
                104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 97, 1,
                0, 11, 26, 118, 62, -57, -8, 51, -92, 90};

        final int decompressSize = (int) ZstdDecompressor.getDecompressedSize(compressedData, 0, compressedData.length);

        final byte[] decompressedData = new byte[decompressSize];

        final ZstdDecompressor decompressor = new ZstdDecompressor();
        decompressor.decompress(
                compressedData, 0, compressedData.length,
                decompressedData, 0, decompressedData.length);
    }
}
