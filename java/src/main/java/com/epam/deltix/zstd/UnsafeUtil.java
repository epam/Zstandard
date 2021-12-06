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

import sun.misc.Unsafe;

import java.lang.reflect.Field;
import java.nio.Buffer;
import java.nio.ByteOrder;

final class UnsafeUtil {
    public static final Unsafe UNSAFE;
    private static final Field ADDRESS_ACCESSOR;

    private UnsafeUtil() {
    }

    static {
        final ByteOrder order = ByteOrder.nativeOrder();
        if (!order.equals(ByteOrder.LITTLE_ENDIAN)) {
            throw new RuntimeException(String.format("Zstandard requires a little endian platform (found %s)", order));
        }
        try {
            final Field theUnsafe = Unsafe.class.getDeclaredField("theUnsafe");
            theUnsafe.setAccessible(true);
            UNSAFE = (Unsafe) theUnsafe.get(null);
        } catch (final Exception e) {
            throw new RuntimeException(e);
        }

        try {
            final Field field = Buffer.class.getDeclaredField("address");
            field.setAccessible(true);
            ADDRESS_ACCESSOR = field;
        } catch (final Exception e) {
            throw new RuntimeException(e);
        }
    }

    public static long getAddress(final Buffer buffer) {
        try {
            return (long) ADDRESS_ACCESSOR.get(buffer);
        } catch (final IllegalAccessException e) {
            throw new RuntimeException(e);
        }
    }
}
