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

import java.io.IOException;
import java.io.InputStream;
import java.nio.ByteBuffer;

import static com.epam.deltix.zstd.Preconditions.checkPositionIndexes;
import static com.epam.deltix.zstd.ZstdFrameDecompressor.ByteBufferWrap;
import static java.lang.Long.rotateLeft;
import static java.lang.Math.min;

public final class XxHash64 {
    private static final long PRIME64_1 = 0x9E3779B185EBCA87L;
    private static final long PRIME64_2 = 0xC2B2AE3D27D4EB4FL;
    private static final long PRIME64_3 = 0x165667B19E3779F9L;
    private static final long PRIME64_4 = 0x85EBCA77C2b2AE63L;
    private static final long PRIME64_5 = 0x27D4EB2F165667C5L;

    private static final long DEFAULT_SEED = 0;

    private static final byte SIZE_OF_LONG = 8;

    private final long seed;

    private static final int BUFFER_ADDRESS = 0;
    private final ByteBuffer buffer = ByteBufferWrap(new byte[32]);
    private int bufferSize;

    private long bodyLength;

    private long v1;
    private long v2;
    private long v3;
    private long v4;

    public XxHash64() {
        this(DEFAULT_SEED);
    }

    public XxHash64(final long seed) {
        this.seed = seed;
        this.v1 = seed + PRIME64_1 + PRIME64_2;
        this.v2 = seed + PRIME64_2;
        this.v3 = seed;
        this.v4 = seed - PRIME64_1;
    }

    public XxHash64 update(final byte[] data) {
        return update(data, 0, data.length);
    }

    public XxHash64 update(final byte[] data, final int offset, final int length) {
        checkPositionIndexes(offset, offset + length, data.length);
        updateHash(ByteBufferWrap(data), offset, length);
        return this;
    }

    public XxHash64 update(final ByteBuffer dataBase, final int dataAddress, final int dataSize) {
        return update(dataBase, dataAddress, dataSize, 0, dataSize);
    }

    public XxHash64 update(final ByteBuffer dataBase, final int dataAddress, final int dataSize, final int offset, final int length) {
        checkPositionIndexes(0, offset + length, dataSize);
        updateHash(dataBase, dataAddress + offset, length);
        return this;
    }

    public long hash() {
        long hash;
        if (bodyLength > 0) {
            hash = computeBody();
        } else {
            hash = seed + PRIME64_5;
        }

        hash += bodyLength + bufferSize;

        return updateTail(hash, buffer, BUFFER_ADDRESS, 0, bufferSize);
    }

    private long computeBody() {
        long hash = rotateLeft(v1, 1) + rotateLeft(v2, 7) + rotateLeft(v3, 12) + rotateLeft(v4, 18);

        hash = update(hash, v1);
        hash = update(hash, v2);
        hash = update(hash, v3);
        hash = update(hash, v4);

        return hash;
    }

    private void updateHash(final ByteBuffer base, int address, int length) {
        if (bufferSize > 0) {
            final int available = min(32 - bufferSize, length);

            System.arraycopy(base.array(), address, buffer, BUFFER_ADDRESS + bufferSize, available);

            bufferSize += available;
            address += available;
            length -= available;

            if (bufferSize == 32) {
                updateBody(buffer, BUFFER_ADDRESS, bufferSize);
                bufferSize = 0;
            }
        }

        if (length >= 32) {
            final int index = updateBody(base, address, length);
            address += index;
            length -= index;
        }

        if (length > 0) {
            System.arraycopy(base.array(), address, buffer.array(), BUFFER_ADDRESS, length);
            bufferSize = length;
        }
    }

    private int updateBody(final ByteBuffer base, int address, final int length) {
        int remaining = length;
        while (remaining >= 32) {
            v1 = mix(v1, base.getLong(address));
            v2 = mix(v2, base.getLong(address + 8));
            v3 = mix(v3, base.getLong(address + 16));
            v4 = mix(v4, base.getLong(address + 24));

            address += 32;
            remaining -= 32;
        }

        final int index = length - remaining;
        bodyLength += index;
        return index;
    }

    public static long hash(final long value) {
        long hash = DEFAULT_SEED + PRIME64_5 + SIZE_OF_LONG;
        hash = updateTail(hash, value);
        hash = finalShuffle(hash);

        return hash;
    }

    public static long hash(final InputStream in)
            throws IOException {
        return hash(DEFAULT_SEED, in);
    }

    public static long hash(final long seed, final InputStream in)
            throws IOException {
        final XxHash64 hash = new XxHash64(seed);
        final byte[] buffer = new byte[8192];
        while (true) {
            final int length = in.read(buffer);
            if (length == -1) {
                break;
            }
            hash.update(buffer, 0, length);
        }
        return hash.hash();
    }

    public static long hash(final ByteBuffer dataBase, final int dataAddress, final int dataSize) {
        return hash(dataBase, dataAddress, dataSize, 0, dataSize);
    }

    public static long hash(final long seed, final ByteBuffer dataBase, final int dataAddress, final int dataSize) {
        return hash(seed, dataBase, dataAddress, dataSize, 0, dataSize);
    }

    public static long hash(final ByteBuffer dataBase, final int dataAddress, final int dataSize, final int offset, final int length) {
        return hash(DEFAULT_SEED, dataBase, dataAddress, dataSize, offset, length);
    }

    public static long hash(final long seed, final ByteBuffer dataBase, final int dataAddress, final int dataSize, final int offset, final int length) {
        checkPositionIndexes(0, offset + length, dataSize);

        final ByteBuffer base = dataBase;
        final int address = dataAddress + offset;

        long hash;
        if (length >= 32) {
            hash = updateBody(seed, base, address, length);
        } else {
            hash = seed + PRIME64_5;
        }

        hash += length;

        // round to the closest 32 byte boundary
        // this is the point up to which updateBody() processed
        final int index = length & 0xFFFFFFE0;

        return updateTail(hash, base, address, index, length);
    }

    private static long updateTail(long hash, final ByteBuffer base, final int address, int index, final int length) {
        while (index <= length - 8) {
            hash = updateTail(hash, base.getLong(address + index));
            index += 8;
        }

        if (index <= length - 4) {
            hash = updateTail(hash, base.getInt(address + index));
            index += 4;
        }

        while (index < length) {
            hash = updateTail(hash, base.get(address + index));
            index++;
        }

        hash = finalShuffle(hash);

        return hash;
    }

    private static long updateBody(final long seed, final ByteBuffer base, int address, final int length) {
        long v1 = seed + PRIME64_1 + PRIME64_2;
        long v2 = seed + PRIME64_2;
        long v3 = seed;
        long v4 = seed - PRIME64_1;

        int remaining = length;
        while (remaining >= 32) {
            v1 = mix(v1, base.getLong(address));
            v2 = mix(v2, base.getLong(address + 8));
            v3 = mix(v3, base.getLong(address + 16));
            v4 = mix(v4, base.getLong(address + 24));

            address += 32;
            remaining -= 32;
        }

        long hash = rotateLeft(v1, 1) + rotateLeft(v2, 7) + rotateLeft(v3, 12) + rotateLeft(v4, 18);

        hash = update(hash, v1);
        hash = update(hash, v2);
        hash = update(hash, v3);
        hash = update(hash, v4);

        return hash;
    }

    private static long mix(final long current, final long value) {
        return rotateLeft(current + value * PRIME64_2, 31) * PRIME64_1;
    }

    private static long update(final long hash, final long value) {
        final long temp = hash ^ mix(0, value);
        return temp * PRIME64_1 + PRIME64_4;
    }

    private static long updateTail(final long hash, final long value) {
        final long temp = hash ^ mix(0, value);
        return rotateLeft(temp, 27) * PRIME64_1 + PRIME64_4;
    }

    private static long updateTail(final long hash, final int value) {
        final long unsigned = value & 0xFFFF_FFFFL;
        final long temp = hash ^ (unsigned * PRIME64_1);
        return rotateLeft(temp, 23) * PRIME64_2 + PRIME64_3;
    }

    private static long updateTail(final long hash, final byte value) {
        final int unsigned = value & 0xFF;
        final long temp = hash ^ (unsigned * PRIME64_5);
        return rotateLeft(temp, 11) * PRIME64_1;
    }

    private static long finalShuffle(long hash) {
        hash ^= hash >>> 33;
        hash *= PRIME64_2;
        hash ^= hash >>> 29;
        hash *= PRIME64_3;
        hash ^= hash >>> 32;
        return hash;
    }
}
