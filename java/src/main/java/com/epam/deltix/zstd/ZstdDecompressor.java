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

import java.nio.ByteBuffer;

public class ZstdDecompressor {
    private final ZstdFrameDecompressor decompressor = new ZstdFrameDecompressor();

    public int decompress(final byte[] input, final int inputOffset, final int inputLength, final byte[] output, final int outputOffset, final int maxOutputLength) {
        return decompressor.decompress(
                ByteBuffer.wrap(input), inputOffset, inputOffset + inputLength,
                ByteBuffer.wrap(output), outputOffset, outputOffset + maxOutputLength);
    }

//    public void decompress(final ByteBuffer input, final ByteBuffer output)
//            throws RuntimeException {
//        final Object inputBase;
//        final long inputAddress;
//        final long inputLimit;
//        if (input.isDirect()) {
//            inputBase = null;
//            final long address = getAddress(input);
//            inputAddress = address + input.position();
//            inputLimit = address + input.limit();
//        } else if (input.hasArray()) {
//            inputBase = input.array();
//            inputAddress = input.arrayOffset() + input.position();
//            inputLimit = input.arrayOffset() + input.limit();
//        } else {
//            throw new IllegalArgumentException("Unsupported input ByteBuffer implementation " + input.getClass().getName());
//        }
//
//        final Object outputBase;
//        final long outputAddress;
//        final long outputLimit;
//        if (output.isDirect()) {
//            outputBase = null;
//            final long address = getAddress(output);
//            outputAddress = address + output.position();
//            outputLimit = address + output.limit();
//        } else if (output.hasArray()) {
//            outputBase = output.array();
//            outputAddress = output.arrayOffset() + output.position();
//            outputLimit = output.arrayOffset() + output.limit();
//        } else {
//            throw new IllegalArgumentException("Unsupported output ByteBuffer implementation " + output.getClass().getName());
//        }
//
//        // HACK: Assure JVM does not collect Slice wrappers while decompressing, since the
//        // collection may trigger freeing of the underlying memory resulting in a segfault
//        // There is no other known way to signal to the JVM that an object should not be
//        // collected in a block, and technically, the JVM is allowed to eliminate these locks.
//        synchronized (input) {
//            synchronized (output) {
//                final int written = new ZstdFrameDecompressor().decompress(inputBase, inputAddress, inputLimit, outputBase, outputAddress, outputLimit);
//                output.position(output.position() + written);
//            }
//        }
//    }

    public static long getDecompressedSize(final byte[] input, final int offset, final int length) {
        return ZstdFrameDecompressor.getDecompressedSize(ByteBuffer.wrap(input), offset, offset + length);
    }
}
