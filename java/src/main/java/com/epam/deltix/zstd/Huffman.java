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
import java.util.Arrays;

import static com.epam.deltix.zstd.BitStream.isEndOfStream;
import static com.epam.deltix.zstd.BitStream.peekBitsFast;
import static com.epam.deltix.zstd.Util.isPowerOf2;
import static com.epam.deltix.zstd.Util.verify;
import static com.epam.deltix.zstd.ZstdFrameDecompressor.SIZE_OF_INT;
import static com.epam.deltix.zstd.ZstdFrameDecompressor.SIZE_OF_SHORT;

class Huffman {
    private static final int MAX_SYMBOL = 255;
    private static final int MAX_TABLE_LOG = 12;

    // stats
    private final byte[] weights = new byte[MAX_SYMBOL + 1];
    private final int[] ranks = new int[MAX_TABLE_LOG + 1];

    // table
    private int tableLog = -1;
    private final byte[] symbols = new byte[1 << MAX_TABLE_LOG];
    private final byte[] numbersOfBits = new byte[1 << MAX_TABLE_LOG];

    private final FiniteStateEntropy finiteStateEntropy = new FiniteStateEntropy(6);

    public boolean isLoaded() {
        return tableLog != -1;
    }

    public int readTable(final ByteBuffer inputBase, final int inputAddress, final int size) {
        Arrays.fill(ranks, 0);
        int input = inputAddress;

        // read table header
        verify(size > 0, input, "Not enough input bytes");
        int inputSize = inputBase.get(input++) & 0xFF;

        final int outputSize;
        if (inputSize >= 128) {
            outputSize = inputSize - 127;
            inputSize = ((outputSize + 1) / 2);

            verify(inputSize + 1 <= size, input, "Not enough input bytes");
            verify(outputSize <= MAX_SYMBOL + 1, input, "Input is corrupted");

            for (int i = 0; i < outputSize; i += 2) {
                final int value = inputBase.get(input + i / 2) & 0xFF;
                weights[i] = (byte) (value >>> 4);
                weights[i + 1] = (byte) (value & 0b1111);
            }
        } else {
            verify(inputSize + 1 <= size, input, "Not enough input bytes");

            outputSize = finiteStateEntropy.decompress(inputBase, input, input + inputSize, weights);
        }

        int totalWeight = 0;
        for (int i = 0; i < outputSize; i++) {
            ranks[weights[i]]++;
            totalWeight += (1 << weights[i]) >> 1;   // TODO same as 1 << (weights[n] - 1)?
        }
        verify(totalWeight != 0, input, "Input is corrupted");

        tableLog = Util.highestBit(totalWeight) + 1;
        verify(tableLog <= MAX_TABLE_LOG, input, "Input is corrupted");

        final int total = 1 << tableLog;
        final int rest = total - totalWeight;
        verify(isPowerOf2(rest), input, "Input is corrupted");

        final int lastWeight = Util.highestBit(rest) + 1;

        weights[outputSize] = (byte) lastWeight;
        ranks[lastWeight]++;

        final int numberOfSymbols = outputSize + 1;

        // populate table
        int nextRankStart = 0;
        for (int i = 1; i < tableLog + 1; ++i) {
            final int current = nextRankStart;
            nextRankStart += ranks[i] << (i - 1);
            ranks[i] = current;
        }

        for (int n = 0; n < numberOfSymbols; n++) {
            final int weight = weights[n];
            final int length = (1 << weight) >> 1;  // TODO: 1 << (weight - 1) ??

            final byte symbol = (byte) n;
            final byte numberOfBits = (byte) (tableLog + 1 - weight);
            for (int i = ranks[weight]; i < ranks[weight] + length; i++) {
                symbols[i] = symbol;
                numbersOfBits[i] = numberOfBits;
            }
            ranks[weight] += length;
        }

        verify(ranks[1] >= 2 && (ranks[1] & 1) == 0, input, "Input is corrupted");

        return inputSize + 1;
    }

    public void decodeSingleStream(final ByteBuffer inputBase, final int inputAddress, final int inputLimit,
                                   final ByteBuffer outputBase, final int outputAddress, final int outputLimit) {
        final BitStream.Initializer initializer = new BitStream.Initializer(inputBase, inputAddress, inputLimit);
        initializer.initialize();

        long bits = initializer.getBits();
        int bitsConsumed = initializer.getBitsConsumed();
        int currentAddress = initializer.getCurrentAddress();

        final int tableLog = this.tableLog;
        final byte[] numbersOfBits = this.numbersOfBits;
        final byte[] symbols = this.symbols;

        // 4 symbols at a time
        int output = outputAddress;
        final long fastOutputLimit = outputLimit - 4;
        while (output < fastOutputLimit) {
            final BitStream.Loader loader = new BitStream.Loader(inputBase, inputAddress, currentAddress, bits, bitsConsumed);
            final boolean done = loader.load();
            bits = loader.getBits();
            bitsConsumed = loader.getBitsConsumed();
            currentAddress = loader.getCurrentAddress();
            if (done) {
                break;
            }

            bitsConsumed = decodeSymbol(outputBase, output, bits, bitsConsumed, tableLog, numbersOfBits, symbols);
            bitsConsumed = decodeSymbol(outputBase, output + 1, bits, bitsConsumed, tableLog, numbersOfBits, symbols);
            bitsConsumed = decodeSymbol(outputBase, output + 2, bits, bitsConsumed, tableLog, numbersOfBits, symbols);
            bitsConsumed = decodeSymbol(outputBase, output + 3, bits, bitsConsumed, tableLog, numbersOfBits, symbols);
            output += SIZE_OF_INT;
        }

        decodeTail(inputBase, inputAddress, currentAddress, bitsConsumed, bits, outputBase, output, outputLimit);
    }

    public void decode4Streams(final ByteBuffer inputBase, final int inputAddress, final int inputLimit,
                               final ByteBuffer outputBase, final int outputAddress, final int outputLimit) {
        verify(inputLimit - inputAddress >= 10, inputAddress, "Input is corrupted"); // jump table + 1 byte per stream

        final int start1 = inputAddress + 3 * SIZE_OF_SHORT; // for the shorts we read below
        final int start2 = start1 + (inputBase.getShort(inputAddress) & 0xFFFF);
        final int start3 = start2 + (inputBase.getShort(inputAddress + 2) & 0xFFFF);
        final int start4 = start3 + (inputBase.getShort(inputAddress + 4) & 0xFFFF);

        BitStream.Initializer initializer = new BitStream.Initializer(inputBase, start1, start2);
        initializer.initialize();
        int stream1bitsConsumed = initializer.getBitsConsumed();
        int stream1currentAddress = initializer.getCurrentAddress();
        long stream1bits = initializer.getBits();

        initializer = new BitStream.Initializer(inputBase, start2, start3);
        initializer.initialize();
        int stream2bitsConsumed = initializer.getBitsConsumed();
        int stream2currentAddress = initializer.getCurrentAddress();
        long stream2bits = initializer.getBits();

        initializer = new BitStream.Initializer(inputBase, start3, start4);
        initializer.initialize();
        int stream3bitsConsumed = initializer.getBitsConsumed();
        int stream3currentAddress = initializer.getCurrentAddress();
        long stream3bits = initializer.getBits();

        initializer = new BitStream.Initializer(inputBase, start4, inputLimit);
        initializer.initialize();
        int stream4bitsConsumed = initializer.getBitsConsumed();
        int stream4currentAddress = initializer.getCurrentAddress();
        long stream4bits = initializer.getBits();

        final int segmentSize = (int) ((outputLimit - outputAddress + 3) / 4);

        final int outputStart2 = outputAddress + segmentSize;
        final int outputStart3 = outputStart2 + segmentSize;
        final int outputStart4 = outputStart3 + segmentSize;

        int output1 = outputAddress;
        int output2 = outputStart2;
        int output3 = outputStart3;
        int output4 = outputStart4;

        final long fastOutputLimit = outputLimit - 7;
        final int tableLog = this.tableLog;
        final byte[] numbersOfBits = this.numbersOfBits;
        final byte[] symbols = this.symbols;

        while (output4 < fastOutputLimit) {
            stream1bitsConsumed = decodeSymbol(outputBase, output1, stream1bits, stream1bitsConsumed, tableLog, numbersOfBits, symbols);
            stream2bitsConsumed = decodeSymbol(outputBase, output2, stream2bits, stream2bitsConsumed, tableLog, numbersOfBits, symbols);
            stream3bitsConsumed = decodeSymbol(outputBase, output3, stream3bits, stream3bitsConsumed, tableLog, numbersOfBits, symbols);
            stream4bitsConsumed = decodeSymbol(outputBase, output4, stream4bits, stream4bitsConsumed, tableLog, numbersOfBits, symbols);

            stream1bitsConsumed = decodeSymbol(outputBase, output1 + 1, stream1bits, stream1bitsConsumed, tableLog, numbersOfBits, symbols);
            stream2bitsConsumed = decodeSymbol(outputBase, output2 + 1, stream2bits, stream2bitsConsumed, tableLog, numbersOfBits, symbols);
            stream3bitsConsumed = decodeSymbol(outputBase, output3 + 1, stream3bits, stream3bitsConsumed, tableLog, numbersOfBits, symbols);
            stream4bitsConsumed = decodeSymbol(outputBase, output4 + 1, stream4bits, stream4bitsConsumed, tableLog, numbersOfBits, symbols);

            stream1bitsConsumed = decodeSymbol(outputBase, output1 + 2, stream1bits, stream1bitsConsumed, tableLog, numbersOfBits, symbols);
            stream2bitsConsumed = decodeSymbol(outputBase, output2 + 2, stream2bits, stream2bitsConsumed, tableLog, numbersOfBits, symbols);
            stream3bitsConsumed = decodeSymbol(outputBase, output3 + 2, stream3bits, stream3bitsConsumed, tableLog, numbersOfBits, symbols);
            stream4bitsConsumed = decodeSymbol(outputBase, output4 + 2, stream4bits, stream4bitsConsumed, tableLog, numbersOfBits, symbols);

            stream1bitsConsumed = decodeSymbol(outputBase, output1 + 3, stream1bits, stream1bitsConsumed, tableLog, numbersOfBits, symbols);
            stream2bitsConsumed = decodeSymbol(outputBase, output2 + 3, stream2bits, stream2bitsConsumed, tableLog, numbersOfBits, symbols);
            stream3bitsConsumed = decodeSymbol(outputBase, output3 + 3, stream3bits, stream3bitsConsumed, tableLog, numbersOfBits, symbols);
            stream4bitsConsumed = decodeSymbol(outputBase, output4 + 3, stream4bits, stream4bitsConsumed, tableLog, numbersOfBits, symbols);

            output1 += SIZE_OF_INT;
            output2 += SIZE_OF_INT;
            output3 += SIZE_OF_INT;
            output4 += SIZE_OF_INT;

            BitStream.Loader loader = new BitStream.Loader(inputBase, start1, stream1currentAddress, stream1bits, stream1bitsConsumed);
            boolean done = loader.load();
            stream1bitsConsumed = loader.getBitsConsumed();
            stream1bits = loader.getBits();
            stream1currentAddress = loader.getCurrentAddress();

            if (done) {
                break;
            }

            loader = new BitStream.Loader(inputBase, start2, stream2currentAddress, stream2bits, stream2bitsConsumed);
            done = loader.load();
            stream2bitsConsumed = loader.getBitsConsumed();
            stream2bits = loader.getBits();
            stream2currentAddress = loader.getCurrentAddress();

            if (done) {
                break;
            }

            loader = new BitStream.Loader(inputBase, start3, stream3currentAddress, stream3bits, stream3bitsConsumed);
            done = loader.load();
            stream3bitsConsumed = loader.getBitsConsumed();
            stream3bits = loader.getBits();
            stream3currentAddress = loader.getCurrentAddress();
            if (done) {
                break;
            }

            loader = new BitStream.Loader(inputBase, start4, stream4currentAddress, stream4bits, stream4bitsConsumed);
            done = loader.load();
            stream4bitsConsumed = loader.getBitsConsumed();
            stream4bits = loader.getBits();
            stream4currentAddress = loader.getCurrentAddress();
            if (done) {
                break;
            }
        }

        verify(output1 <= outputStart2 && output2 <= outputStart3 && output3 <= outputStart4, inputAddress, "Input is corrupted");

        /// finish streams one by one
        decodeTail(inputBase, start1, stream1currentAddress, stream1bitsConsumed, stream1bits, outputBase, output1, outputStart2);
        decodeTail(inputBase, start2, stream2currentAddress, stream2bitsConsumed, stream2bits, outputBase, output2, outputStart3);
        decodeTail(inputBase, start3, stream3currentAddress, stream3bitsConsumed, stream3bits, outputBase, output3, outputStart4);
        decodeTail(inputBase, start4, stream4currentAddress, stream4bitsConsumed, stream4bits, outputBase, output4, outputLimit);
    }

    private void decodeTail(final ByteBuffer inputBase, final int startAddress, int currentAddress,
                            int bitsConsumed, long bits, final ByteBuffer outputBase, int outputAddress, final int outputLimit) {
        final int tableLog = this.tableLog;
        final byte[] numbersOfBits = this.numbersOfBits;
        final byte[] symbols = this.symbols;

        // closer to the end
        while (outputAddress < outputLimit) {
            final BitStream.Loader loader = new BitStream.Loader(inputBase, startAddress, currentAddress, bits, bitsConsumed);
            final boolean done = loader.load();
            bitsConsumed = loader.getBitsConsumed();
            bits = loader.getBits();
            currentAddress = loader.getCurrentAddress();
            if (done) {
                break;
            }

            bitsConsumed = decodeSymbol(outputBase, outputAddress++, bits, bitsConsumed, tableLog, numbersOfBits, symbols);
        }

        // not more data in bit stream, so no need to reload
        while (outputAddress < outputLimit) {
            bitsConsumed = decodeSymbol(outputBase, outputAddress++, bits, bitsConsumed, tableLog, numbersOfBits, symbols);
        }

        verify(isEndOfStream(startAddress, currentAddress, bitsConsumed), startAddress, "Bit stream is not fully consumed");
    }

    private static int decodeSymbol(final ByteBuffer outputBase, final int outputAddress,
                                    final long bitContainer, final int bitsConsumed,
                                    final int tableLog, final byte[] numbersOfBits, final byte[] symbols) {
        final int value = (int) peekBitsFast(bitsConsumed, bitContainer, tableLog);
        outputBase.put(outputAddress, symbols[value]);
        return bitsConsumed + numbersOfBits[value];
    }
}
