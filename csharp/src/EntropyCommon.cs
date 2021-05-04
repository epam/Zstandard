/*
   Common functions of New Generation Entropy library
   Copyright (C) 2016, Yann Collet.

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
*************************************************************************** */

using System.Diagnostics;
using System.Runtime.InteropServices;
using BYTE = System.Byte;
using size_t = System.UInt32;
using U16 = System.UInt16;
using U32 = System.UInt32;
using U64 = System.UInt64;
using S16 = System.Int16;

using FSE_DTable = System.UInt32;

using static EPAM.Deltix.ZStd.Mem;
using static EPAM.Deltix.ZStd.ZStdErrors;

namespace EPAM.Deltix.ZStd
{
	internal static unsafe class EntropyCommon
	{
		///* *************************************
		//*  Dependencies
		//***************************************/
		//#include "mem.h"
		//#include "error_private.h"       /* ERR_*, ERROR */
		//#define FSE_STATIC_LINKING_ONLY  /* FSE_MIN_TABLELOG */
		//#include "fse.h"
		//#define HUF_STATIC_LINKING_ONLY  /* HUF_TABLELOG_ABSOLUTEMAX */
		//#include "huf.h"


		///*===   Version   ===*/
		//unsigned VersionNumber(void) { return FSE_VERSION_NUMBER; }


		///*===   Error Management   ===*/
		//unsigned IsError(size_t code) { return ERR_isError(code); }
		// char* GetErrorName(size_t code) { return ERR_getErrorName(code); }

		//unsigned IsError(size_t code) { return ERR_isError(code); }
		// char* GetErrorName(size_t code) { return ERR_getErrorName(code); }


		/*-**************************************************************
		*  FSE NCount encoding-decoding
		****************************************************************/
		public static size_t ReadNCount(short[] normalizedCounter, uint* maxSVPtr, uint* tableLogPtr, void* headerBuffer, size_t hbSize)
		{
			BYTE* istart = (BYTE*)headerBuffer;
			BYTE* iend = istart + hbSize;
			BYTE* ip = istart;
			int nbBits;
			int remaining;
			int threshold;
			U32 bitStream;
			int bitCount;
			uint charnum = 0;
			int previous0 = 0;

			if (hbSize < 4) return ERROR(Error.srcSize_wrong);
			bitStream = MEM_readLE32(ip);
			nbBits = (int)(bitStream & 0xF) + Fse.FSE_MIN_TABLELOG;   /* extract tableLog */
			if (nbBits > Fse.FSE_TABLELOG_ABSOLUTE_MAX) return ERROR(Error.tableLog_tooLarge);
			bitStream >>= 4;
			bitCount = 4;
			*tableLogPtr = (size_t)nbBits;
			remaining = (1 << nbBits) + 1;
			threshold = 1 << nbBits;
			nbBits++;

			while ((remaining > 1) & (charnum <= *maxSVPtr))
			{
				if (previous0 != 0)
				{
					uint n0 = charnum;
					while ((bitStream & 0xFFFF) == 0xFFFF)
					{
						n0 += 24;
						if (ip < iend - 5)
						{
							ip += 2;
							bitStream = MEM_readLE32(ip) >> bitCount;
						}
						else
						{
							bitStream >>= 16;
							bitCount += 16;
						}
					}
					while ((bitStream & 3) == 3)
					{
						n0 += 3;
						bitStream >>= 2;
						bitCount += 2;
					}
					n0 += bitStream & 3;
					bitCount += 2;
					if (n0 > *maxSVPtr) return ERROR(Error.maxSymbolValue_tooSmall);
					while (charnum < n0) normalizedCounter[charnum++] = 0;
					if ((ip <= iend - 7) || (ip + (bitCount >> 3) <= iend - 4))
					{
						ip += bitCount >> 3;
						bitCount &= 7;
						bitStream = MEM_readLE32(ip) >> bitCount;
					}
					else
					{
						bitStream >>= 2;
					}
				}
				{
					int max = (2 * threshold - 1) - remaining;
					int count;

					if ((bitStream & (threshold - 1)) < (U32)max)
					{
						count = (int)bitStream & (threshold - 1);
						bitCount += nbBits - 1;
					}
					else
					{
						count = (int)bitStream & (2 * threshold - 1);
						if (count >= threshold) count -= max;
						bitCount += nbBits;
					}

					count--;   /* extra accuracy */
					remaining -= count < 0 ? -count : count;   /* -1 means +1 */
					normalizedCounter[charnum++] = (short)count;
					previous0 = count == 0 ? 1 : 0;
					while (remaining < threshold)
					{
						nbBits--;
						threshold >>= 1;
					}

					if ((ip <= iend - 7) || (ip + (bitCount >> 3) <= iend - 4))
					{
						ip += bitCount >> 3;
						bitCount &= 7;
					}
					else
					{
						bitCount -= (int)(8 * (iend - 4 - ip));
						ip = iend - 4;
					}
					bitStream = MEM_readLE32(ip) >> (bitCount & 31);
				}
			}   /* while ((remaining>1) & (charnum<=*maxSVPtr)) */
			if (remaining != 1) return ERROR(Error.corruption_detected);
			if (bitCount > 32) return ERROR(Error.corruption_detected);
			*maxSVPtr = charnum - 1;

			ip += (bitCount + 7) >> 3;
			return (size_t)(ip - istart);
		}


		/*! ReadStats() :
		    Read compact Huffman tree, saved by WriteCTable().
		    `huffWeight` is destination buffer.
		    `rankStats` is assumed to be a table of at least HUF_TABLELOG_MAX U32.
		    @return : size read from `src` , or an error Code .
		    Note : Needed by ReadCTable() and HUF_readDTableX?() .
		*/
		public static size_t ReadStats(BYTE* huffWeight, size_t hwSize, U32* rankStats, U32* nbSymbolsPtr, U32* tableLogPtr, void* src, size_t srcSize)
		{
			U32 weightTotal;
			BYTE* ip = (BYTE*)src;
			size_t iSize;
			size_t oSize;

			if (srcSize == 0) return ERROR(Error.srcSize_wrong);
			iSize = ip[0];
			/* memset(huffWeight, 0, hwSize);   *//* is not necessary, even though some analyzer complain ... */

			if (iSize >= 128)
			{  /* special header */
				oSize = iSize - 127;
				iSize = ((oSize + 1) / 2);
				if (iSize + 1 > srcSize) return ERROR(Error.srcSize_wrong);
				if (oSize >= hwSize) return ERROR(Error.corruption_detected);
				ip += 1;
				{
					U32 n;
					for (n = 0; n < oSize; n += 2)
					{
						huffWeight[n] = (BYTE)(ip[n / 2] >> 4);
						huffWeight[n + 1] = (BYTE)(ip[n / 2] & 15);
					}
				}
			}
			else
			{   /* header compressed with FSE (normal case) */
				FSE_DTable* fseWorkspace = stackalloc FSE_DTable[Fse.FSE_DTABLE_SIZE_U32(6)];  /* 6 is max possible tableLog for HUF header (maybe even 5, to be tested) */
				if (iSize + 1 > srcSize) return ERROR(Error.srcSize_wrong);
				oSize = FseDecompress.FSE_decompress_wksp(huffWeight, hwSize - 1, ip + 1, iSize, fseWorkspace, 6);   /* max (hwSize-1) values decoded, as last one is implied */
				if (IsError(oSize)) return oSize;
			}

			/* collect weight stats */
			memset(rankStats, 0, (Huf.HUF_TABLELOG_MAX + 1) * sizeof(U32));
			weightTotal = 0;
			{
				U32 n; for (n = 0; n < oSize; n++)
				{
					if (huffWeight[n] >= Huf.HUF_TABLELOG_MAX) return ERROR(Error.corruption_detected);
					rankStats[huffWeight[n]]++;
					weightTotal += ((U32)1 << huffWeight[n]) >> 1;
				}
			}
			if (weightTotal == 0) return ERROR(Error.corruption_detected);

			/* get last non-null symbol weight (implied, total must be 2^n) */
			{
				U32 tableLog = BitStream.BIT_highbit32(weightTotal) + 1;
				if (tableLog > Huf.HUF_TABLELOG_MAX) return ERROR(Error.corruption_detected);
				*tableLogPtr = tableLog;
				/* determine last weight */
				{
					U32 total = (U32)1 << (int)tableLog;
					U32 rest = total - weightTotal;
					U32 verif = (U32)1 << (int)BitStream.BIT_highbit32(rest);
					U32 lastWeight = BitStream.BIT_highbit32(rest) + 1;
					if (verif != rest) return ERROR(Error.corruption_detected);    /* last value must be a clean power of 2 */
					huffWeight[oSize] = (BYTE)lastWeight;
					rankStats[lastWeight]++;
				}
			}

			/* check tree construction validity */
			if ((rankStats[1] < 2) || ((rankStats[1] & 1) != 0)) return ERROR(Error.corruption_detected);   /* by construction : at least 2 elts of rank 1, must be even */

			/* results */
			*nbSymbolsPtr = (U32)(oSize + 1);
			return iSize + 1;
		}
	}
}
