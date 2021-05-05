/* ******************************************************************
   bitstream
   Part of FSE library
   header file (to include)
   Copyright (C) 2013-2017, Yann Collet.

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
   - Source repository : https://github.com/Cyan4973/FiniteStateEntropy
****************************************************************** */

using System.Diagnostics;
using System.Runtime.InteropServices;
using BYTE = System.Byte;
using size_t = System.UInt32;
using U16 = System.UInt16;
using U32 = System.UInt32;
using U64 = System.UInt64;
using S16 = System.Int16;

using static EPAM.Deltix.ZStd.Mem;
using static EPAM.Deltix.ZStd.ZStdErrors;

namespace EPAM.Deltix.ZStd
{
	internal static unsafe class BitStream
	{
		//#ifndef BITSTREAM_H_MODULE
		//#define BITSTREAM_H_MODULE

		//#if defined (__cplusplus)
		//extern "C" {
		//#endif

		///*
		//*  This API consists of small unitary functions, which must be inlined for best performance.
		//*  Since link-time-optimization is not available for all compilers,
		//*  these functions are defined into a .h to be included.
		//*/

		///*-****************************************
		//*  Dependencies
		//******************************************/
		//#include "mem.h"            /* unaligned access routines */
		//#include "error_private.h"  /* error codes and messages */


		///*-*************************************
		//*  Debug
		//***************************************/
		//#if defined(BIT_DEBUG) && (BIT_DEBUG>=1)
		//#  include <Debug.Assert.h>
		//#else
		//#  ifndef Debug.Assert
		//#    define Debug.Assert(condition) ((void)0)
		//#  endif
		//#endif


		///*=========================================
		//*  Target specific
		//=========================================*/
		//#if defined(__BMI__) && defined(__GNUC__)
		//#  include <immintrin.h>   /* support for bextr (experimental) */
		//#endif

		public const U32 STREAM_ACCUMULATOR_MIN_32 = 25;
		public const U32 STREAM_ACCUMULATOR_MIN_64 = 57;
		public static U32 STREAM_ACCUMULATOR_MIN = ((U32)(MEM_32bits() ? STREAM_ACCUMULATOR_MIN_32 : STREAM_ACCUMULATOR_MIN_64));


		///*-******************************************
		//*  bitStream encoding API (write forward)
		//********************************************/
		///* bitStream can mix input from multiple sources.
		// * A critical property of these streams is that they encode and decode in **reverse** direction.
		// * So the first bit sequence you add will be the last to be read, like a LIFO stack.
		// */
		//typedef struct
		//{
		//    size_t bitContainer;
		//    unsigned bitPos;
		//    char*  startPtr;
		//    char*  ptr;
		//    char*  endPtr;
		//} BIT_CStream_t;

		//MEM_STATIC size_t InitCStream(BIT_CStream_t* bitC, void* dstBuffer, size_t dstCapacity);
		//MEM_STATIC void   AddBits(BIT_CStream_t* bitC, size_t value, unsigned nbBits);
		//MEM_STATIC void   FlushBits(BIT_CStream_t* bitC);
		//MEM_STATIC size_t CloseCStream(BIT_CStream_t* bitC);

		///* Start with initCStream, providing the size of buffer to write into.
		//*  bitStream will never write outside of this buffer.
		//*  `dstCapacity` must be >= sizeof(bitD->bitContainer), otherwise @return will be an error code.
		//*
		//*  bits are first added to a local register.
		//*  Local register is size_t, hence 64-bits on 64-bits systems, or 32-bits on 32-bits systems.
		//*  Writing data into memory is an explicit operation, performed by the flushBits function.
		//*  Hence keep track how many bits are potentially stored into local register to avoid register overflow.
		//*  After a flushBits, a maximum of 7 bits might still be stored into local register.
		//*
		//*  Avoid storing elements of more than 24 bits if you want compatibility with 32-bits bitstream readers.
		//*
		//*  Last operation is to close the bitStream.
		//*  The function returns the final size of CStream in bytes.
		//*  If data couldn't fit into `dstBuffer`, it will return a 0 ( == not storable)
		//*/


		///*-********************************************
		//*  bitStream decoding API (read backward)
		//**********************************************/
		public class BIT_DStream_t
		{
			public void Reset()
			{
				bitContainer = 0;
				bitsConsumed = 0;
				ptr = null;
				start = null;
				limitPtr = null;
			}

			public size_t bitContainer;
			public uint bitsConsumed;
			public sbyte* ptr;
			public sbyte* start;
			public sbyte* limitPtr;
		}

		public enum BIT_DStream_status : int
		{
			BIT_DStream_unfinished = 0,
			BIT_DStream_endOfBuffer = 1,
			BIT_DStream_completed = 2,
			BIT_DStream_overflow = 3
		}  /* result of ReloadDStream() */
		   /* 1,2,4,8 would be better for bitmap combinations, but slows down performance a bit ... :( */

		//MEM_STATIC size_t   InitDStream(BIT_DStream_t* bitD, void* srcBuffer, size_t srcSize);
		//MEM_STATIC size_t   ReadBits(BIT_DStream_t* bitD, unsigned nbBits);
		//MEM_STATIC BIT_DStream_status ReloadDStream(BIT_DStream_t* bitD);
		//MEM_STATIC unsigned EndOfDStream( BIT_DStream_t* bitD);


		///* Start by invoking InitDStream().
		//*  A chunk of the bitStream is then stored into a local register.
		//*  Local register size is 64-bits on 64-bits systems, 32-bits on 32-bits systems (size_t).
		//*  You can then retrieve bitFields stored into the local register, **in reverse order**.
		//*  Local register is explicitly reloaded from memory by the ReloadDStream() method.
		//*  A reload guarantee a minimum of ((8*sizeof(bitD->bitContainer))-7) bits when its result is BIT_DStream_unfinished.
		//*  Otherwise, it can be less than that, so proceed accordingly.
		//*  Checking if DStream has reached its end can be performed with EndOfDStream().
		//*/


		///*-****************************************
		//*  unsafe API
		//******************************************/
		//MEM_STATIC void AddBitsFast(BIT_CStream_t* bitC, size_t value, unsigned nbBits);
		///* faster, but works only if value is "clean", meaning all high bits above nbBits are 0 */

		//MEM_STATIC void FlushBitsFast(BIT_CStream_t* bitC);
		///* unsafe version; does not check buffer overflow */

		//MEM_STATIC size_t ReadBitsFast(BIT_DStream_t* bitD, unsigned nbBits);
		///* faster, but works only if nbBits >= 1 */



		/*-**************************************************************
		*  Internal functions
		****************************************************************/
		private readonly static uint[] DeBruijnClz = new uint[32] {
			0,  9,  1, 10, 13, 21,  2, 29,
			11, 14, 16, 18, 22, 25,  3, 30,
			8, 12, 20, 28, 15, 17, 24,  7,
			19, 27, 23,  6, 26,  5,  4, 31 };

		public static uint BIT_highbit32(U32 val)
		{
			Debug.Assert(val != 0);
			U32 v = val;
			v |= v >> 1;
			v |= v >> 2;
			v |= v >> 4;
			v |= v >> 8;
			v |= v >> 16;
			return DeBruijnClz[(U32)(v * 0x07C4ACDDU) >> 27];
		}

		///*=====    Local Constants   =====*/
		//static readonly unsigned BIT_mask[] = {
		//    0,          1,         3,         7,         0xF,       0x1F,
		//    0x3F,       0x7F,      0xFF,      0x1FF,     0x3FF,     0x7FF,
		//    0xFFF,      0x1FFF,    0x3FFF,    0x7FFF,    0xFFFF,    0x1FFFF,
		//    0x3FFFF,    0x7FFFF,   0xFFFFF,   0x1FFFFF,  0x3FFFFF,  0x7FFFFF,
		//    0xFFFFFF,   0x1FFFFFF, 0x3FFFFFF, 0x7FFFFFF, 0xFFFFFFF, 0x1FFFFFFF,
		//    0x3FFFFFFF, 0x7FFFFFFF}; /* up to 31 bits */
		//#define BIT_MASK_SIZE (sizeof(BIT_mask) / sizeof(BIT_mask[0]))

		///*-**************************************************************
		//*  bitStream encoding
		//****************************************************************/
		///*! InitCStream() :
		// *  `dstCapacity` must be > sizeof(size_t)
		// *  @return : 0 if success,
		// *            otherwise an error code (can be tested using ERR_isError()) */
		//MEM_STATIC size_t InitCStream(BIT_CStream_t* bitC,
		//                                  void* startPtr, size_t dstCapacity)
		//{
		//    bitC->bitContainer = 0;
		//    bitC->bitPos = 0;
		//    bitC->startPtr = (char*)startPtr;
		//    bitC->ptr = bitC->startPtr;
		//    bitC->endPtr = bitC->startPtr + dstCapacity - sizeof(bitC->bitContainer);
		//    if (dstCapacity <= sizeof(bitC->bitContainer)) return  ERROR( Error.dstSize_tooSmall);
		//    return 0;
		//}

		///*! AddBits() :
		// *  can add up to 31 bits into `bitC`.
		// *  Note : does not check for register overflow ! */
		//MEM_STATIC void AddBits(BIT_CStream_t* bitC,
		//                            size_t value, unsigned nbBits)
		//{
		//    MEM_STATIC_ASSERT(BIT_MASK_SIZE == 32);
		//    Debug.Assert(nbBits < BIT_MASK_SIZE);
		//    Debug.Assert(nbBits + bitC->bitPos < sizeof(bitC->bitContainer) * 8);
		//    bitC->bitContainer |= (value & BIT_mask[nbBits]) << bitC->bitPos;
		//    bitC->bitPos += nbBits;
		//}

		///*! AddBitsFast() :
		// *  works only if `value` is _clean_, meaning all high bits above nbBits are 0 */
		//MEM_STATIC void AddBitsFast(BIT_CStream_t* bitC,
		//                                size_t value, unsigned nbBits)
		//{
		//    Debug.Assert((value>>nbBits) == 0);
		//    Debug.Assert(nbBits + bitC->bitPos < sizeof(bitC->bitContainer) * 8);
		//    bitC->bitContainer |= value << bitC->bitPos;
		//    bitC->bitPos += nbBits;
		//}

		///*! FlushBitsFast() :
		// *  assumption : bitContainer has not overflowed
		// *  unsafe version; does not check buffer overflow */
		//MEM_STATIC void FlushBitsFast(BIT_CStream_t* bitC)
		//{
		//    size_t  nbBytes = bitC->bitPos >> 3;
		//    Debug.Assert(bitC->bitPos < sizeof(bitC->bitContainer) * 8);
		//    MEM_writeLEST(bitC->ptr, bitC->bitContainer);
		//    bitC->ptr += nbBytes;
		//    Debug.Assert(bitC->ptr <= bitC->endPtr);
		//    bitC->bitPos &= 7;
		//    bitC->bitContainer >>= nbBytes*8;
		//}

		///*! FlushBits() :
		// *  assumption : bitContainer has not overflowed
		// *  safe version; check for buffer overflow, and prevents it.
		// *  note : does not signal buffer overflow.
		// *  overflow will be revealed later on using CloseCStream() */
		//MEM_STATIC void FlushBits(BIT_CStream_t* bitC)
		//{
		//    size_t  nbBytes = bitC->bitPos >> 3;
		//    Debug.Assert(bitC->bitPos < sizeof(bitC->bitContainer) * 8);
		//    MEM_writeLEST(bitC->ptr, bitC->bitContainer);
		//    bitC->ptr += nbBytes;
		//    if (bitC->ptr > bitC->endPtr) bitC->ptr = bitC->endPtr;
		//    bitC->bitPos &= 7;
		//    bitC->bitContainer >>= nbBytes*8;
		//}

		///*! CloseCStream() :
		// *  @return : size of CStream, in bytes,
		// *            or 0 if it could not fit into dstBuffer */
		//MEM_STATIC size_t CloseCStream(BIT_CStream_t* bitC)
		//{
		//    AddBitsFast(bitC, 1, 1);   /* endMark */
		//    FlushBits(bitC);
		//    if (bitC->ptr >= bitC->endPtr) return 0; /* overflow detected */
		//    return (bitC->ptr - bitC->startPtr) + (bitC->bitPos > 0);
		//}

		public const uint sizeOfBitContainer = 4; //sizeof(BIT_DStream_t.bitContainer)

		/*-********************************************************
		*  bitStream decoding
		**********************************************************/
		/*! InitDStream() :
		 *  Initialize a BIT_DStream_t.
		 * `bitD` : a pointer to an already allocated BIT_DStream_t structure.
		 * `srcSize` must be the *exact* size of the bitStream, in bytes.
		 * @return : size of stream (== srcSize), or an errorCode if a problem is detected
		 */
		public static size_t InitDStream(BIT_DStream_t bitD, void* srcBuffer, size_t srcSize)
		{
			if (srcSize < 1)
			{
				bitD.Reset(); return ERROR(Error.srcSize_wrong);
			}

			bitD.start = (sbyte*)srcBuffer;
			bitD.limitPtr = bitD.start + sizeOfBitContainer;

			if (srcSize >= sizeOfBitContainer)
			{  /* normal case */
				bitD.ptr = (sbyte*)srcBuffer + srcSize - sizeOfBitContainer;
				bitD.bitContainer = MEM_readLEST(bitD.ptr);
				{
					BYTE lastByte = ((BYTE*)srcBuffer)[srcSize - 1];
					bitD.bitsConsumed = lastByte != 0 ? 8 - BIT_highbit32(lastByte) : 0;  /* ensures bitsConsumed is always set */
					if (lastByte == 0) return ERROR(Error.GENERIC); /* endMark not present */
				}
			}
			else
			{
				bitD.ptr = bitD.start;
				bitD.bitContainer = *(BYTE*)(bitD.start);
				{
					if (srcSize >= 7) bitD.bitContainer += (size_t)(((BYTE*)(srcBuffer))[6]) << (int)(sizeOfBitContainer * 8 - 16);
					/* fall-through */

					if (srcSize >= 6) bitD.bitContainer += (size_t)(((BYTE*)(srcBuffer))[5]) << (int)(sizeOfBitContainer * 8 - 24);
					/* fall-through */

					if (srcSize >= 5) bitD.bitContainer += (size_t)(((BYTE*)(srcBuffer))[4]) << (int)(sizeOfBitContainer * 8 - 32);
					/* fall-through */

					if (srcSize >= 4) bitD.bitContainer += (size_t)(((BYTE*)(srcBuffer))[3]) << 24;
					/* fall-through */

					if (srcSize >= 3) bitD.bitContainer += (size_t)(((BYTE*)(srcBuffer))[2]) << 16;
					/* fall-through */

					if (srcSize >= 2) bitD.bitContainer += (size_t)(((BYTE*)(srcBuffer))[1]) << 8;
					/* fall-through */

					/*default: break;*/
				}


				{
					BYTE lastByte = ((BYTE*)srcBuffer)[srcSize - 1];
					bitD.bitsConsumed = lastByte != 0 ? 8 - BIT_highbit32(lastByte) : 0;
					if (lastByte == 0) return ERROR(Error.corruption_detected);  /* endMark not present */
				}
				bitD.bitsConsumed += (U32)(sizeOfBitContainer - srcSize) * 8;
			}

			return srcSize;
		}

		//MEM_STATIC size_t GetUpperBits(size_t bitContainer, U32  start)
		//{
		//    return bitContainer >> start;
		//}

		//MEM_STATIC size_t GetMiddleBits(size_t bitContainer, U32  start, U32  nbBits)
		//{
		//#if defined(__BMI__) && defined(__GNUC__) && __GNUC__*1000+__GNUC_MINOR__ >= 4008  /* experimental */
		//#  if defined(__x86_64__)
		//    if (sizeof(bitContainer)==8)
		//        return _bextr_u64(bitContainer, start, nbBits);
		//    else
		//#  endif
		//        return _bextr_u32(bitContainer, start, nbBits);
		//#else
		//    Debug.Assert(nbBits < BIT_MASK_SIZE);
		//    return (bitContainer >> start) & BIT_mask[nbBits];
		//#endif
		//}

		//MEM_STATIC size_t GetLowerBits(size_t bitContainer, U32  nbBits)
		//{
		//    Debug.Assert(nbBits < BIT_MASK_SIZE);
		//    return bitContainer & BIT_mask[nbBits];
		//}

		/*! LookBits() :
		 *  Provides next n bits from local register.
		 *  local register is not modified.
		 *  On 32-bits, maxNbBits==24.
		 *  On 64-bits, maxNbBits==56.
		 * @return : value extracted */
		public static size_t LookBits(BIT_DStream_t bitD, U32 nbBits)
		{
			U32 regMask = sizeOfBitContainer * 8 - 1;
			return ((bitD.bitContainer << (int)(bitD.bitsConsumed & regMask)) >> 1) >> (int)((regMask - nbBits) & regMask);
		}

		/*! LookBitsFast() :
		 *  unsafe version; only works if nbBits >= 1 */
		public static size_t LookBitsFast(BIT_DStream_t bitD, U32 nbBits)
		{
			U32 regMask = sizeOfBitContainer * 8 - 1;
			Debug.Assert(nbBits >= 1);
			return (bitD.bitContainer << (int)(bitD.bitsConsumed & regMask)) >> (int)(((regMask + 1) - nbBits) & regMask);
		}

		public static void SkipBits(BIT_DStream_t bitD, U32 nbBits)
		{
			bitD.bitsConsumed += nbBits;
		}

		/*! ReadBits() :
		 *  Read (consume) next n bits from local register and update.
		 *  Pay attention to not read more than nbBits contained into local register.
		 * @return : extracted value. */
		public static size_t ReadBits(BIT_DStream_t bitD, U32 nbBits)
		{
			size_t value = LookBits(bitD, nbBits);
			SkipBits(bitD, nbBits);
			return value;
		}

		/*! ReadBitsFast() :
		 *  unsafe version; only works only if nbBits >= 1 */
		public static size_t ReadBitsFast(BIT_DStream_t bitD, U32 nbBits)
		{
			size_t value = LookBitsFast(bitD, nbBits);
			Debug.Assert(nbBits >= 1);
			SkipBits(bitD, nbBits);
			return value;
		}

		/*! ReloadDStream() :
		 *  Refill `bitD` from buffer previously set in InitDStream() .
		 *  This function is safe, it guarantees it will not read beyond src buffer.
		 * @return : status of `BIT_DStream_t` internal register.
		 *           when status == BIT_DStream_unfinished, internal register is filled with at least 25 or 57 bits */
		public static BIT_DStream_status ReloadDStream(BIT_DStream_t bitD)
		{
			if (bitD.bitsConsumed > (sizeOfBitContainer * 8))  /* overflow detected, like end of stream */
				return BIT_DStream_status.BIT_DStream_overflow;

			if (bitD.ptr >= bitD.limitPtr)
			{
				bitD.ptr -= bitD.bitsConsumed >> 3;
				bitD.bitsConsumed &= 7;
				bitD.bitContainer = MEM_readLEST(bitD.ptr);
				return BIT_DStream_status.BIT_DStream_unfinished;
			}
			if (bitD.ptr == bitD.start)
			{
				if (bitD.bitsConsumed < sizeOfBitContainer * 8) return BIT_DStream_status.BIT_DStream_endOfBuffer;
				return BIT_DStream_status.BIT_DStream_completed;
			}
			/* start < ptr < limitPtr */
			{
				U32 nbBytes = bitD.bitsConsumed >> 3;
				BIT_DStream_status result = BIT_DStream_status.BIT_DStream_unfinished;
				if (bitD.ptr - nbBytes < bitD.start)
				{
					nbBytes = (U32)(bitD.ptr - bitD.start);  /* ptr > start */
					result = BIT_DStream_status.BIT_DStream_endOfBuffer;
				}
				bitD.ptr -= nbBytes;
				bitD.bitsConsumed -= nbBytes * 8;
				bitD.bitContainer = MEM_readLEST(bitD.ptr);   /* reminder : srcSize > sizeof(bitD->bitContainer), otherwise bitD->ptr == bitD->start */
				return result;
			}
		}

		/*! EndOfDStream() :
		 * @return : 1 if DStream has _exactly_ reached its end (all bits consumed).
		 */
		public static uint EndOfDStream(BIT_DStream_t DStream)
		{
			return ((DStream.ptr == DStream.start) && (DStream.bitsConsumed == sizeOfBitContainer * 8)) ? (uint)1 : 0;
		}

		//#if defined (__cplusplus)
		//}
		//#endif

		//#endif /* BITSTREAM_H_MODULE */
	}
}
