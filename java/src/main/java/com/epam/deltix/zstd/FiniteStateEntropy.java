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

import static com.epam.deltix.zstd.BitStream.peekBits;
import static com.epam.deltix.zstd.FseTableReader.FSE_MAX_SYMBOL_VALUE;
import static com.epam.deltix.zstd.Util.verify;
import static com.epam.deltix.zstd.ZstdFrameDecompressor.SIZE_OF_INT;
import static sun.misc.Unsafe.ARRAY_BYTE_BASE_OFFSET;

class FiniteStateEntropy {
    private static final int MAX_TABLE_LOG = 12;

    private final FiniteStateEntropy.Table table;
    private final FseTableReader reader = new FseTableReader();

    public FiniteStateEntropy(final int maxLog) {
        table = new FiniteStateEntropy.Table(maxLog);
    }

    public int decompress(final Object inputBase, final long inputAddress, final long inputLimit, final byte[] weights) {
        long input = inputAddress;
        input += reader.readFseTable(table, inputBase, input, inputLimit, FSE_MAX_SYMBOL_VALUE, MAX_TABLE_LOG);

        final Object outputBase = weights;
        final long outputAddress = ARRAY_BYTE_BASE_OFFSET;
        final long outputLimit = outputAddress + weights.length;

        long output = outputAddress;

        // initialize bit stream
        final BitStream.Initializer initializer = new BitStream.Initializer(inputBase, input, inputLimit);
        initializer.initialize();
        int bitsConsumed = initializer.getBitsConsumed();
        long currentAddress = initializer.getCurrentAddress();
        long bits = initializer.getBits();

        // initialize first FSE stream
        int state1 = (int) peekBits(bitsConsumed, bits, table.log2Size);
        bitsConsumed += table.log2Size;

        BitStream.Loader loader = new BitStream.Loader(inputBase, input, currentAddress, bits, bitsConsumed);
        loader.load();
        bits = loader.getBits();
        bitsConsumed = loader.getBitsConsumed();
        currentAddress = loader.getCurrentAddress();

        // initialize second FSE stream
        int state2 = (int) peekBits(bitsConsumed, bits, table.log2Size);
        bitsConsumed += table.log2Size;

        loader = new BitStream.Loader(inputBase, input, currentAddress, bits, bitsConsumed);
        loader.load();
        bits = loader.getBits();
        bitsConsumed = loader.getBitsConsumed();
        currentAddress = loader.getCurrentAddress();

        final byte[] symbols = table.symbol;
        final byte[] numbersOfBits = table.numberOfBits;
        final int[] newStates = table.newState;

        // decode 4 symbols per loop
        while (output <= outputLimit - 4) {
            int numberOfBits;

            outputBase.put(output, symbols[state1]);
            numberOfBits = numbersOfBits[state1];
            state1 = (int) (newStates[state1] + peekBits(bitsConsumed, bits, numberOfBits));
            bitsConsumed += numberOfBits;

            outputBase.put(output + 1, symbols[state2]);
            numberOfBits = numbersOfBits[state2];
            state2 = (int) (newStates[state2] + peekBits(bitsConsumed, bits, numberOfBits));
            bitsConsumed += numberOfBits;

            outputBase.put(output + 2, symbols[state1]);
            numberOfBits = numbersOfBits[state1];
            state1 = (int) (newStates[state1] + peekBits(bitsConsumed, bits, numberOfBits));
            bitsConsumed += numberOfBits;

            outputBase.put(output + 3, symbols[state2]);
            numberOfBits = numbersOfBits[state2];
            state2 = (int) (newStates[state2] + peekBits(bitsConsumed, bits, numberOfBits));
            bitsConsumed += numberOfBits;

            output += SIZE_OF_INT;

            loader = new BitStream.Loader(inputBase, input, currentAddress, bits, bitsConsumed);
            final boolean done = loader.load();
            bitsConsumed = loader.getBitsConsumed();
            bits = loader.getBits();
            currentAddress = loader.getCurrentAddress();
            if (done) {
                break;
            }
        }

        while (true) {
            verify(output <= outputLimit - 2, input, "Output buffer is too small");
            outputBase.put(output++, symbols[state1]);
            final int numberOfBits = numbersOfBits[state1];
            state1 = (int) (newStates[state1] + peekBits(bitsConsumed, bits, numberOfBits));
            bitsConsumed += numberOfBits;

            loader = new BitStream.Loader(inputBase, input, currentAddress, bits, bitsConsumed);
            loader.load();
            bitsConsumed = loader.getBitsConsumed();
            bits = loader.getBits();
            currentAddress = loader.getCurrentAddress();

            if (loader.isOverflow()) {
                outputBase.put(output++, symbols[state2]);
                break;
            }

            verify(output <= outputLimit - 2, input, "Output buffer is too small");
            outputBase.put(output++, symbols[state2]);
            final int numberOfBits1 = numbersOfBits[state2];
            state2 = (int) (newStates[state2] + peekBits(bitsConsumed, bits, numberOfBits1));
            bitsConsumed += numberOfBits1;

            loader = new BitStream.Loader(inputBase, input, currentAddress, bits, bitsConsumed);
            loader.load();
            bitsConsumed = loader.getBitsConsumed();
            bits = loader.getBits();
            currentAddress = loader.getCurrentAddress();

            if (loader.isOverflow()) {
                outputBase.put(output++, symbols[state1]);
                break;
            }
        }

        return (int) (output - outputAddress);
    }

    public static final class Table {
        int log2Size;
        final int[] newState;
        final byte[] symbol;
        final byte[] numberOfBits;

        public Table(final int log2Size) {
            final int size = 1 << log2Size;
            newState = new int[size];
            symbol = new byte[size];
            numberOfBits = new byte[size];
        }

        public Table(final int log2Size, final int[] newState, final byte[] symbol, final byte[] numberOfBits) {
            final int size = 1 << log2Size;
            if (newState.length != size || symbol.length != size || numberOfBits.length != size) {
                throw new IllegalArgumentException("Expected arrays to match provided size");
            }

            this.log2Size = log2Size;
            this.newState = newState;
            this.symbol = symbol;
            this.numberOfBits = numberOfBits;
        }
    }
}
