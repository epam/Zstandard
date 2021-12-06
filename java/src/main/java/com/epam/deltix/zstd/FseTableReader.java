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

import static com.epam.deltix.zstd.Util.highestBit;
import static com.epam.deltix.zstd.Util.verify;

class FseTableReader {
    private static final int FSE_MIN_TABLE_LOG = 5;

    public static final int FSE_MAX_SYMBOL_VALUE = 255;
    private final short[] nextSymbol = new short[FSE_MAX_SYMBOL_VALUE + 1];
    private final short[] normalizedCounters = new short[FSE_MAX_SYMBOL_VALUE + 1];

    public int readFseTable(final FiniteStateEntropy.Table table, final ByteBuffer inputBase, final int inputAddress, final int inputLimit, int maxSymbol, final int maxTableLog) {
        // read table headers
        int input = inputAddress;
        verify(inputLimit - inputAddress >= 4, input, "Not enough input bytes");

        int threshold;
        int symbolNumber = 0;
        boolean previousIsZero = false;

        int bitStream = inputBase.getInt(input);

        final int tableLog = (bitStream & 0xF) + FSE_MIN_TABLE_LOG;

        int numberOfBits = tableLog + 1;
        bitStream >>>= 4;
        int bitCount = 4;

        verify(tableLog <= maxTableLog, input, "FSE table size exceeds maximum allowed size");

        int remaining = (1 << tableLog) + 1;
        threshold = 1 << tableLog;

        while (remaining > 1 && symbolNumber <= maxSymbol) {
            if (previousIsZero) {
                int n0 = symbolNumber;
                while ((bitStream & 0xFFFF) == 0xFFFF) {
                    n0 += 24;
                    if (input < inputLimit - 5) {
                        input += 2;
                        bitStream = (inputBase.getInt(input) >>> bitCount);
                    } else {
                        // end of bit stream
                        bitStream >>>= 16;
                        bitCount += 16;
                    }
                }
                while ((bitStream & 3) == 3) {
                    n0 += 3;
                    bitStream >>>= 2;
                    bitCount += 2;
                }
                n0 += bitStream & 3;
                bitCount += 2;

                verify(n0 <= maxSymbol, input, "Symbol larger than max value");

                while (symbolNumber < n0) {
                    normalizedCounters[symbolNumber++] = 0;
                }
                if ((input <= inputLimit - 7) || (input + (bitCount >>> 3) <= inputLimit - 4)) {
                    input += bitCount >>> 3;
                    bitCount &= 7;
                    bitStream = inputBase.getInt(input) >>> bitCount;
                } else {
                    bitStream >>>= 2;
                }
            }

            final short max = (short) ((2 * threshold - 1) - remaining);
            short count;

            if ((bitStream & (threshold - 1)) < max) {
                count = (short) (bitStream & (threshold - 1));
                bitCount += numberOfBits - 1;
            } else {
                count = (short) (bitStream & (2 * threshold - 1));
                if (count >= threshold) {
                    count -= max;
                }
                bitCount += numberOfBits;
            }
            count--;  // extra accuracy

            remaining -= Math.abs(count);
            normalizedCounters[symbolNumber++] = count;
            previousIsZero = count == 0;
            while (remaining < threshold) {
                numberOfBits--;
                threshold >>>= 1;
            }

            if ((input <= inputLimit - 7) || (input + (bitCount >> 3) <= inputLimit - 4)) {
                input += bitCount >>> 3;
                bitCount &= 7;
            } else {
                bitCount -= (int) (8 * (inputLimit - 4 - input));
                input = inputLimit - 4;
            }
            bitStream = inputBase.getInt(input) >>> (bitCount & 31);
        }

        verify(remaining == 1 && bitCount <= 32, input, "Input is corrupted");

        maxSymbol = symbolNumber - 1;
        verify(maxSymbol <= FSE_MAX_SYMBOL_VALUE, input, "Max symbol value too large (too many symbols for FSE)");

        input += (bitCount + 7) >> 3;

        // populate decoding table
        final int symbolCount = maxSymbol + 1;
        final int tableSize = 1 << tableLog;
        int highThreshold = tableSize - 1;

        table.log2Size = tableLog;

        for (byte symbol = 0; symbol < symbolCount; symbol++) {
            if (normalizedCounters[symbol] == -1) {
                table.symbol[highThreshold--] = symbol;
                nextSymbol[symbol] = 1;
            } else {
                nextSymbol[symbol] = normalizedCounters[symbol];
            }
        }

        // spread symbols
        final int tableMask = tableSize - 1;
        final int step = (tableSize >>> 1) + (tableSize >>> 3) + 3;
        int position = 0;
        for (byte symbol = 0; symbol < symbolCount; symbol++) {
            for (int i = 0; i < normalizedCounters[symbol]; i++) {
                table.symbol[position] = symbol;
                do {
                    position = (position + step) & tableMask;
                }
                while (position > highThreshold);
            }
        }

        // position must reach all cells once, otherwise normalizedCounter is incorrect
        verify(position == 0, input, "Input is corrupted");

        for (int i = 0; i < tableSize; i++) {
            final byte symbol = table.symbol[i];
            final short nextState = nextSymbol[symbol]++;
            table.numberOfBits[i] = (byte) (tableLog - highestBit(nextState));
            table.newState[i] = (short) ((nextState << table.numberOfBits[i]) - tableSize);
        }

        return (int) (input - inputAddress);
    }

    public static void buildRleTable(final FiniteStateEntropy.Table table, final byte value) {
        table.log2Size = 0;
        table.symbol[0] = value;
        table.newState[0] = 0;
        table.numberOfBits[0] = 0;
    }
}
