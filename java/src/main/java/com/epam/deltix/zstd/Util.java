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

class Util {
    private Util() {
    }

    public static int highestBit(final int value) {
        return 31 - Integer.numberOfLeadingZeros(value);
    }

    public static boolean isPowerOf2(final int value) {
        return (value & (value - 1)) == 0;
    }

    public static int mask(final int bits) {
        return (1 << bits) - 1;
    }

    public static void verify(final boolean condition, final long offset, final String reason) {
        if (!condition) {
            throw new RuntimeException(reason + ": offset=" + offset);
        }
    }

    public static RuntimeException fail(final long offset, final String reason) {
        throw new RuntimeException(reason + ": offset=" + offset);
    }

    public static long getLong(final byte[] data, final int offset) {
        return (data[offset] & 0xffL)
                | ((data[offset + 1] & 0xffL) << 8)
                | ((data[offset + 2] & 0xffL) << 16)
                | ((data[offset + 3] & 0xffL) << 24)
                | ((data[offset + 4] & 0xffL) << 32)
                | ((data[offset + 5] & 0xffL) << 40)
                | ((data[offset + 6] & 0xffL) << 48)
                | ((data[offset + 7] & 0xffL) << 56);
    }

    public static int getInt(final byte[] data, final int offset) {
        return (data[offset] & 0xff)
                | ((data[offset + 1] & 0xff) << 8)
                | ((data[offset + 2] & 0xff) << 16)
                | ((data[offset + 3] & 0xff) << 24);
    }

    public static short getShort(final byte[] data, final int offset) {
        return (short) ((data[offset] & 0xff)
                | ((data[offset + 1] & 0xff) << 8));
    }

    public static byte getByte(final byte[] data, final int offset) {
        return data[offset];
    }

    public static void putLong(final byte[] data, final int offset, final long value) {
        data[offset] = (byte) (value & 0xffL);
        data[offset + 1] = (byte) ((value >>> 8) & 0xffL);
        data[offset + 2] = (byte) ((value >>> 16) & 0xffL);
        data[offset + 3] = (byte) ((value >>> 24) & 0xffL);
        data[offset + 4] = (byte) ((value >>> 32) & 0xffL);
        data[offset + 5] = (byte) ((value >>> 40) & 0xffL);
        data[offset + 6] = (byte) ((value >>> 48) & 0xffL);
        data[offset + 7] = (byte) ((value >>> 56) & 0xffL);
    }

    public static void putInt(final byte[] data, final int offset, final int value) {
        data[offset] = (byte) value;
        data[offset + 1] = (byte) (value >> 8);
        data[offset + 2] = (byte) (value >> 16);
        data[offset + 3] = (byte) (value >> 24);
    }

    public static void putShort(final byte[] data, final int offset, final short value) {
        data[offset] = (byte) value;
        data[offset + 1] = (byte) (value >> 8);
    }

    public static void putByte(final byte[] data, final int offset, final byte value) {
        data[offset] = value;
    }
}
