/*
 * Copyright (c) 2016-present, Yann Collet, Facebook, Inc.
 * All rights reserved.
 *
 * This source code is licensed under both the BSD-style license (found in the
 * LICENSE file in the root directory of this source tree) and the GPLv2 (found
 * in the COPYING file in the root directory of this source tree).
 * You may select, at your option, one of the above-listed licenses.
 */

using System;
using System.Runtime.InteropServices;
using BYTE = System.Byte;
using size_t = System.UInt32;
using U16 = System.UInt16;
using U32 = System.UInt32;
using U64 = System.UInt64;
using S16 = System.Int16;

namespace EPAM.Deltix.ZStd
{
	internal static unsafe class ZStdInternal
	{
		//#ifndef ZSTD_CCOMMON_H_MODULE
		//#define ZSTD_CCOMMON_H_MODULE

		///* this module contains definitions which must be identical
		// * across compression, decompression and dictBuilder.
		// * It also contains a few functions useful to at least 2 of them
		// * and which benefit from being inlined */

		///*-*************************************
		//*  Dependencies
		//***************************************/
		//#include "compiler.h"
		//#include "mem.h"
		//#include "error_private.h"
		//#define ZSTD_STATIC_LINKING_ONLY
		//#include "zstd.h"
		//#define FSE_STATIC_LINKING_ONLY
		//#include "fse.h"
		//#define HUF_STATIC_LINKING_ONLY
		//#include "huf.h"
		//#ifndef XXH_STATIC_LINKING_ONLY
		//#  define XXH_STATIC_LINKING_ONLY  /* XXH64_state_t */
		//#endif
		//#include "xxhash.h"                /* XXH_reset, update, digest */


		//#if defined (__cplusplus)
		//extern "C" {
		//#endif


		///*-*************************************
		//*  Debug
		//***************************************/
		//#if defined(ZSTD_DEBUG) && (ZSTD_DEBUG>=1)
		//#  include <Debug.Assert.h>
		//#else
		//#  ifndef Debug.Assert
		//#    define Debug.Assert(condition) ((void)0)
		//#  endif
		//#endif

		//#define ZSTD_STATIC_ASSERT(c) { enum { ZSTD_static_assert = 1/(int)(!!(c)) }; }

		//#if defined(ZSTD_DEBUG) && (ZSTD_DEBUG>=2)
		//#  include <stdio.h>
		//extern int g_debuglog_enable;
		///* recommended values for ZSTD_DEBUG display levels :
		// * 1 : no display, enables Debug.Assert() only
		// * 2 : reserved for currently active debug path
		// * 3 : events once per object lifetime (CCtx, CDict, etc.)
		// * 4 : events once per frame
		// * 5 : events once per block
		// * 6 : events once per sequence (*very* verbose) */
		//#  define RAWLOG(l, ...) {                                      \
		//                if ((g_debuglog_enable) & (l<=ZSTD_DEBUG)) {    \
		//                    fprintf(stderr, __VA_ARGS__);               \
		//            }   }
		//#  define DEBUGLOG(l, ...) {                                    \
		//                if ((g_debuglog_enable) & (l<=ZSTD_DEBUG)) {    \
		//                    fprintf(stderr, __FILE__ ": " __VA_ARGS__); \
		//                    fprintf(stderr, " \n");                     \
		//            }   }
		//#else
		//#  define RAWLOG(l, ...)      {}    /* disabled */
		//#  define DEBUGLOG(l, ...)    {}    /* disabled */
		//#endif


		///*-*************************************
		//*  shared macros
		//***************************************/
		//#undef MIN
		//#undef MAX
		//#define MIN(a,b) ((a)<(b) ? (a) : (b))
		//#define MAX(a,b) ((a)>(b) ? (a) : (b))
		//#define CHECK_F(f) { size_t readonly errcod = f; if (ERR_isError(errcod)) return errcod; }  /* check and Forward error code */
		//#define CHECK_E(f, e) { size_t readonly errcod = f; if (ERR_isError(errcod)) return ERROR(e); }  /* check and send Error code */


		///*-*************************************
		//*  Common constants
		//***************************************/
		//#define ZSTD_OPT_NUM    (1<<12)

		public const int ZSTD_REP_NUM = 3;                 /* number of repcodes */
		public const int ZSTD_REP_MOVE = (ZSTD_REP_NUM - 1);
		public static readonly U32[] repStartValue = new U32[ZSTD_REP_NUM] { 1, 4, 8 };

		public const uint KB = (1 << 10);
		public const uint MB = (1 << 20);
		public const uint GB = (1U << 30);

		//#define BIT7 128
		//#define BIT6  64
		//#define BIT5  32
		//#define BIT4  16
		//#define BIT1   2
		//#define BIT0   1

		public const int ZSTD_WINDOWLOG_ABSOLUTEMIN = 10;
		//#define ZSTD_WINDOWLOG_DEFAULTMAX 27 /* Default maximum allowed window log */
		public static readonly size_t[] ZSTD_fcs_fieldSize = { 0, 2, 4, 8 };
		public static readonly size_t[] ZSTD_did_fieldSize = { 0, 1, 2, 4 };

		public const int ZSTD_FRAMEIDSIZE = 4;
		public const size_t ZSTD_frameIdSize = ZSTD_FRAMEIDSIZE;  /* magic number size */

		public const int ZSTD_BLOCKHEADERSIZE = 3;   /* C standard doesn't allow `static readonly` variable to be init using another `static readonly` variable */
		public const size_t ZSTD_blockHeaderSize = ZSTD_BLOCKHEADERSIZE;

		public const int MIN_SEQUENCES_SIZE = 1; /* nbSeq==0 */
		public const int MIN_CBLOCK_SIZE = (1 /*litCSize*/ + 1 /* RLE or RAW */ + MIN_SEQUENCES_SIZE /* nbSeq==0 */);   /* for a non-null block */

		public const int HufLog = 12;
		public enum symbolEncodingType_e { set_basic, set_rle, set_compressed, set_repeat }

		public const int LONGNBSEQ = 0x7F00;

		public const int MINMATCH = 3;

		//#define Litbits  8
		//#define MaxLit ((1<<Litbits) - 1)
		public const int MaxML = 52;
		public const int MaxLL = 35;
		public const int DefaultMaxOff = 28;
		public const int MaxOff = 31;
		public static int MaxSeq = Math.Max(MaxLL, MaxML);   /* Assumption : MaxOff < MaxLL,MaxML */

		public const int MLFSELog = 9;
		public const int LLFSELog = 9;
		public const int OffFSELog = 8;
		public static int MaxFSELog = Math.Max(Math.Max(MLFSELog, LLFSELog), OffFSELog);

		public static readonly U32[] LL_bits = new U32[MaxLL + 1] {
											  0, 0, 0, 0, 0, 0, 0, 0,
											  0, 0, 0, 0, 0, 0, 0, 0,
											  1, 1, 1, 1, 2, 2, 3, 3,
											  4, 6, 7, 8, 9,10,11,12,
											 13,14,15,16 };
		public static readonly S16[] LL_defaultNorm = new S16[MaxLL + 1] {
													 4, 3, 2, 2, 2, 2, 2, 2,
													 2, 2, 2, 2, 2, 1, 1, 1,
													 2, 2, 2, 2, 2, 2, 2, 2,
													 2, 3, 2, 1, 1, 1, 1, 1,
													-1,-1,-1,-1 };
		public const int LL_DEFAULTNORMLOG = 6;  /* for static allocation */
		public const U32 LL_defaultNormLog = LL_DEFAULTNORMLOG;

		public static readonly U32[] ML_bits = new U32[MaxML + 1] {
											  0, 0, 0, 0, 0, 0, 0, 0,
											  0, 0, 0, 0, 0, 0, 0, 0,
											  0, 0, 0, 0, 0, 0, 0, 0,
											  0, 0, 0, 0, 0, 0, 0, 0,
											  1, 1, 1, 1, 2, 2, 3, 3,
											  4, 4, 5, 7, 8, 9,10,11,
											 12,13,14,15,16 };
		public static readonly S16[] ML_defaultNorm = new S16[MaxML + 1] {
													 1, 4, 3, 2, 2, 2, 2, 2,
													 2, 1, 1, 1, 1, 1, 1, 1,
													 1, 1, 1, 1, 1, 1, 1, 1,
													 1, 1, 1, 1, 1, 1, 1, 1,
													 1, 1, 1, 1, 1, 1, 1, 1,
													 1, 1, 1, 1, 1, 1,-1,-1,
													-1,-1,-1,-1,-1 };
		public const int ML_DEFAULTNORMLOG = 6;  /* for static allocation */
		public const U32 ML_defaultNormLog = ML_DEFAULTNORMLOG;

		public static readonly S16[] OF_defaultNorm = new S16[DefaultMaxOff + 1] {
															 1, 1, 1, 1, 1, 1, 2, 2,
															 2, 1, 1, 1, 1, 1, 1, 1,
															 1, 1, 1, 1, 1, 1, 1, 1,
															-1,-1,-1,-1,-1 };
		public const int OF_DEFAULTNORMLOG = 5;  /* for static allocation */
		public const U32 OF_defaultNormLog = OF_DEFAULTNORMLOG;


		///*-*******************************************
		//*  Shared functions to include for inlining
		//*********************************************/
		//static void ZSTD_copy8(void* dst, void* src) { memcpy(dst, src, 8); }
		//#define COPY8(d,s) { ZSTD_copy8(d,s); d+=8; s+=8; }

		///*! Wildcopy() :
		// *  custom version of memcpy(), can overwrite up to WILDCOPY_OVERLENGTH bytes (if length==0) */
		public const int WILDCOPY_OVERLENGTH = 8;

		//MEM_STATIC void Wildcopy(void* dst, void* src, ptrdiff_t length)
		//{
		//    readonly BYTE* ip = (readonly BYTE*)src;
		//    BYTE* op = (BYTE*)dst;
		//    BYTE* readonly oend = op + length;
		//    do
		//        COPY8(op, ip)
		//    while (op < oend);
		//}

		//MEM_STATIC void ZSTD_wildcopy_e(void* dst, void* src, void* dstEnd)   /* should be faster for decoding, but strangely, not verified on all platform */
		//{
		//    readonly BYTE* ip = (readonly BYTE*)src;
		//    BYTE* op = (BYTE*)dst;
		//    BYTE* readonly oend = (BYTE*)dstEnd;
		//    do
		//        COPY8(op, ip)
		//    while (op < oend);
		//}


		///*-*******************************************
		//*  Private declarations
		//*********************************************/
		//typedef struct seqDef_s {
		//    U32 offset;
		//    U16 litLength;
		//    U16 matchLength;
		//} seqDef;

		//typedef struct {
		//    seqDef* sequencesStart;
		//    seqDef* sequences;
		//    BYTE* litStart;
		//    BYTE* lit;
		//    BYTE* llCode;
		//    BYTE* mlCode;
		//    BYTE* ofCode;
		//    U32   longLengthID;   /* 0 == no longLength; 1 == Lit.longLength; 2 == Match.longLength; */
		//    U32   longLengthPos;
		//} seqStore_t;

		//readonly seqStore_t* GetSeqStore(readonly ZSTD_CCtx* ctx);   /* compress & dictBuilder */
		//void SeqToCodes(readonly seqStore_t* seqStorePtr);   /* compress, dictBuilder, decodeCorpus (shouldn't get its definition from here) */

		///* custom memory allocation functions */
		//void* Malloc(size_t size, ZSTD_customMem customMem);
		//void* Calloc(size_t size, ZSTD_customMem customMem);
		//void Free(void* ptr, ZSTD_customMem customMem);


		//MEM_STATIC U32 ZSTD_highbit32(U32 val)   /* compress, dictBuilder, decodeCorpus */
		//{
		//    Debug.Assert(val != 0);
		//    {
		//#   if defined(_MSC_VER)   /* Visual */
		//        unsigned long r=0;
		//        _BitScanReverse(&r, val);
		//        return (unsigned)r;
		//#   elif defined(__GNUC__) && (__GNUC__ >= 3)   /* GCC Intrinsic */
		//        return 31 - __builtin_clz(val);
		//#   else   /* Software version */
		//        static readonly U32 DeBruijnClz[32] = { 0, 9, 1, 10, 13, 21, 2, 29, 11, 14, 16, 18, 22, 25, 3, 30, 8, 12, 20, 28, 15, 17, 24, 7, 19, 27, 23, 6, 26, 5, 4, 31 };
		//        U32 v = val;
		//        v |= v >> 1;
		//        v |= v >> 2;
		//        v |= v >> 4;
		//        v |= v >> 8;
		//        v |= v >> 16;
		//        return DeBruijnClz[(v * 0x07C4ACDDU) >> 27];
		//#   endif
		//    }
		//}


		///* InvalidateRepCodes() :
		// * ensures next compression will not use repcodes from previous block.
		// * Note : only works with regular variant;
		// *        do not use with extDict variant ! */
		//void InvalidateRepCodes(ZSTD_CCtx* cctx);   /* zstdmt, adaptive_compression (shouldn't get this definition from here) */

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
		public struct blockProperties_t
		{
			public blockType_e blockType;
			public U32 lastBlock;
			public U32 origSize;
		};

		///*! GetcBlockSize() :
		// *  Provides the size of compressed block from block header `src` */
		///* Used by: decompress, fullbench (does not get its definition from here) */
		//size_t GetcBlockSize(void* src, size_t srcSize,
		//                          blockProperties_t* bpPtr);

		//#if defined (__cplusplus)
		//}
		//#endif

		//#endif   /* ZSTD_CCOMMON_H_MODULE */
	}
}
