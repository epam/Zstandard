package com.epam.deltix.zstd;

import org.junit.Assert;
import org.junit.Test;

import java.util.Arrays;
import java.util.Random;

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

    @Test
    public void testPutGet() {
        final byte[] input = new byte[1000000];
        final byte[] output = new byte[input.length];

        new Random().nextBytes(input);

        Arrays.fill(output, (byte) -1);
        for (int i = 0; i < input.length; i += 1)
            Util.putByte(output, i, Util.getByte(input, i));
        Assert.assertArrayEquals(input, output);

        Arrays.fill(output, (byte) -1);
        for (int i = 0; i < input.length; i += 2)
            Util.putShort(output, i, Util.getShort(input, i));
        Assert.assertArrayEquals(input, output);

        Arrays.fill(output, (byte) -1);
        for (int i = 0; i < input.length; i += 4)
            Util.putInt(output, i, Util.getInt(input, i));
        Assert.assertArrayEquals(input, output);

        Arrays.fill(output, (byte) -1);
        for (int i = 0; i < input.length; i += 8)
            Util.putLong(output, i, Util.getLong(input, i));
        Assert.assertArrayEquals(input, output);
    }
}
