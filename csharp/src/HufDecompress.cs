/* ******************************************************************
   Huffman decoder, part of New Generation Entropy library
   Copyright (C) 2013-2016, Yann Collet.

   BSD 2-Clause License (http://www.opensource.org/licenses/bsd-license.php)

   Redistribution and use in source and binary forms, with or without
   modification, are permitted provided that the following conditions are
   met:

       * Redistributions of source code must retain the above copyright
   notice, this list of conditions and the following disclaimer.
       * Redistributions in binary form must reproduce the above
   copyright notice, this list of conditions and the following disclaimer
   in the documentation and/or other materials provided with the
   distribution.

   THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
   "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
   LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
   A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
   OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
   SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
   LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
   DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
   THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
   (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
   OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

    You can contact the author at :
    - FSE+HUF source repository : https://github.com/Cyan4973/FiniteStateEntropy
    - Public forum : https://groups.google.com/forum/#!forum/lz4c
****************************************************************** */

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BYTE = System.Byte;
using size_t = System.UInt32;
using U16 = System.UInt16;
using U32 = System.UInt32;
using U64 = System.UInt64;
using S16 = System.Int16;

using HUF_DTable = System.UInt32;

using static EPAM.Deltix.ZStd.Mem;
using static EPAM.Deltix.ZStd.ZStdErrors;
using static EPAM.Deltix.ZStd.BitStream;
using static EPAM.Deltix.ZStd.EntropyCommon;
using static EPAM.Deltix.ZStd.ZStdInternal;
using static EPAM.Deltix.ZStd.Huf;

namespace EPAM.Deltix.ZStd
{
	internal static unsafe class HufDecompress
	{
		///* **************************************************************
		//*  Dependencies
		//****************************************************************/
		//#include <string.h>     /* memcpy, memset */
		//#include "bitstream.h"  /* BIT_* */
		//#include "compiler.h"
		//#include "fse.h"        /* header compression */
		//#define HUF_STATIC_LINKING_ONLY
		//#include "huf.h"
		//#include "error_private.h"


		///* **************************************************************
		//*  Error Management
		//****************************************************************/
		//#define HUF_isError ERR_isError
		//#define HUF_STATIC_ASSERT(c) { enum { HUF_static_assert = 1/(int)(!!(c)) }; }   /* use only *after* variable declarations */
		//#define CHECK_F(f) { size_t  err_ = (f); if (IsError(err_)) return err_; }


		///* **************************************************************
		//*  Byte alignment for workSpace management
		//****************************************************************/
		public static uint HUF_ALIGN(uint x, uint a) => HUF_ALIGN_MASK((x), (a) - 1);

		public static uint HUF_ALIGN_MASK(uint x, uint mask) => (((x) + (mask)) & ~(mask));


		///*-***************************/
		///*  generic DTableDesc       */
		///*-***************************/
		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
		public struct DTableDesc
		{
			public BYTE maxTableLog;
			public BYTE tableType;
			public BYTE tableLog;
			public BYTE reserved;
		}

		public static DTableDesc GetDTableDesc(HUF_DTable* table)
		{
			return *(DTableDesc*)table;
			//DTableDesc dtd;
			//memcpy(&dtd, table, sizeof(dtd));
			//return dtd;
		}


		///*-***************************/
		///*  single-symbol decoding   */
		///*-***************************/
		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
		public struct HUF_DEltX2
		{
			public BYTE byteField;
			public BYTE nbBits;
		}   /* single-symbol decoding */

		public static size_t HUF_readDTableX2_wksp(HUF_DTable* DTable, void* src, size_t srcSize, void* workSpace, size_t wkspSize)
		{
			U32 tableLog = 0;
			U32 nbSymbols = 0;
			size_t iSize;
			void* dtPtr = DTable + 1;
			HUF_DEltX2* dt = (HUF_DEltX2*)dtPtr;

			U32* rankVal;
			BYTE* huffWeight;
			size_t spaceUsed32 = 0;

			rankVal = (U32*)workSpace + spaceUsed32;
			spaceUsed32 += HUF_TABLELOG_ABSOLUTEMAX + 1;
			huffWeight = (BYTE*)((U32*)workSpace + spaceUsed32);
			spaceUsed32 += HUF_ALIGN(HUF_SYMBOLVALUE_MAX + 1, sizeof(U32)) >> 2;

			if ((spaceUsed32 << 2) > wkspSize) return ERROR(Error.tableLog_tooLarge);

			//HUF_STATIC_ASSERT(sizeof(DTableDesc) == sizeof(HUF_DTable));
			/* memset(huffWeight, 0, sizeof(huffWeight)); */   /* is not necessary, even though some analyzer complain ... */

			iSize = ReadStats(huffWeight, HUF_SYMBOLVALUE_MAX + 1, rankVal, &nbSymbols, &tableLog, src, srcSize);
			if (IsError(iSize)) return iSize;

			/* Table header */
			{
				DTableDesc dtd = GetDTableDesc(DTable);
				if (tableLog > (U32)(dtd.maxTableLog + 1)) return ERROR(Error.tableLog_tooLarge);   /* DTable too small, Huffman tree cannot fit in */
				dtd.tableType = 0;
				dtd.tableLog = (BYTE)tableLog;
				*(DTableDesc*)DTable = dtd; // memcpy(DTable, &dtd, sizeof(dtd));
			}

			/* Calculate starting value for each rank */
			{
				U32 n, nextRankStart = 0;
				for (n = 1; n < tableLog + 1; n++)
				{
					U32 current = nextRankStart;
					nextRankStart += (rankVal[n] << (int)(n - 1));
					rankVal[n] = current;
				}
			}

			/* fill DTable */
			{
				U32 n;
				for (n = 0; n < nbSymbols; n++)
				{
					U32 w = huffWeight[n];
					U32 length = ((U32)1 << (int)w) >> 1;
					U32 u;
					HUF_DEltX2 D;
					D.byteField = (BYTE)n;
					D.nbBits = (BYTE)(tableLog + 1 - w);
					for (u = rankVal[w]; u < rankVal[w] + length; u++)
						dt[u] = D;
					rankVal[w] += length;
				}
			}

			return iSize;
		}

		//size_t HUF_readDTableX2(HUF_DTable* DTable, void* src, size_t srcSize)
		//{
		//    U32 workSpace[HUF_DECOMPRESS_WORKSPACE_SIZE_U32];
		//    return HUF_readDTableX2_wksp(DTable, src, srcSize,
		//                                 workSpace, sizeof(workSpace));
		//}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
		public struct HUF_DEltX4
		{
			public U16 sequence;
			public BYTE nbBits;
			public BYTE length;
		}  /* double-symbols decoding */

		public static BYTE HUF_decodeSymbolX2(BIT_DStream_t Dstream, HUF_DEltX2* dt, U32 dtLog)
		{
			size_t val = LookBitsFast(Dstream, dtLog); /* note : dtLog >= 1 */
			BYTE c = dt[val].byteField;
			SkipBits(Dstream, dt[val].nbBits);
			return c;
		}

		public static void HUF_DECODE_SYMBOLX2_0(ref BYTE* ptr, BIT_DStream_t DStreamPtr, HUF_DEltX2* dt, U32 dtLog)
		{
			*ptr++ = HUF_decodeSymbolX2(DStreamPtr, dt, dtLog);
		}

		public static void HUF_DECODE_SYMBOLX2_1(ref BYTE* ptr, BIT_DStream_t DStreamPtr, HUF_DEltX2* dt, U32 dtLog)
		{
			if (MEM_64bits() || (HUF_TABLELOG_MAX <= 12))
				HUF_DECODE_SYMBOLX2_0(ref ptr, DStreamPtr, dt, dtLog);
		}

		public static void HUF_DECODE_SYMBOLX2_2(ref BYTE* ptr, BIT_DStream_t DStreamPtr, HUF_DEltX2* dt, U32 dtLog)
		{
			if (MEM_64bits())
				HUF_DECODE_SYMBOLX2_0(ref ptr, DStreamPtr, dt, dtLog);
		}

		public static size_t HUF_decodeStreamX2(BYTE* p, BIT_DStream_t bitDPtr, BYTE* pEnd, HUF_DEltX2* dt, U32 dtLog)
		{
			BYTE* pStart = p;

			/* up to 4 symbols at a time */
			while ((ReloadDStream(bitDPtr) == BIT_DStream_status.BIT_DStream_unfinished) & (p < pEnd - 3))
			{
				HUF_DECODE_SYMBOLX2_2(ref p, bitDPtr, dt, dtLog);
				HUF_DECODE_SYMBOLX2_1(ref p, bitDPtr, dt, dtLog);
				HUF_DECODE_SYMBOLX2_2(ref p, bitDPtr, dt, dtLog);
				HUF_DECODE_SYMBOLX2_0(ref p, bitDPtr, dt, dtLog);
			}

			/* [0-3] symbols remaining */
			if (MEM_32bits())
				while ((ReloadDStream(bitDPtr) == BIT_DStream_status.BIT_DStream_unfinished) & (p < pEnd))
					HUF_DECODE_SYMBOLX2_0(ref p, bitDPtr, dt, dtLog);

			/* no more data to retrieve from bitstream, no need to reload */
			while (p < pEnd)
				HUF_DECODE_SYMBOLX2_0(ref p, bitDPtr, dt, dtLog);

			return (size_t)(pEnd - pStart);
		}

		public static size_t HUF_decompress1X2_usingDTable_internal_body(void* dst, size_t dstSize, void* cSrc, size_t cSrcSize, HUF_DTable* DTable)
		{
			BYTE* op = (BYTE*)dst;
			BYTE* oend = op + dstSize;
			void* dtPtr = DTable + 1;
			HUF_DEltX2* dt = (HUF_DEltX2*)dtPtr;
			BIT_DStream_t bitD = new BIT_DStream_t();
			DTableDesc dtd = GetDTableDesc(DTable);
			U32 dtLog = dtd.tableLog;

			{ size_t errcod = InitDStream(bitD, cSrc, cSrcSize); if (IsError(errcod)) return errcod; }

			HUF_decodeStreamX2(op, bitD, oend, dt, dtLog);

			if (EndOfDStream(bitD) == 0) return ERROR(Error.corruption_detected);

			return dstSize;
		}

		public static size_t HUF_decompress4X2_usingDTable_internal_body(void* dst, size_t dstSize, void* cSrc, size_t cSrcSize, HUF_DTable* DTable)
		{
			/* Check */
			if (cSrcSize < 10) return ERROR(Error.corruption_detected);  /* strict minimum : jump table + 1 byte per stream */

			{
				BYTE* istart = (BYTE*)cSrc;
				BYTE* ostart = (BYTE*)dst;
				BYTE* oend = ostart + dstSize;
				void* dtPtr = DTable + 1;
				HUF_DEltX2* dt = (HUF_DEltX2*)dtPtr;

				/* Init */
				BIT_DStream_t bitD1 = new BIT_DStream_t();
				BIT_DStream_t bitD2 = new BIT_DStream_t();
				BIT_DStream_t bitD3 = new BIT_DStream_t();
				BIT_DStream_t bitD4 = new BIT_DStream_t();
				size_t length1 = MEM_readLE16(istart);
				size_t length2 = MEM_readLE16(istart + 2);
				size_t length3 = MEM_readLE16(istart + 4);
				size_t length4 = cSrcSize - (length1 + length2 + length3 + 6);
				BYTE* istart1 = istart + 6;  /* jumpTable */
				BYTE* istart2 = istart1 + length1;
				BYTE* istart3 = istart2 + length2;
				BYTE* istart4 = istart3 + length3;
				size_t segmentSize = (dstSize + 3) / 4;
				BYTE* opStart2 = ostart + segmentSize;
				BYTE* opStart3 = opStart2 + segmentSize;
				BYTE* opStart4 = opStart3 + segmentSize;
				BYTE* op1 = ostart;
				BYTE* op2 = opStart2;
				BYTE* op3 = opStart3;
				BYTE* op4 = opStart4;
				U32 endSignal = (U32)BIT_DStream_status.BIT_DStream_unfinished;
				DTableDesc dtd = GetDTableDesc(DTable);
				U32 dtLog = dtd.tableLog;

				if (length4 > cSrcSize) return ERROR(Error.corruption_detected);   /* overflow */
				{ size_t errcod = InitDStream(bitD1, istart1, length1); if (IsError(errcod)) return errcod; }
				{ size_t errcod = InitDStream(bitD2, istart2, length2); if (IsError(errcod)) return errcod; }
				{ size_t errcod = InitDStream(bitD3, istart3, length3); if (IsError(errcod)) return errcod; }
				{ size_t errcod = InitDStream(bitD4, istart4, length4); if (IsError(errcod)) return errcod; }

				/* up to 16 symbols per loop (4 symbols per stream) in 64-bit mode */
				endSignal = (U32)ReloadDStream(bitD1) | (U32)ReloadDStream(bitD2) | (U32)ReloadDStream(bitD3) | (U32)ReloadDStream(bitD4);
				while ((endSignal == (U32)BIT_DStream_status.BIT_DStream_unfinished) && (op4 < (oend - 3)))
				{
					HUF_DECODE_SYMBOLX2_2(ref op1, bitD1, dt, dtLog);
					HUF_DECODE_SYMBOLX2_2(ref op2, bitD2, dt, dtLog);
					HUF_DECODE_SYMBOLX2_2(ref op3, bitD3, dt, dtLog);
					HUF_DECODE_SYMBOLX2_2(ref op4, bitD4, dt, dtLog);
					HUF_DECODE_SYMBOLX2_1(ref op1, bitD1, dt, dtLog);
					HUF_DECODE_SYMBOLX2_1(ref op2, bitD2, dt, dtLog);
					HUF_DECODE_SYMBOLX2_1(ref op3, bitD3, dt, dtLog);
					HUF_DECODE_SYMBOLX2_1(ref op4, bitD4, dt, dtLog);
					HUF_DECODE_SYMBOLX2_2(ref op1, bitD1, dt, dtLog);
					HUF_DECODE_SYMBOLX2_2(ref op2, bitD2, dt, dtLog);
					HUF_DECODE_SYMBOLX2_2(ref op3, bitD3, dt, dtLog);
					HUF_DECODE_SYMBOLX2_2(ref op4, bitD4, dt, dtLog);
					HUF_DECODE_SYMBOLX2_0(ref op1, bitD1, dt, dtLog);
					HUF_DECODE_SYMBOLX2_0(ref op2, bitD2, dt, dtLog);
					HUF_DECODE_SYMBOLX2_0(ref op3, bitD3, dt, dtLog);
					HUF_DECODE_SYMBOLX2_0(ref op4, bitD4, dt, dtLog);
					ReloadDStream(bitD1);
					ReloadDStream(bitD2);
					ReloadDStream(bitD3);
					ReloadDStream(bitD4);
				}

				/* check corruption */
				/* note : should not be necessary : op# advance in lock step, and we control op4.
		         *        but curiously, binary generated by gcc 7.2 & 7.3 with -mbmi2 runs faster when >=1 test is present */
				if (op1 > opStart2) return ERROR(Error.corruption_detected);
				if (op2 > opStart3) return ERROR(Error.corruption_detected);
				if (op3 > opStart4) return ERROR(Error.corruption_detected);
				/* note : op4 supposed already verified within main loop */

				/* finish bitStreams one by one */
				HUF_decodeStreamX2(op1, bitD1, opStart2, dt, dtLog);
				HUF_decodeStreamX2(op2, bitD2, opStart3, dt, dtLog);
				HUF_decodeStreamX2(op3, bitD3, opStart4, dt, dtLog);
				HUF_decodeStreamX2(op4, bitD4, oend, dt, dtLog);

				/* check */
				{
					U32 endCheck = EndOfDStream(bitD1) & EndOfDStream(bitD2) & EndOfDStream(bitD3) & EndOfDStream(bitD4);
					if (endCheck == 0) return ERROR(Error.corruption_detected);
				}

				/* decoded size */
				return dstSize;
			}
		}


		public static U32 HUF_decodeSymbolX4(void* op, BIT_DStream_t DStream, HUF_DEltX4* dt, U32 dtLog)
		{
			size_t val = LookBitsFast(DStream, dtLog);   /* note : dtLog >= 1 */
			memcpy(op, dt + val, 2);
			SkipBits(DStream, dt[val].nbBits);
			return dt[val].length;
		}

		public static U32 HUF_decodeLastSymbolX4(void* op, BIT_DStream_t DStream, HUF_DEltX4* dt, U32 dtLog)
		{
			size_t val = LookBitsFast(DStream, dtLog);   /* note : dtLog >= 1 */
			memcpy(op, dt + val, 1);
			if (dt[val].length == 1) SkipBits(DStream, dt[val].nbBits);
			else
			{
				if (DStream.bitsConsumed < (sizeOfBitContainer * 8))
				{
					SkipBits(DStream, dt[val].nbBits);
					if (DStream.bitsConsumed > (sizeOfBitContainer * 8))
						/* ugly hack; works only because it's the last symbol. Note : can't easily extract nbBits from just this symbol */
						DStream.bitsConsumed = (sizeOfBitContainer * 8);
				}
			}
			return 1;
		}

		public static void HUF_DECODE_SYMBOLX4_0(ref BYTE* ptr, BIT_DStream_t DStreamPtr, HUF_DEltX4* dt, U32 dtLog)
		{
			ptr += HUF_decodeSymbolX4(ptr, DStreamPtr, dt, dtLog);
		}

		public static void HUF_DECODE_SYMBOLX4_1(ref BYTE* ptr, BIT_DStream_t DStreamPtr, HUF_DEltX4* dt, U32 dtLog)
		{
			if (MEM_64bits() || (HUF_TABLELOG_MAX <= 12))
				ptr += HUF_decodeSymbolX4(ptr, DStreamPtr, dt, dtLog);
		}

		public static void HUF_DECODE_SYMBOLX4_2(ref BYTE* ptr, BIT_DStream_t DStreamPtr, HUF_DEltX4* dt, U32 dtLog)
		{
			if (MEM_64bits())
				ptr += HUF_decodeSymbolX4(ptr, DStreamPtr, dt, dtLog);
		}

		public static size_t HUF_decodeStreamX4(BYTE* p, BIT_DStream_t bitDPtr, BYTE* pEnd, HUF_DEltX4* dt, U32 dtLog)
		{
			BYTE* pStart = p;

			/* up to 8 symbols at a time */
			while ((ReloadDStream(bitDPtr) == BIT_DStream_status.BIT_DStream_unfinished) & (p < pEnd - (sizeOfBitContainer - 1)))
			{
				HUF_DECODE_SYMBOLX4_2(ref p, bitDPtr, dt, dtLog);
				HUF_DECODE_SYMBOLX4_1(ref p, bitDPtr, dt, dtLog);
				HUF_DECODE_SYMBOLX4_2(ref p, bitDPtr, dt, dtLog);
				HUF_DECODE_SYMBOLX4_0(ref p, bitDPtr, dt, dtLog);
			}

			/* closer to end : up to 2 symbols at a time */
			while ((ReloadDStream(bitDPtr) == BIT_DStream_status.BIT_DStream_unfinished) & (p <= pEnd - 2))
				HUF_DECODE_SYMBOLX4_0(ref p, bitDPtr, dt, dtLog);

			while (p <= pEnd - 2)
				HUF_DECODE_SYMBOLX4_0(ref p, bitDPtr, dt, dtLog);   /* no need to reload : reached the end of DStream */

			if (p < pEnd)
				p += HUF_decodeLastSymbolX4(p, bitDPtr, dt, dtLog);

			return (size_t)(p - pStart);
		}

		static size_t HUF_decompress1X4_usingDTable_internal_body(void* dst, size_t dstSize, void* cSrc, size_t cSrcSize, HUF_DTable* DTable)
		{
			BIT_DStream_t bitD = new BIT_DStream_t();

			/* Init */
			{ size_t errcod = InitDStream(bitD, cSrc, cSrcSize); if (IsError(errcod)) return errcod; }

			/* decode */
			{
				BYTE* ostart = (BYTE*)dst;
				BYTE* oend = ostart + dstSize;
				void* dtPtr = DTable + 1;   /* force compiler to not use strict-aliasing */
				HUF_DEltX4* dt = (HUF_DEltX4*)dtPtr;
				DTableDesc dtd = GetDTableDesc(DTable);
				HUF_decodeStreamX4(ostart, bitD, oend, dt, dtd.tableLog);
			}

			/* check */
			if (EndOfDStream(bitD) == 0) return ERROR(Error.corruption_detected);

			/* decoded size */
			return dstSize;
		}


		static size_t HUF_decompress4X4_usingDTable_internal_body(void* dst, size_t dstSize, void* cSrc, size_t cSrcSize, HUF_DTable* DTable)
		{
			if (cSrcSize < 10) return ERROR(Error.corruption_detected);   /* strict minimum : jump table + 1 byte per stream */

			{
				BYTE* istart = (BYTE*)cSrc;
				BYTE* ostart = (BYTE*)dst;
				BYTE* oend = ostart + dstSize;
				void* dtPtr = DTable + 1;
				HUF_DEltX4* dt = (HUF_DEltX4*)dtPtr;

				/* Init */
				BIT_DStream_t bitD1 = new BIT_DStream_t();
				BIT_DStream_t bitD2 = new BIT_DStream_t();
				BIT_DStream_t bitD3 = new BIT_DStream_t();
				BIT_DStream_t bitD4 = new BIT_DStream_t();
				size_t length1 = MEM_readLE16(istart);
				size_t length2 = MEM_readLE16(istart + 2);
				size_t length3 = MEM_readLE16(istart + 4);
				size_t length4 = cSrcSize - (length1 + length2 + length3 + 6);
				BYTE* istart1 = istart + 6;  /* jumpTable */
				BYTE* istart2 = istart1 + length1;
				BYTE* istart3 = istart2 + length2;
				BYTE* istart4 = istart3 + length3;
				size_t segmentSize = (dstSize + 3) / 4;
				BYTE* opStart2 = ostart + segmentSize;
				BYTE* opStart3 = opStart2 + segmentSize;
				BYTE* opStart4 = opStart3 + segmentSize;
				BYTE* op1 = ostart;
				BYTE* op2 = opStart2;
				BYTE* op3 = opStart3;
				BYTE* op4 = opStart4;
				U32 endSignal;
				DTableDesc dtd = GetDTableDesc(DTable);
				U32 dtLog = dtd.tableLog;

				if (length4 > cSrcSize) return ERROR(Error.corruption_detected);   /* overflow */
				{ size_t errcod = InitDStream(bitD1, istart1, length1); if (IsError(errcod)) return errcod; }
				{ size_t errcod = InitDStream(bitD2, istart2, length2); if (IsError(errcod)) return errcod; }
				{ size_t errcod = InitDStream(bitD3, istart3, length3); if (IsError(errcod)) return errcod; }
				{ size_t errcod = InitDStream(bitD4, istart4, length4); if (IsError(errcod)) return errcod; }

				/* 16-32 symbols per loop (4-8 symbols per stream) */
				endSignal = (U32)ReloadDStream(bitD1) | (U32)ReloadDStream(bitD2) | (U32)ReloadDStream(bitD3) | (U32)ReloadDStream(bitD4);
				for (; (endSignal == (U32)BIT_DStream_status.BIT_DStream_unfinished) & (op4 < (oend - (sizeOfBitContainer - 1)));)
				{
					HUF_DECODE_SYMBOLX4_2(ref op1, bitD1, dt, dtLog);
					HUF_DECODE_SYMBOLX4_2(ref op2, bitD2, dt, dtLog);
					HUF_DECODE_SYMBOLX4_2(ref op3, bitD3, dt, dtLog);
					HUF_DECODE_SYMBOLX4_2(ref op4, bitD4, dt, dtLog);
					HUF_DECODE_SYMBOLX4_1(ref op1, bitD1, dt, dtLog);
					HUF_DECODE_SYMBOLX4_1(ref op2, bitD2, dt, dtLog);
					HUF_DECODE_SYMBOLX4_1(ref op3, bitD3, dt, dtLog);
					HUF_DECODE_SYMBOLX4_1(ref op4, bitD4, dt, dtLog);
					HUF_DECODE_SYMBOLX4_2(ref op1, bitD1, dt, dtLog);
					HUF_DECODE_SYMBOLX4_2(ref op2, bitD2, dt, dtLog);
					HUF_DECODE_SYMBOLX4_2(ref op3, bitD3, dt, dtLog);
					HUF_DECODE_SYMBOLX4_2(ref op4, bitD4, dt, dtLog);
					HUF_DECODE_SYMBOLX4_0(ref op1, bitD1, dt, dtLog);
					HUF_DECODE_SYMBOLX4_0(ref op2, bitD2, dt, dtLog);
					HUF_DECODE_SYMBOLX4_0(ref op3, bitD3, dt, dtLog);
					HUF_DECODE_SYMBOLX4_0(ref op4, bitD4, dt, dtLog);

					endSignal = (U32)ReloadDStream(bitD1) | (U32)ReloadDStream(bitD2) | (U32)ReloadDStream(bitD3) | (U32)ReloadDStream(bitD4);
				}

				/* check corruption */
				if (op1 > opStart2) return ERROR(Error.corruption_detected);
				if (op2 > opStart3) return ERROR(Error.corruption_detected);
				if (op3 > opStart4) return ERROR(Error.corruption_detected);
				/* note : op4 already verified within main loop */

				/* finish bitStreams one by one */
				HUF_decodeStreamX4(op1, bitD1, opStart2, dt, dtLog);
				HUF_decodeStreamX4(op2, bitD2, opStart3, dt, dtLog);
				HUF_decodeStreamX4(op3, bitD3, opStart4, dt, dtLog);
				HUF_decodeStreamX4(op4, bitD4, oend, dt, dtLog);

				/* check */
				{
					U32 endCheck = (U32)EndOfDStream(bitD1) & (U32)EndOfDStream(bitD2) & (U32)EndOfDStream(bitD3) & (U32)EndOfDStream(bitD4);
					if (endCheck == 0) return ERROR(Error.corruption_detected);
				}

				/* decoded size */
				return dstSize;
			}
		}


		//typedef size_t (*HUF_decompress_usingDTable_t)(void *dst, size_t dstSize,
		//                                                void *cSrc,
		//                                               size_t cSrcSize,
		//                                                HUF_DTable *DTable);
		//#if DYNAMIC_BMI2

		//#define X(fn)                                                               \
		//                                                                            \
		//    static size_t fn##_default(                                             \
		//                  void* dst,  size_t dstSize,                               \
		//            void* cSrc, size_t cSrcSize,                              \
		//             HUF_DTable* DTable)                                       \
		//    {                                                                       \
		//        return fn##_body(dst, dstSize, cSrc, cSrcSize, DTable);             \
		//    }                                                                       \
		//                                                                            \
		//    static TARGET_ATTRIBUTE("bmi2") size_t fn##_bmi2(                       \
		//                  void* dst,  size_t dstSize,                               \
		//            void* cSrc, size_t cSrcSize,                              \
		//             HUF_DTable* DTable)                                       \
		//    {                                                                       \
		//        return fn##_body(dst, dstSize, cSrc, cSrcSize, DTable);             \
		//    }                                                                       \
		//                                                                            \
		//    static size_t fn(void* dst, size_t dstSize, void * cSrc,           \
		//                     size_t cSrcSize, HUF_DTable * DTable, int bmi2)   \
		//    {                                                                       \
		//        if (bmi2) {                                                         \
		//            return fn##_bmi2(dst, dstSize, cSrc, cSrcSize, DTable);         \
		//        }                                                                   \
		//        return fn##_default(dst, dstSize, cSrc, cSrcSize, DTable);          \
		//    }

		//#else

		//#define X(fn)                                                               \
		//    static size_t fn(void* dst, size_t dstSize, void * cSrc,           \
		//                     size_t cSrcSize, HUF_DTable * DTable, int bmi2)   \
		//    {                                                                       \
		//        (void)bmi2;                                                         \
		//        return fn##_body(dst, dstSize, cSrc, cSrcSize, DTable);             \
		//    }

		//#endif

		//X(HUF_decompress1X2_usingDTable_internal)
		//X(HUF_decompress4X2_usingDTable_internal)
		//X(HUF_decompress1X4_usingDTable_internal)
		//X(HUF_decompress4X4_usingDTable_internal)

		//#undef X


		//size_t HUF_decompress1X2_usingDTable(
		//          void* dst,  size_t dstSize,
		//    void* cSrc, size_t cSrcSize,
		//     HUF_DTable* DTable)
		//{
		//    DTableDesc dtd = GetDTableDesc(DTable);
		//    if (dtd.tableType != 0) return  ERROR( Error.GENERIC);
		//    return HUF_decompress1X2_usingDTable_internal(dst, dstSize, cSrc, cSrcSize, DTable, /* bmi2 */ 0);
		//}

		//size_t HUF_decompress1X2_DCtx_wksp(HUF_DTable* DCtx, void* dst, size_t dstSize,
		//                                   void* cSrc, size_t cSrcSize,
		//                                   void* workSpace, size_t wkspSize)
		//{
		//     BYTE* ip = ( BYTE*) cSrc;

		//    size_t  hSize = HUF_readDTableX2_wksp(DCtx, cSrc, cSrcSize, workSpace, wkspSize);
		//    if (IsError(hSize)) return hSize;
		//    if (hSize >= cSrcSize) return  ERROR( Error.srcSize_wrong);
		//    ip += hSize; cSrcSize -= hSize;

		//    return HUF_decompress1X2_usingDTable_internal(dst, dstSize, ip, cSrcSize, DCtx, /* bmi2 */ 0);
		//}


		//size_t HUF_decompress1X2_DCtx(HUF_DTable* DCtx, void* dst, size_t dstSize,
		//                              void* cSrc, size_t cSrcSize)
		//{
		//    U32 workSpace[HUF_DECOMPRESS_WORKSPACE_SIZE_U32];
		//    return HUF_decompress1X2_DCtx_wksp(DCtx, dst, dstSize, cSrc, cSrcSize,
		//                                       workSpace, sizeof(workSpace));
		//}

		//size_t HUF_decompress1X2 (void* dst, size_t dstSize, void* cSrc, size_t cSrcSize)
		//{
		//    HUF_CREATE_STATIC_DTABLEX2(DTable, HUF_TABLELOG_MAX);
		//    return HUF_decompress1X2_DCtx (DTable, dst, dstSize, cSrc, cSrcSize);
		//}

		//size_t HUF_decompress4X2_usingDTable(
		//          void* dst,  size_t dstSize,
		//    void* cSrc, size_t cSrcSize,
		//     HUF_DTable* DTable)
		//{
		//    DTableDesc dtd = GetDTableDesc(DTable);
		//    if (dtd.tableType != 0) return  ERROR( Error.GENERIC);
		//    return HUF_decompress4X2_usingDTable_internal(dst, dstSize, cSrc, cSrcSize, DTable, /* bmi2 */ 0);
		//}

		public static size_t HUF_decompress4X2_DCtx_wksp_bmi2(HUF_DTable* dctx, void* dst, size_t dstSize,
										   void* cSrc, size_t cSrcSize,
										   void* workSpace, size_t wkspSize/*, int bmi2*/)
		{
			BYTE* ip = (BYTE*)cSrc;

			size_t hSize = HUF_readDTableX2_wksp(dctx, cSrc, cSrcSize,
														workSpace, wkspSize);
			if (IsError(hSize)) return hSize;
			if (hSize >= cSrcSize) return ERROR(Error.srcSize_wrong);
			ip += hSize; cSrcSize -= hSize;

			return HUF_decompress4X2_usingDTable_internal_body(dst, dstSize, ip, cSrcSize, dctx/*, bmi2*/);
		}

		//size_t HUF_decompress4X2_DCtx_wksp(HUF_DTable* dctx, void* dst, size_t dstSize,
		//                                   void* cSrc, size_t cSrcSize,
		//                                   void* workSpace, size_t wkspSize)
		//{
		//    return HUF_decompress4X2_DCtx_wksp_bmi2(dctx, dst, dstSize, cSrc, cSrcSize, workSpace, wkspSize, 0);
		//}


		//size_t HUF_decompress4X2_DCtx (HUF_DTable* dctx, void* dst, size_t dstSize, void* cSrc, size_t cSrcSize)
		//{
		//    U32 workSpace[HUF_DECOMPRESS_WORKSPACE_SIZE_U32];
		//    return HUF_decompress4X2_DCtx_wksp(dctx, dst, dstSize, cSrc, cSrcSize,
		//                                       workSpace, sizeof(workSpace));
		//}
		//size_t HUF_decompress4X2 (void* dst, size_t dstSize, void* cSrc, size_t cSrcSize)
		//{
		//    HUF_CREATE_STATIC_DTABLEX2(DTable, HUF_TABLELOG_MAX);
		//    return HUF_decompress4X2_DCtx(DTable, dst, dstSize, cSrc, cSrcSize);
		//}


		///* *************************/
		///* double-symbols decoding */
		///* *************************/
		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
		public struct sortedSymbol_t
		{
			public BYTE symbol;
			public BYTE weight;
		}

		/* HUF_fillDTableX4Level2() :
		 * `rankValOrigin` must be a table of at least (HUF_TABLELOG_MAX + 1) U32 */
		public static void HUF_fillDTableX4Level2(HUF_DEltX4* DTable, U32 sizeLog, U32 consumed,
									U32* rankValOrigin, int minWeight,
									sortedSymbol_t* sortedSymbols, U32 sortedListSize,
								   U32 nbBitsBaseline, U16 baseSeq)
		{
			HUF_DEltX4 DElt;
			U32* rankVal = stackalloc U32[(int)HUF_TABLELOG_MAX + 1];
			/* get pre-calculated rankVal */
			memcpy(rankVal, rankValOrigin, sizeof(U32) * (HUF_TABLELOG_MAX + 1));

			/* fill skipped values */
			if (minWeight > 1)
			{
				U32 i, skipSize = rankVal[minWeight];
				DElt.sequence = baseSeq; // MEM_writeLE16(&(DElt.sequence), baseSeq);
				DElt.nbBits = (BYTE)(consumed);
				DElt.length = 1;
				for (i = 0; i < skipSize; i++)
					DTable[i] = DElt;
			}

			/* fill DTable */
			{
				U32 s;
				for (s = 0; s < sortedListSize; s++)
				{
					/* note : sortedSymbols already skipped */
					U32 symbol = sortedSymbols[s].symbol;
					U32 weight = sortedSymbols[s].weight;
					U32 nbBits = nbBitsBaseline - weight;
					U32 length = (U32)1 << (int)(sizeLog - nbBits);
					U32 start = rankVal[weight];
					U32 i = start;
					U32 end = start + length;

					DElt.sequence = (U16)(baseSeq + (symbol << 8)); // MEM_writeLE16(&(DElt.sequence), (U16)(baseSeq + (symbol << 8)));
					DElt.nbBits = (BYTE)(nbBits + consumed);
					DElt.length = 2;
					do
					{
						DTable[i++] = DElt;
					} while (i < end); /* since length >= 1 */

					rankVal[weight] += length;
				}
			}
		}

		//[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
		//internal struct rankValCol_t
		//{
		//	public fixed U32 data[(int)HUF_TABLELOG_MAX + 1];
		//}

		//[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
		//internal struct rankVal_t
		//{
		//	public fixed rankValCol_t rankVal_t[HUF_TABLELOG_MAX];
		//}

		//[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
		//internal struct rankVal_t
		//{
		//	public fixed U32 data[(int)HUF_TABLELOG_MAX * ((int)HUF_TABLELOG_MAX + 1)];
		//}

		public static void HUF_fillDTableX4(HUF_DEltX4* DTable, U32 targetLog,
									sortedSymbol_t* sortedList, U32 sortedListSize,
									U32* rankStart, U32* rankValOrigin, U32 maxWeight,
									U32 nbBitsBaseline)
		{

			U32* rankVal = stackalloc U32[(int)HUF_TABLELOG_MAX + 1];
			int scaleLog = (int)(nbBitsBaseline - targetLog); /* note : targetLog >= srcLog, hence scaleLog <= 1 */
			U32 minBits = nbBitsBaseline - maxWeight;
			U32 s;

			memcpy(rankVal, rankValOrigin, sizeof(U32) * (HUF_TABLELOG_MAX + 1));

			/* fill DTable */
			for (s = 0; s < sortedListSize; s++)
			{
				U16 symbol = sortedList[s].symbol;
				U32 weight = sortedList[s].weight;
				U32 nbBits = nbBitsBaseline - weight;
				U32 start = rankVal[weight];
				U32 length = (U32)1 << (int)(targetLog - nbBits);

				if (targetLog - nbBits >= minBits)
				{
					/* enough room for a second symbol */
					U32 sortedRank;
					int minWeight = (int)(nbBits + scaleLog);
					if (minWeight < 1) minWeight = 1;
					sortedRank = rankStart[minWeight];
					HUF_fillDTableX4Level2(DTable + start, targetLog - nbBits, nbBits,
						rankValOrigin + nbBits * (HUF_TABLELOG_MAX + 1), minWeight,
						sortedList + sortedRank, sortedListSize - sortedRank,
						nbBitsBaseline, symbol);
				}
				else
				{
					HUF_DEltX4 DElt;
					DElt.sequence = symbol;//MEM_writeLE16(&(DElt.sequence), symbol);
					DElt.nbBits = (BYTE)(nbBits);
					DElt.length = 1;
					{
						U32 end = start + length;
						U32 u;
						for (u = start; u < end; u++) DTable[u] = DElt;
					}
				}
				rankVal[weight] += length;
			}
		}

		public const uint sizeofRankValCol_t = sizeof(U32) * (HUF_TABLELOG_MAX + 1);

		public static size_t HUF_readDTableX4_wksp(HUF_DTable* DTable, void* src, size_t srcSize, U32* workSpace, size_t wkspSize)
		{
			U32 tableLog, maxW, sizeOfSort, nbSymbols;
			DTableDesc dtd = GetDTableDesc(DTable);
			U32 maxTableLog = dtd.maxTableLog;
			size_t iSize;
			void* dtPtr = DTable + 1; /* force compiler to avoid strict-aliasing */
			HUF_DEltX4* dt = (HUF_DEltX4*)dtPtr;
			U32* rankStart;

			U32* rankVal;
			U32* rankStats;
			U32* rankStart0;
			sortedSymbol_t* sortedSymbol;
			BYTE* weightList;
			size_t spaceUsed32 = 0;

			rankVal = (U32*)((U32*)workSpace + spaceUsed32);
			spaceUsed32 += (sizeofRankValCol_t * HUF_TABLELOG_MAX) >> 2;
			rankStats = (U32*)workSpace + spaceUsed32;
			spaceUsed32 += HUF_TABLELOG_MAX + 1;
			rankStart0 = (U32*)workSpace + spaceUsed32;
			spaceUsed32 += HUF_TABLELOG_MAX + 2;
			sortedSymbol = (sortedSymbol_t*)workSpace + (spaceUsed32 * sizeof(U32)) / sizeof(sortedSymbol_t);
			spaceUsed32 += HUF_ALIGN((size_t)sizeof(sortedSymbol_t) * (HUF_SYMBOLVALUE_MAX + 1), sizeof(U32)) >> 2;
			weightList = (BYTE*)((U32*)workSpace + spaceUsed32);
			spaceUsed32 += HUF_ALIGN(HUF_SYMBOLVALUE_MAX + 1, sizeof(U32)) >> 2;

			if ((spaceUsed32 << 2) > wkspSize) return ERROR(Error.tableLog_tooLarge);

			rankStart = rankStart0 + 1;
			memset(rankStats, 0, sizeof(U32) * (2 * HUF_TABLELOG_MAX + 2 + 1));

			//HUF_STATIC_ASSERT(sizeof(HUF_DEltX4) == sizeof(HUF_DTable));   /* if compiler fails here, assertion is wrong */
			if (maxTableLog > HUF_TABLELOG_MAX) return ERROR(Error.tableLog_tooLarge);
			/* memset(weightList, 0, sizeof(weightList)); */ /* is not necessary, even though some analyzer complain ... */

			iSize = ReadStats(weightList, HUF_SYMBOLVALUE_MAX + 1, rankStats, &nbSymbols, &tableLog, src, srcSize);
			if (IsError(iSize)) return iSize;

			/* check result */
			if (tableLog > maxTableLog) return ERROR(Error.tableLog_tooLarge); /* DTable can't fit code depth */

			/* find maxWeight */
			for (maxW = tableLog; rankStats[maxW] == 0; maxW--)
			{
			} /* necessarily finds a solution before 0 */

			/* Get start index of each weight */
			{
				U32 w, nextRankStart = 0;
				for (w = 1; w < maxW + 1; w++)
				{
					U32 current = nextRankStart;
					nextRankStart += rankStats[w];
					rankStart[w] = current;
				}
				rankStart[0] = nextRankStart; /* put all 0w symbols at the end of sorted list*/
				sizeOfSort = nextRankStart;
			}

			/* sort symbols by weight */
			{
				U32 s;
				for (s = 0; s < nbSymbols; s++)
				{
					U32 w = weightList[s];
					U32 r = rankStart[w]++;
					sortedSymbol[r].symbol = (BYTE)s;
					sortedSymbol[r].weight = (BYTE)w;
				}
				rankStart[0] = 0; /* forget 0w symbols; this is beginning of weight(1) */
			}

			/* Build rankVal */
			{
				U32* rankVal0 = &rankVal[0];
				{
					int rescale = (int)(maxTableLog - tableLog) - 1; /* tableLog <= maxTableLog */
					U32 nextRankVal = 0;
					U32 w;
					for (w = 1; w < maxW + 1; w++)
					{
						U32 current = nextRankVal;
						nextRankVal += rankStats[w] << (int)(w + rescale);
						rankVal0[w] = current;
					}
				}
				{
					U32 minBits = tableLog + 1 - maxW;
					U32 consumed;
					for (consumed = minBits; consumed < maxTableLog - minBits + 1; consumed++)
					{
						U32* rankValPtr = &rankVal[consumed * (HUF_TABLELOG_MAX + 1)];
						U32 w;
						for (w = 1; w < maxW + 1; w++)
						{
							rankValPtr[w] = rankVal0[w] >> (int)consumed;
						}
					}
				}
			}

			HUF_fillDTableX4(dt, maxTableLog,
				sortedSymbol, sizeOfSort,
				rankStart0, rankVal, maxW,
				tableLog + 1);

			dtd.tableLog = (BYTE)maxTableLog;
			dtd.tableType = 1;
			*(DTableDesc*)DTable = dtd; // memcpy(DTable, &dtd, sizeof(dtd));
			return iSize;
		}

		//size_t HUF_readDTableX4(HUF_DTable* DTable, void* src, size_t srcSize)
		//{
		//  U32 workSpace[HUF_DECOMPRESS_WORKSPACE_SIZE_U32];
		//  return HUF_readDTableX4_wksp(DTable, src, srcSize,
		//                               workSpace, sizeof(workSpace));
		//}

		//size_t HUF_decompress1X4_usingDTable(
		//          void* dst,  size_t dstSize,
		//    void* cSrc, size_t cSrcSize,
		//     HUF_DTable* DTable)
		//{
		//    DTableDesc dtd = GetDTableDesc(DTable);
		//    if (dtd.tableType != 1) return  ERROR( Error.GENERIC);
		//    return HUF_decompress1X4_usingDTable_internal(dst, dstSize, cSrc, cSrcSize, DTable, /* bmi2 */ 0);
		//}

		//size_t HUF_decompress1X4_DCtx_wksp(HUF_DTable* DCtx, void* dst, size_t dstSize,
		//                                   void* cSrc, size_t cSrcSize,
		//                                   void* workSpace, size_t wkspSize)
		//{
		//     BYTE* ip = ( BYTE*) cSrc;

		//    size_t  hSize = HUF_readDTableX4_wksp(DCtx, cSrc, cSrcSize,
		//                                               workSpace, wkspSize);
		//    if (IsError(hSize)) return hSize;
		//    if (hSize >= cSrcSize) return  ERROR( Error.srcSize_wrong);
		//    ip += hSize; cSrcSize -= hSize;

		//    return HUF_decompress1X4_usingDTable_internal(dst, dstSize, ip, cSrcSize, DCtx, /* bmi2 */ 0);
		//}


		//size_t HUF_decompress1X4_DCtx(HUF_DTable* DCtx, void* dst, size_t dstSize,
		//                              void* cSrc, size_t cSrcSize)
		//{
		//    U32 workSpace[HUF_DECOMPRESS_WORKSPACE_SIZE_U32];
		//    return HUF_decompress1X4_DCtx_wksp(DCtx, dst, dstSize, cSrc, cSrcSize,
		//                                       workSpace, sizeof(workSpace));
		//}

		//size_t HUF_decompress1X4 (void* dst, size_t dstSize, void* cSrc, size_t cSrcSize)
		//{
		//    HUF_CREATE_STATIC_DTABLEX4(DTable, HUF_TABLELOG_MAX);
		//    return HUF_decompress1X4_DCtx(DTable, dst, dstSize, cSrc, cSrcSize);
		//}

		//size_t HUF_decompress4X4_usingDTable(
		//          void* dst,  size_t dstSize,
		//    void* cSrc, size_t cSrcSize,
		//     HUF_DTable* DTable)
		//{
		//    DTableDesc dtd = GetDTableDesc(DTable);
		//    if (dtd.tableType != 1) return  ERROR( Error.GENERIC);
		//    return HUF_decompress4X4_usingDTable_internal(dst, dstSize, cSrc, cSrcSize, DTable, /* bmi2 */ 0);
		//}

		public static size_t HUF_decompress4X4_DCtx_wksp_bmi2(HUF_DTable* dctx, void* dst, size_t dstSize,
										   void* cSrc, size_t cSrcSize,
										   U32* workSpace, size_t wkspSize/*, int bmi2*/)
		{
			BYTE* ip = (BYTE*)cSrc;

			size_t hSize = HUF_readDTableX4_wksp(dctx, cSrc, cSrcSize,
												 workSpace, wkspSize);
			if (IsError(hSize)) return hSize;
			if (hSize >= cSrcSize) return ERROR(Error.srcSize_wrong);
			ip += hSize; cSrcSize -= hSize;

			return HUF_decompress4X4_usingDTable_internal_body(dst, dstSize, ip, cSrcSize, dctx /*, bmi2*/);
		}

		//size_t HUF_decompress4X4_DCtx_wksp(HUF_DTable* dctx, void* dst, size_t dstSize,
		//                                   void* cSrc, size_t cSrcSize,
		//                                   void* workSpace, size_t wkspSize)
		//{
		//    return HUF_decompress4X4_DCtx_wksp_bmi2(dctx, dst, dstSize, cSrc, cSrcSize, workSpace, wkspSize, /* bmi2 */ 0);
		//}


		//size_t HUF_decompress4X4_DCtx(HUF_DTable* dctx, void* dst, size_t dstSize,
		//                              void* cSrc, size_t cSrcSize)
		//{
		//    U32 workSpace[HUF_DECOMPRESS_WORKSPACE_SIZE_U32];
		//    return HUF_decompress4X4_DCtx_wksp(dctx, dst, dstSize, cSrc, cSrcSize,
		//                                       workSpace, sizeof(workSpace));
		//}

		//size_t HUF_decompress4X4 (void* dst, size_t dstSize, void* cSrc, size_t cSrcSize)
		//{
		//    HUF_CREATE_STATIC_DTABLEX4(DTable, HUF_TABLELOG_MAX);
		//    return HUF_decompress4X4_DCtx(DTable, dst, dstSize, cSrc, cSrcSize);
		//}


		///* ********************************/
		///* Generic decompression selector */
		///* ********************************/

		//size_t HUF_decompress1X_usingDTable(void* dst, size_t maxDstSize,
		//                                    void* cSrc, size_t cSrcSize,
		//                                     HUF_DTable* DTable)
		//{
		//    DTableDesc  dtd = GetDTableDesc(DTable);
		//    return dtd.tableType ? HUF_decompress1X4_usingDTable_internal(dst, maxDstSize, cSrc, cSrcSize, DTable, /* bmi2 */ 0) :
		//                           HUF_decompress1X2_usingDTable_internal(dst, maxDstSize, cSrc, cSrcSize, DTable, /* bmi2 */ 0);
		//}

		//size_t HUF_decompress4X_usingDTable(void* dst, size_t maxDstSize,
		//                                    void* cSrc, size_t cSrcSize,
		//                                     HUF_DTable* DTable)
		//{
		//    DTableDesc  dtd = GetDTableDesc(DTable);
		//    return dtd.tableType ? HUF_decompress4X4_usingDTable_internal(dst, maxDstSize, cSrc, cSrcSize, DTable, /* bmi2 */ 0) :
		//                           HUF_decompress4X2_usingDTable_internal(dst, maxDstSize, cSrc, cSrcSize, DTable, /* bmi2 */ 0);
		//}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
		public struct algo_time_t
		{
			public algo_time_t(U32 tableTime, U32 decode256Time)
			{
				this.tableTime = tableTime;
				this.decode256Time = decode256Time;
			}

			public U32 tableTime;
			public U32 decode256Time;
		}
		public static algo_time_t[,] algoTime = new algo_time_t[16 /* Quantization */, 3 /* single, double, quad */]
		{
		    /* single, double, quad */
		    {new algo_time_t(0,0), new algo_time_t(1,1), new algo_time_t(2,2)},  /* Q==0 : impossible */
		    {new algo_time_t(0,0), new algo_time_t(1,1), new algo_time_t(2,2)},  /* Q==1 : impossible */
		    {new algo_time_t(  38,130), new algo_time_t(1313, 74), new algo_time_t(2151, 38)},   /* Q == 2 : 12-18% */
		    {new algo_time_t( 448,128), new algo_time_t(1353, 74), new algo_time_t(2238, 41)},   /* Q == 3 : 18-25% */
		    {new algo_time_t( 556,128), new algo_time_t(1353, 74), new algo_time_t(2238, 47)},   /* Q == 4 : 25-32% */
		    {new algo_time_t( 714,128), new algo_time_t(1418, 74), new algo_time_t(2436, 53)},   /* Q == 5 : 32-38% */
		    {new algo_time_t( 883,128), new algo_time_t(1437, 74), new algo_time_t(2464, 61)},   /* Q == 6 : 38-44% */
		    {new algo_time_t( 897,128), new algo_time_t(1515, 75), new algo_time_t(2622, 68)},   /* Q == 7 : 44-50% */
		    {new algo_time_t( 926,128), new algo_time_t(1613, 75), new algo_time_t(2730, 75)},   /* Q == 8 : 50-56% */
		    {new algo_time_t( 947,128), new algo_time_t(1729, 77), new algo_time_t(3359, 77)},   /* Q == 9 : 56-62% */
		    {new algo_time_t(1107,128), new algo_time_t(2083, 81), new algo_time_t(4006, 84)},   /* Q ==10 : 62-69% */
		    {new algo_time_t(1177,128), new algo_time_t(2379, 87), new algo_time_t(4785, 88)},   /* Q ==11 : 69-75% */
		    {new algo_time_t(1242,128), new algo_time_t(2415, 93), new algo_time_t(5155, 84)},   /* Q ==12 : 75-81% */
		    {new algo_time_t(1349,128), new algo_time_t(2644,106), new algo_time_t(5260,106)},   /* Q ==13 : 81-87% */
		    {new algo_time_t(1455,128), new algo_time_t(2422,124), new algo_time_t(4174,124)},   /* Q ==14 : 87-93% */
		    {new algo_time_t( 722,128), new algo_time_t(1891,145), new algo_time_t(1936,146)},   /* Q ==15 : 93-99% */
		};

		/** SelectDecoder() :
		 *  Tells which decoder is likely to decode faster,
		 *  based on a set of pre-computed metrics.
		 * @return : 0==HUF_decompress4X2, 1==HUF_decompress4X4 .
		 *  Assumption : 0 < dstSize <= 128 KB */
		public static U32 SelectDecoder(size_t dstSize, size_t cSrcSize)
		{
			Debug.Assert(dstSize > 0);
			Debug.Assert(dstSize <= 128 * KB);
			/* decoder timing evaluation */
			{
				U32 Q = (cSrcSize >= dstSize) ? 15 : (U32)(cSrcSize * 16 / dstSize);   /* Q < 16 */
				U32 D256 = (U32)(dstSize >> 8);
				U32 DTime0 = algoTime[Q, 0].tableTime + (algoTime[Q, 0].decode256Time * D256);
				U32 DTime1 = algoTime[Q, 1].tableTime + (algoTime[Q, 1].decode256Time * D256);
				DTime1 += DTime1 >> 3;  /* advantage to algorithm using less memory, to reduce cache eviction */
				return DTime1 < DTime0 ? (U32)1 : 0;
			}
		}


		//typedef size_t (*decompressionAlgo)(void* dst, size_t dstSize, void* cSrc, size_t cSrcSize);

		//size_t Decompress(void* dst, size_t dstSize, void* cSrc, size_t cSrcSize)
		//{
		//    static readonly decompressionAlgo decompress[2] = { HUF_decompress4X2, HUF_decompress4X4 };

		//    /* validation checks */
		//    if (dstSize == 0) return  ERROR( Error.dstSize_tooSmall);
		//    if (cSrcSize > dstSize) return  ERROR( Error.corruption_detected);   /* invalid */
		//    if (cSrcSize == dstSize) { memcpy(dst, cSrc, dstSize); return dstSize; }   /* not compressed */
		//    if (cSrcSize == 1) { memset(dst, *( BYTE*)cSrc, dstSize); return dstSize; }   /* RLE */

		//    {   U32  algoNb = SelectDecoder(dstSize, cSrcSize);
		//        return decompress[algoNb](dst, dstSize, cSrc, cSrcSize);
		//    }
		//}

		//size_t HUF_decompress4X_DCtx (HUF_DTable* dctx, void* dst, size_t dstSize, void* cSrc, size_t cSrcSize)
		//{
		//    /* validation checks */
		//    if (dstSize == 0) return  ERROR( Error.dstSize_tooSmall);
		//    if (cSrcSize > dstSize) return  ERROR( Error.corruption_detected);   /* invalid */
		//    if (cSrcSize == dstSize) { memcpy(dst, cSrc, dstSize); return dstSize; }   /* not compressed */
		//    if (cSrcSize == 1) { memset(dst, *( BYTE*)cSrc, dstSize); return dstSize; }   /* RLE */

		//    {   U32  algoNb = SelectDecoder(dstSize, cSrcSize);
		//        return algoNb ? HUF_decompress4X4_DCtx(dctx, dst, dstSize, cSrc, cSrcSize) :
		//                        HUF_decompress4X2_DCtx(dctx, dst, dstSize, cSrc, cSrcSize) ;
		//    }
		//}

		//size_t HUF_decompress4X_hufOnly(HUF_DTable* dctx, void* dst, size_t dstSize, void* cSrc, size_t cSrcSize)
		//{
		//    U32 workSpace[HUF_DECOMPRESS_WORKSPACE_SIZE_U32];
		//    return HUF_decompress4X_hufOnly_wksp(dctx, dst, dstSize, cSrc, cSrcSize,
		//                                         workSpace, sizeof(workSpace));
		//}


		//size_t HUF_decompress4X_hufOnly_wksp(HUF_DTable* dctx, void* dst,
		//                                     size_t dstSize, void* cSrc,
		//                                     size_t cSrcSize, void* workSpace,
		//                                     size_t wkspSize)
		//{
		//    /* validation checks */
		//    if (dstSize == 0) return  ERROR( Error.dstSize_tooSmall);
		//    if (cSrcSize == 0) return  ERROR( Error.corruption_detected);

		//    {   U32  algoNb = SelectDecoder(dstSize, cSrcSize);
		//        return algoNb ? HUF_decompress4X4_DCtx_wksp(dctx, dst, dstSize, cSrc, cSrcSize, workSpace, wkspSize):
		//                        HUF_decompress4X2_DCtx_wksp(dctx, dst, dstSize, cSrc, cSrcSize, workSpace, wkspSize);
		//    }
		//}

		//size_t HUF_decompress1X_DCtx_wksp(HUF_DTable* dctx, void* dst, size_t dstSize,
		//                                  void* cSrc, size_t cSrcSize,
		//                                  void* workSpace, size_t wkspSize)
		//{
		//    /* validation checks */
		//    if (dstSize == 0) return  ERROR( Error.dstSize_tooSmall);
		//    if (cSrcSize > dstSize) return  ERROR( Error.corruption_detected);   /* invalid */
		//    if (cSrcSize == dstSize) { memcpy(dst, cSrc, dstSize); return dstSize; }   /* not compressed */
		//    if (cSrcSize == 1) { memset(dst, *( BYTE*)cSrc, dstSize); return dstSize; }   /* RLE */

		//    {   U32  algoNb = SelectDecoder(dstSize, cSrcSize);
		//        return algoNb ? HUF_decompress1X4_DCtx_wksp(dctx, dst, dstSize, cSrc,
		//                                cSrcSize, workSpace, wkspSize):
		//                        HUF_decompress1X2_DCtx_wksp(dctx, dst, dstSize, cSrc,
		//                                cSrcSize, workSpace, wkspSize);
		//    }
		//}

		//size_t HUF_decompress1X_DCtx(HUF_DTable* dctx, void* dst, size_t dstSize,
		//                             void* cSrc, size_t cSrcSize)
		//{
		//    U32 workSpace[HUF_DECOMPRESS_WORKSPACE_SIZE_U32];
		//    return HUF_decompress1X_DCtx_wksp(dctx, dst, dstSize, cSrc, cSrcSize,
		//                                      workSpace, sizeof(workSpace));
		//}


		public static size_t HUF_decompress1X_usingDTable_bmi2(BYTE* dst, size_t maxDstSize, void* cSrc, size_t cSrcSize, HUF_DTable* DTable/*, int bmi2*/)
		{
			DTableDesc dtd = GetDTableDesc(DTable);
			return dtd.tableType != 0
				? HUF_decompress1X4_usingDTable_internal_body(dst, maxDstSize, cSrc, cSrcSize, DTable /*, bmi2*/)
				: HUF_decompress1X2_usingDTable_internal_body(dst, maxDstSize, cSrc, cSrcSize, DTable /*, bmi2*/);
		}

		public static size_t HUF_decompress1X2_DCtx_wksp_bmi2(HUF_DTable* dctx, BYTE* dst, size_t dstSize, void* cSrc, size_t cSrcSize, U32* workSpace, size_t wkspSize/*, int bmi2*/)
		{
			BYTE* ip = (BYTE*)cSrc;

			size_t hSize = HUF_readDTableX2_wksp(dctx, cSrc, cSrcSize, workSpace, wkspSize);
			if (IsError(hSize)) return hSize;
			if (hSize >= cSrcSize) return ERROR(Error.srcSize_wrong);
			ip += hSize;
			cSrcSize -= hSize;

			return HUF_decompress1X2_usingDTable_internal_body(dst, dstSize, ip, cSrcSize, dctx /*, bmi2*/);
		}

		public static size_t HUF_decompress4X_usingDTable_bmi2(BYTE* dst, size_t maxDstSize, void* cSrc, size_t cSrcSize, HUF_DTable* DTable/*, int bmi2*/)
		{
			DTableDesc dtd = GetDTableDesc(DTable);
			return dtd.tableType != 0
				? HUF_decompress4X4_usingDTable_internal_body(dst, maxDstSize, cSrc, cSrcSize, DTable /*, bmi2*/)
				: HUF_decompress4X2_usingDTable_internal_body(dst, maxDstSize, cSrc, cSrcSize, DTable /*, bmi2*/);
		}

		public static size_t HUF_decompress4X_hufOnly_wksp_bmi2(HUF_DTable* dctx, BYTE* dst, size_t dstSize, void* cSrc, size_t cSrcSize, U32* workSpace, size_t wkspSize/*, int bmi2*/)
		{
			/* validation checks */
			if (dstSize == 0) return ERROR(Error.dstSize_tooSmall);
			if (cSrcSize == 0) return ERROR(Error.corruption_detected);

			{
				U32 algoNb = SelectDecoder(dstSize, cSrcSize);
				return algoNb != 0
					? HUF_decompress4X4_DCtx_wksp_bmi2(dctx, dst, dstSize, cSrc, cSrcSize, workSpace, wkspSize /*, bmi2*/)
					: HUF_decompress4X2_DCtx_wksp_bmi2(dctx, dst, dstSize, cSrc, cSrcSize, workSpace, wkspSize /*, bmi2*/);
			}
		}
	}
}
