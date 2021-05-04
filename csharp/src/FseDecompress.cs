/* ******************************************************************
   FSE : Finite State Entropy decoder
   Copyright (C) 2013-2015, Yann Collet.

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
    - FSE source repository : https://github.com/Cyan4973/FiniteStateEntropy
    - Public forum : https://groups.google.com/forum/#!forum/lz4c
****************************************************************** */
using BYTE = System.Byte;
using size_t = System.UInt32;
using U16 = System.UInt16;
using U32 = System.UInt32;
using U64 = System.UInt64;
using S16 = System.Int16;

using FSE_DTable = System.UInt32;
using FSE_FUNCTION_TYPE = System.Byte;
using FSE_DECODE_TYPE = EPAM.Deltix.ZStd.Fse.FSE_decode_t;

using static EPAM.Deltix.ZStd.BitStream;
using static EPAM.Deltix.ZStd.Fse;
using static EPAM.Deltix.ZStd.ZStdErrors;
using static EPAM.Deltix.ZStd.EntropyCommon;

namespace EPAM.Deltix.ZStd
{
	internal static unsafe class FseDecompress
	{
		///* **************************************************************
		//*  Includes
		//****************************************************************/
		//#include <stdlib.h>     /* malloc, free, qsort */
		//#include <string.h>     /* memcpy, memset */
		//#include "bitstream.h"
		//#include "compiler.h"
		//#define FSE_STATIC_LINKING_ONLY
		//#include "fse.h"
		//#include "error_private.h"


		///* **************************************************************
		//*  Error Management
		//****************************************************************/
		//#define FSE_isError ERR_isError
		//#define FSE_STATIC_ASSERT(c) { enum { FSE_static_assert = 1/(int)(!!(c)) }; }   /* use only *after* variable declarations */

		///* check and forward error code */
		//#define CHECK_F(f) { size_t  e = f; if (IsError(e)) return e; }


		///* **************************************************************
		//*  Templates
		//****************************************************************/
		///*
		//  designed to be included
		//  for type-specific functions (template emulation in C)
		//  Objective is to write these functions only once, for improved maintenance
		//*/

		///* safety checks */
		//#ifndef FSE_FUNCTION_EXTENSION
		//#  error "FSE_FUNCTION_EXTENSION must be defined"
		//#endif
		//#ifndef FSE_FUNCTION_TYPE
		//#  error "FSE_FUNCTION_TYPE must be defined"
		//#endif

		///* Function names */
		//#define FSE_CAT(X,Y) X##Y
		//#define FSE_FUNCTION_NAME(X,Y) FSE_CAT(X,Y)
		//#define FSE_TYPE_NAME(X,Y) FSE_CAT(X,Y)


		///* Function templates */
		//FSE_DTable* CreateDTable(unsigned tableLog)
		//{
		//    if (tableLog > FSE_TABLELOG_ABSOLUTE_MAX) tableLog = FSE_TABLELOG_ABSOLUTE_MAX;
		//    return (FSE_DTable*)malloc( FSE_DTABLE_SIZE_U32(tableLog) * sizeof (U32) );
		//}

		//void FreeDTable(FSE_DTable* dt)
		//{
		//    free(dt);
		//}

		public static size_t BuildDTable(FSE_DTable* dt, short[] normalizedCounter, uint maxSymbolValue, uint tableLog)
		{
			void* tdPtr = dt + 1;   /* because *dt is unsigned, 32-bits aligned on 32-bits */
			FSE_DECODE_TYPE* tableDecode = (FSE_DECODE_TYPE*)(tdPtr);
			U16[] symbolNext = new U16[Fse.FSE_MAX_SYMBOL_VALUE + 1];

			U32 maxSV1 = maxSymbolValue + 1;
			U32 tableSize = (U32)1 << (int)tableLog;
			U32 highThreshold = tableSize - 1;

			/* Sanity Checks */
			if (maxSymbolValue > FSE_MAX_SYMBOL_VALUE) return ERROR(Error.maxSymbolValue_tooLarge);
			if (tableLog > FSE_MAX_TABLELOG) return ERROR(Error.tableLog_tooLarge);

			/* Init, lay down lowprob symbols */
			{
				FSE_DTableHeader DTableH;
				DTableH.tableLog = (U16)tableLog;
				DTableH.fastMode = 1;
				{
					S16 largeLimit = (S16)(1 << (int)(tableLog - 1));
					U32 s;
					for (s = 0; s < maxSV1; s++)
					{
						if (normalizedCounter[s] == -1)
						{
							tableDecode[highThreshold--].symbol = (FSE_FUNCTION_TYPE)s;
							symbolNext[s] = 1;
						}
						else
						{
							if (normalizedCounter[s] >= largeLimit) DTableH.fastMode = 0;
							symbolNext[s] = (U16)normalizedCounter[s];
						}
					}
				}
				*(FSE_DTableHeader*)dt = DTableH; // memcpy(dt, &DTableH, sizeof(DTableH));
			}

			/* Spread symbols */
			{
				U32 tableMask = tableSize - 1;
				U32 step = FSE_TABLESTEP(tableSize);
				U32 s, position = 0;
				for (s = 0; s < maxSV1; s++)
				{
					int i;
					for (i = 0; i < normalizedCounter[s]; i++)
					{
						tableDecode[position].symbol = (FSE_FUNCTION_TYPE)s;
						position = (position + step) & tableMask;
						while (position > highThreshold) position = (position + step) & tableMask;   /* lowprob area */
					}
				}
				if (position != 0) return ERROR(Error.GENERIC);   /* position must reach all cells once, otherwise normalizedCounter is incorrect */
			}

			/* Build Decoding table */
			{
				U32 u;
				for (u = 0; u < tableSize; u++)
				{
					FSE_FUNCTION_TYPE symbol = (FSE_FUNCTION_TYPE)(tableDecode[u].symbol);
					U32 nextState = symbolNext[symbol]++;
					tableDecode[u].nbBits = (BYTE)(tableLog - BIT_highbit32(nextState));
					tableDecode[u].newState = (U16)((nextState << tableDecode[u].nbBits) - tableSize);
				}
			}

			return 0;
		}


		//#ifndef FSE_COMMONDEFS_ONLY

		///*-*******************************************************
		//*  Decompression (Byte symbols)
		//*********************************************************/
		//size_t FSE_buildDTable_rle (FSE_DTable* dt, BYTE symbolValue)
		//{
		//    void* ptr = dt;
		//    FSE_DTableHeader*  DTableH = (FSE_DTableHeader*)ptr;
		//    void* dPtr = dt + 1;
		//    FSE_decode_t*  cell = (FSE_decode_t*)dPtr;

		//    DTableH->tableLog = 0;
		//    DTableH->fastMode = 0;

		//    cell->newState = 0;
		//    cell->symbol = symbolValue;
		//    cell->nbBits = 0;

		//    return 0;
		//}


		//size_t FSE_buildDTable_raw (FSE_DTable* dt, unsigned nbBits)
		//{
		//    void* ptr = dt;
		//    FSE_DTableHeader*  DTableH = (FSE_DTableHeader*)ptr;
		//    void* dPtr = dt + 1;
		//    FSE_decode_t*  dinfo = (FSE_decode_t*)dPtr;
		//     unsigned tableSize = 1 << nbBits;
		//     unsigned tableMask = tableSize - 1;
		//     unsigned maxSV1 = tableMask+1;
		//    unsigned s;

		//    /* Sanity checks */
		//    if (nbBits < 1) return ERROR( Error.GENERIC);         /* min size */

		//    /* Build Decoding Table */
		//    DTableH->tableLog = (U16)nbBits;
		//    DTableH->fastMode = 1;
		//    for (s=0; s<maxSV1; s++) {
		//        dinfo[s].newState = 0;
		//        dinfo[s].symbol = (BYTE)s;
		//        dinfo[s].nbBits = (BYTE)nbBits;
		//    }

		//    return 0;
		//}

		public static size_t FSE_decompress_usingDTable_generic(void* dst, size_t maxDstSize, void* cSrc, size_t cSrcSize, FSE_DTable* dt, uint fast)
		{
			BYTE* ostart = (BYTE*)dst;
			BYTE* op = ostart;
			BYTE* omax = op + maxDstSize;
			BYTE* olimit = omax - 3;

			BIT_DStream_t bitD = new BIT_DStream_t();
			FSE_DState_t state1 = new FSE_DState_t();
			FSE_DState_t state2 = new FSE_DState_t();

			/* Init */
			{ size_t errcod = InitDStream(bitD, cSrc, cSrcSize); if (IsError(errcod)) return errcod; }

			Fse.InitDState(state1, bitD, dt);
			Fse.InitDState(state2, bitD, dt);

			//#define FSE_GETSYMBOL(statePtr) fast ? DecodeSymbolFast(statePtr, &bitD) : DecodeSymbol(statePtr, &bitD)

			/* 4 symbols per loop */
			for (; (ReloadDStream(bitD) == BIT_DStream_status.BIT_DStream_unfinished) & (op < olimit); op += 4)
			{
				op[0] = fast != 0 ? DecodeSymbolFast(state1, bitD) : DecodeSymbol(state1, bitD);

				//if (FSE_MAX_TABLELOG * 2 + 7 > sizeOfBitContainer * 8)    /* This test must be static */
				//	ReloadDStream(bitD);

				op[1] = fast != 0 ? DecodeSymbolFast(state2, bitD) : DecodeSymbol(state2, bitD);

				if (FSE_MAX_TABLELOG * 4 + 7 > sizeOfBitContainer * 8)    /* This test must be static */
				{ if (ReloadDStream(bitD) > BIT_DStream_status.BIT_DStream_unfinished) { op += 2; break; } }

				op[2] = fast != 0 ? DecodeSymbolFast(state1, bitD) : DecodeSymbol(state1, bitD);

				//if (FSE_MAX_TABLELOG * 2 + 7 > sizeOfBitContainer * 8)    /* This test must be static */
				//	ReloadDStream(bitD);

				op[3] = fast != 0 ? DecodeSymbolFast(state2, bitD) : DecodeSymbol(state2, bitD);
			}

			/* tail */
			/* note : ReloadDStream(&bitD) >= FSE_DStream_partiallyFilled; Ends at exactly BIT_DStream_completed */
			while (true)
			{
				if (op > (omax - 2)) return ERROR(Error.dstSize_tooSmall);
				*op++ = fast != 0 ? DecodeSymbolFast(state1, bitD) : DecodeSymbol(state1, bitD);
				if (ReloadDStream(bitD) == BIT_DStream_status.BIT_DStream_overflow)
				{
					*op++ = fast != 0 ? DecodeSymbolFast(state2, bitD) : DecodeSymbol(state2, bitD);
					break;
				}

				if (op > (omax - 2)) return ERROR(Error.dstSize_tooSmall);
				*op++ = fast != 0 ? DecodeSymbolFast(state2, bitD) : DecodeSymbol(state2, bitD);
				if (ReloadDStream(bitD) == BIT_DStream_status.BIT_DStream_overflow)
				{
					*op++ = fast != 0 ? DecodeSymbolFast(state1, bitD) : DecodeSymbol(state1, bitD);
					break;
				}
			}

			return (size_t)(op - ostart);
		}


		public static size_t FSE_decompress_usingDTable(void* dst, size_t originalSize, void* cSrc, size_t cSrcSize, FSE_DTable* dt)
		{
			void* ptr = dt;
			FSE_DTableHeader* DTableH = (FSE_DTableHeader*)ptr;
			U32 fastMode = DTableH->fastMode;

			/* select fast mode (static) */
			if (fastMode != 0) return FSE_decompress_usingDTable_generic(dst, originalSize, cSrc, cSrcSize, dt, 1);
			return FSE_decompress_usingDTable_generic(dst, originalSize, cSrc, cSrcSize, dt, 0);
		}


		public static size_t FSE_decompress_wksp(void* dst, size_t dstCapacity, void* cSrc, size_t cSrcSize, FSE_DTable* workSpace, uint maxLog)
		{
			BYTE* istart = (BYTE*)cSrc;
			BYTE* ip = istart;
			short[] counting = new short[Fse.FSE_MAX_SYMBOL_VALUE + 1];
			uint tableLog;
			uint maxSymbolValue = FSE_MAX_SYMBOL_VALUE;

			/* normal FSE decoding mode */
			size_t NCountLength = ReadNCount(counting, &maxSymbolValue, &tableLog, istart, cSrcSize);
			if (IsError(NCountLength)) return NCountLength;
			//if (NCountLength >= cSrcSize) return ERROR( Error.srcSize_wrong);   /* too small input size; supposed to be already checked in NCountLength, only remaining case : NCountLength==cSrcSize */
			if (tableLog > maxLog) return ERROR(Error.tableLog_tooLarge);
			ip += NCountLength;
			cSrcSize -= NCountLength;

			{
				size_t errcod = BuildDTable(workSpace, counting, maxSymbolValue, tableLog);
				if (IsError(errcod)) return errcod;
			}

			return FSE_decompress_usingDTable(dst, dstCapacity, ip, cSrcSize, workSpace); /* always return, even if it is an error code */
		}


		//typedef FSE_DTable DTable_max_t[FSE_DTABLE_SIZE_U32(FSE_MAX_TABLELOG)];

		//size_t Decompress(void* dst, size_t dstCapacity,  void* cSrc, size_t cSrcSize)
		//{
		//    DTable_max_t dt;   /* Static analyzer seems unable to understand this table will be properly initialized later */
		//    return FSE_decompress_wksp(dst, dstCapacity, cSrc, cSrcSize, dt, FSE_MAX_TABLELOG);
		//}



		//#endif   /* FSE_COMMONDEFS_ONLY */
	}
}
