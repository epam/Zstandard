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

import static sun.misc.Unsafe.*;

public final class SizeOf {
    public static final byte SIZE_OF_BYTE = 1;
    public static final byte SIZE_OF_SHORT = 2;
    public static final byte SIZE_OF_INT = 4;
    public static final byte SIZE_OF_LONG = 8;
    public static final byte SIZE_OF_FLOAT = 4;
    public static final byte SIZE_OF_DOUBLE = 8;

    public static long sizeOf(final boolean[] array) {
        return (array == null) ? 0 : sizeOfBooleanArray(array.length);
    }

    public static long sizeOf(final byte[] array) {
        return (array == null) ? 0 : sizeOfByteArray(array.length);
    }

    public static long sizeOf(final short[] array) {
        return (array == null) ? 0 : sizeOfShortArray(array.length);
    }

    public static long sizeOf(final char[] array) {
        return (array == null) ? 0 : sizeOfCharArray(array.length);
    }

    public static long sizeOf(final int[] array) {
        return (array == null) ? 0 : sizeOfIntArray(array.length);
    }

    public static long sizeOf(final long[] array) {
        return (array == null) ? 0 : sizeOfLongArray(array.length);
    }

    public static long sizeOf(final float[] array) {
        return (array == null) ? 0 : sizeOfFloatArray(array.length);
    }

    public static long sizeOf(final double[] array) {
        return (array == null) ? 0 : sizeOfDoubleArray(array.length);
    }

    public static long sizeOf(final Object[] array) {
        return (array == null) ? 0 : sizeOfObjectArray(array.length);
    }

    public static long sizeOfBooleanArray(final int length) {
        return ARRAY_BOOLEAN_BASE_OFFSET + (((long) ARRAY_BOOLEAN_INDEX_SCALE) * length);
    }

    public static long sizeOfByteArray(final int length) {
        return ARRAY_BYTE_BASE_OFFSET + (((long) ARRAY_BYTE_INDEX_SCALE) * length);
    }

    public static long sizeOfShortArray(final int length) {
        return ARRAY_SHORT_BASE_OFFSET + (((long) ARRAY_SHORT_INDEX_SCALE) * length);
    }

    public static long sizeOfCharArray(final int length) {
        return ARRAY_CHAR_BASE_OFFSET + (((long) ARRAY_CHAR_INDEX_SCALE) * length);
    }

    public static long sizeOfIntArray(final int length) {
        return ARRAY_INT_BASE_OFFSET + (((long) ARRAY_INT_INDEX_SCALE) * length);
    }

    public static long sizeOfLongArray(final int length) {
        return ARRAY_LONG_BASE_OFFSET + (((long) ARRAY_LONG_INDEX_SCALE) * length);
    }

    public static long sizeOfFloatArray(final int length) {
        return ARRAY_FLOAT_BASE_OFFSET + (((long) ARRAY_FLOAT_INDEX_SCALE) * length);
    }

    public static long sizeOfDoubleArray(final int length) {
        return ARRAY_DOUBLE_BASE_OFFSET + (((long) ARRAY_DOUBLE_INDEX_SCALE) * length);
    }

    public static long sizeOfObjectArray(final int length) {
        return ARRAY_OBJECT_BASE_OFFSET + (((long) ARRAY_OBJECT_INDEX_SCALE) * length);
    }

    private SizeOf() {
    }
}
