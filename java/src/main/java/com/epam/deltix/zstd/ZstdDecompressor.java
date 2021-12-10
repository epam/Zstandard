/*
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
package com.epam.deltix.zstd;

public class ZstdDecompressor {
    private final ZstdFrameDecompressor decompressor = new ZstdFrameDecompressor();


    public int decompress(final byte[] input, final int inputOffset, final int inputLength,
                          final byte[] output, final int outputOffset, final int maxOutputLength) {

        return decompressor.decompress(
                input, inputOffset, inputOffset + inputLength,
                output, outputOffset, outputOffset + maxOutputLength);
    }

    public static long getDecompressedSize(final byte[] input, final int offset, final int length) {
        return ZstdFrameDecompressor.getDecompressedSize(input, offset, offset + length);
    }
}
