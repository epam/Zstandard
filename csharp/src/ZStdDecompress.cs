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
using System.Diagnostics;
using System.Runtime.InteropServices;
using BYTE = System.Byte;
using size_t = System.UInt32;
using U16 = System.UInt16;
using U32 = System.UInt32;
using U64 = System.UInt64;
using S16 = System.Int16;
using ZSTD_DCtx = EPAM.Deltix.ZStd.ZStdDecompress.ZSTD_DCtx_s;

using HUF_DTable = System.UInt32;
using BIT_DStream_t = EPAM.Deltix.ZStd.BitStream.BIT_DStream_t;
using XXH64_state_t = EPAM.Deltix.ZStd.XXH64_state_s;

using static EPAM.Deltix.ZStd.Mem;
using static EPAM.Deltix.ZStd.BitStream;
using static EPAM.Deltix.ZStd.ZStd;
using static EPAM.Deltix.ZStd.ZStdInternal;
using static EPAM.Deltix.ZStd.ZStdErrors;
using static EPAM.Deltix.ZStd.EntropyCommon;
using static EPAM.Deltix.ZStd.HufDecompress;
using static EPAM.Deltix.ZStd.XxHash;
using static EPAM.Deltix.ZStd.Huf;

namespace EPAM.Deltix.ZStd
{
#if ZSTDPUBLIC
	public
#else
	internal
#endif
	static unsafe class ZStdDecompress
	{
		///* ***************************************************************
		//*  Tuning parameters
		//*****************************************************************/
		///*!
		// * HEAPMODE :
		// * Select how default decompression function Decompress() allocates its context,
		// * on stack (0), or into heap (1, default; requires malloc()).
		// * Note that functions with explicit context such as DecompressDCtx() are unaffected.
		// */
		//#ifndef ZSTD_HEAPMODE
		//#  define ZSTD_HEAPMODE 1
		//#endif

		///*!
		//*  LEGACY_SUPPORT :
		//*  if set to 1+, Decompress() can decode older formats (v0.1+)
		//*/
		//#ifndef ZSTD_LEGACY_SUPPORT
		//#  define ZSTD_LEGACY_SUPPORT 0
		//#endif

		///*!
		// *  MAXWINDOWSIZE_DEFAULT :
		// *  maximum window size accepted by DStream __by default__.
		// *  Frames requiring more memory will be rejected.
		// *  It's possible to set a different limit using ZSTD_DCtx_setMaxWindowSize().
		// */
		//#ifndef ZSTD_MAXWINDOWSIZE_DEFAULT
		//#  define ZSTD_MAXWINDOWSIZE_DEFAULT (((U32)1 << ZSTD_WINDOWLOG_DEFAULTMAX) + 1)
		//#endif


		///*-*******************************************************
		//*  Dependencies
		//*********************************************************/
		//#include <string.h>      /* memcpy, memmove, memset */
		//#include "cpu.h"
		//#include "mem.h"         /* low level memory routines */
		//#define FSE_STATIC_LINKING_ONLY
		//#include "fse.h"
		//#define HUF_STATIC_LINKING_ONLY
		//#include "huf.h"
		//#include "zstd_internal.h"

		//#if defined(ZSTD_LEGACY_SUPPORT) && (ZSTD_LEGACY_SUPPORT>=1)
		//#  include "zstd_legacy.h"
		//#endif


		///*-*************************************
		//*  Errors
		//***************************************/
		//#define IsError ERR_isError   /* for inlining */
		//#define FSE_isError  ERR_isError
		//#define HUF_isError  ERR_isError


		///*_*******************************************************
		//*  Memory operations
		//**********************************************************/
		//static void ZSTD_copy4(void* dst, void* src) { memcpy(dst, src, 4); }


		///*-*************************************************************
		//*   Context management
		//***************************************************************/
		public enum ZSTD_dStage
		{
			ZSTDds_getFrameHeaderSize, ZSTDds_decodeFrameHeader,
			ZSTDds_decodeBlockHeader, ZSTDds_decompressBlock,
			ZSTDds_decompressLastBlock, ZSTDds_checkChecksum,
			ZSTDds_decodeSkippableHeader, ZSTDds_skipFrame
		}

		public enum ZSTD_dStreamStage
		{
			zdss_init = 0, zdss_loadHeader,
			zdss_read, zdss_load, zdss_flush
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
		public struct ZSTD_seqSymbol_header
		{
			public U32 fastMode;
			public U32 tableLog;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
		public struct SeqSymbol
		{
			public SeqSymbol(U16 nextState, BYTE nbAdditionalBits, BYTE nbBits, U32 baseValue)
			{
				this.nextState = nextState;
				this.nbAdditionalBits = nbAdditionalBits;
				this.nbBits = nbBits;
				this.baseValue = baseValue;
			}

			public U16 nextState;
			public BYTE nbAdditionalBits;
			public BYTE nbBits;
			public U32 baseValue;
		}

		static int SEQSYMBOL_TABLE_SIZE(int log)
		{
			return (1 + (1 << (log)));
		}

		public class ZSTD_entropyDTables_t : IDisposable
		{
			public SeqSymbol* LLTable;
			public SeqSymbol* OFTable;
			public SeqSymbol* MLTable;

			public HUF_DTable* hufTable;
			public U32* workspace;
			public U32* rep;

			public GCHandle hufTableHandle;
			public GCHandle workspaceHandle;
			public GCHandle repHandle;

			public GCHandle LLTableHandle;
			public GCHandle OFTableHandle;
			public GCHandle MLTableHandle;

			internal static IntPtr InitPtr(object array, ref GCHandle handle)
			{
				handle = GCHandle.Alloc(array, GCHandleType.Pinned);
				return handle.AddrOfPinnedObject();
			}

			internal static void SafeFree(ref GCHandle handle)
			{
				if (handle.IsAllocated)
					handle.Free();
			}

			public ZSTD_entropyDTables_t()
			{
				hufTable = (HUF_DTable*)InitPtr(new HUF_DTable[Huf.HUF_DTABLE_SIZE(HufLog)]  /* can accommodate HUF_decompress4X */, ref hufTableHandle);
				workspace = (U32*)InitPtr(new U32[HUF_DECOMPRESS_WORKSPACE_SIZE_U32], ref workspaceHandle);
				rep = (U32*)InitPtr(new U32[ZSTD_REP_NUM], ref repHandle);

				LLTable = (SeqSymbol*)InitPtr(new SeqSymbol[SEQSYMBOL_TABLE_SIZE(LLFSELog)], ref LLTableHandle);
				OFTable = (SeqSymbol*)InitPtr(new SeqSymbol[SEQSYMBOL_TABLE_SIZE(OffFSELog)], ref OFTableHandle);
				MLTable = (SeqSymbol*)InitPtr(new SeqSymbol[SEQSYMBOL_TABLE_SIZE(MLFSELog)], ref MLTableHandle);
			}

			public void Dispose()
			{
				SafeFree(ref hufTableHandle);
				SafeFree(ref workspaceHandle);
				SafeFree(ref repHandle);

				SafeFree(ref LLTableHandle);
				SafeFree(ref OFTableHandle);
				SafeFree(ref MLTableHandle);
			}
		}

		public class ZSTD_DCtx_s : IDisposable
		{
			public SeqSymbol* LLTptr;
			public SeqSymbol* MLTptr;
			public SeqSymbol* OFTptr;
			public HUF_DTable* HUFptr;
			public ZSTD_entropyDTables_t entropy = new ZSTD_entropyDTables_t();
			public void* previousDstEnd;   /* detect continuity */
			public void* baseField;             /* start of current segment */
			public void* vBase;            /* virtual start of previous segment if it was just before current one */
			public void* dictEnd;          /* end of previous segment */
			public size_t expected;
			public FrameHeader fParams;
			public U64 decodedSize;
			//public blockType_e bType;            /* used in DecompressContinue(), store blockType between block header decoding and block decompression stages */
			public ZSTD_dStage stage;
			public U32 litEntropy;
			public U32 fseEntropy;
			public XXH64_state_t xxhState = new XXH64_state_t();
			//public size_t headerSize;
			public U32 dictID;
			public ZSTD_format_e format;
			public BYTE* litPtr;
			//public ZSTD_customMem customMem;
			public size_t litSize;
			//public size_t rleSize;
			public size_t staticSize;
			//public int bmi2;                     /* == 1 if the CPU supports BMI2 and 0 otherwise. CPU support is determined dynamically once per context lifetime. */

			/* streaming */
			//public ZSTD_DDict* ddictLocal;
			//public  ZSTD_DDict* ddict;
			//public ZSTD_dStreamStage streamStage;
			//public sbyte* inBuff;
			//public size_t inBuffSize;
			//public size_t inPos;
			//public size_t maxWindowSize;
			//public sbyte* outBuff;
			//public size_t outBuffSize;
			//public size_t outStart;
			//public size_t outEnd;
			//public size_t lhSize;
			//public void* legacyContext;
			//public U32 previousLegacyVersion;
			//public U32 legacyVersion;
			//public U32 hostageByte;

			/* workspace */
			public BYTE* litBuffer;
			//public BYTE[] headerBuffer = new BYTE[ZSTD_FRAMEHEADERSIZE_MAX];

			private GCHandle litBufferHandle;

			public ZSTD_DCtx_s()
			{
				litBuffer = (BYTE*)ZSTD_entropyDTables_t.InitPtr(new BYTE[ZSTD_BLOCKSIZE_MAX + WILDCOPY_OVERLENGTH], ref litBufferHandle);
			}

			public void Dispose()
			{
				entropy.Dispose();
				xxhState.Dispose();
				ZSTD_entropyDTables_t.SafeFree(ref litBufferHandle);
			}
		}  /* typedef'd to ZSTD_DCtx within "zstd.h" */

		//size_t ZSTD_sizeof_DCtx ( ZSTD_DCtx dctx)
		//{
		//    if (dctx==null) return 0;   /* support sizeof null */
		//    return sizeof(*dctx)
		//           + ZSTD_sizeof_DDict(dctx->ddictLocal)
		//           + dctx->inBuffSize + dctx->outBuffSize;
		//}

		//size_t EstimateDCtxSize(void) { return sizeof(ZSTD_DCtx); }


		static size_t StartingInputLength(ZSTD_format_e format)
		{
			size_t startingInputLength = (format == ZSTD_format_e.ZSTD_f_zstd1_magicless) ?
							ZSTD_frameHeaderSize_prefix - ZSTD_frameIdSize :
							ZSTD_frameHeaderSize_prefix;
			//ZSTD_STATIC_ASSERT(ZSTD_FRAMEHEADERSIZE_PREFIX >=  ZSTD_FRAMEIDSIZE);
			/* only supports formats ZSTD_format_e.ZSTD_f_zstd1 and ZSTD_format_e.ZSTD_f_zstd1_magicless */
			Debug.Assert((format == ZSTD_format_e.ZSTD_f_zstd1) || (format == ZSTD_format_e.ZSTD_f_zstd1_magicless));
			return startingInputLength;
		}

		static void ZSTD_initDCtx_internal(ZSTD_DCtx dctx)
		{
			dctx.format = ZSTD_format_e.ZSTD_f_zstd1;  /* DecompressBegin() invokes StartingInputLength() with argument dctx->format */
			dctx.staticSize = 0;
			//dctx.maxWindowSize = ZSTD_MAXWINDOWSIZE_DEFAULT;
			//dctx.ddict = null;
			//dctx.ddictLocal = null;
			//dctx.inBuff = null;
			//dctx.inBuffSize = 0;
			//dctx.outBuffSize = 0;
			//dctx.streamStage = zdss_init;
			//dctx.bmi2 = ZSTD_cpuid_bmi2(Cpuid());
		}

		//ZSTD_DCtx InitStaticDCtx(void *workspace, size_t workspaceSize)
		//{
		//    ZSTD_DCtx  dctx = (ZSTD_DCtx) workspace;

		//    if ((size_t)workspace & 7) return null;  /* 8-aligned */
		//    if (workspaceSize < sizeof(ZSTD_DCtx)) return null;  /* minimum size */

		//    ZSTD_initDCtx_internal(dctx);
		//    dctx->staticSize = workspaceSize;
		//    dctx->inBuff = (sbyte*)(dctx+1);
		//    return dctx;
		//}

		static ZSTD_DCtx ZSTD_createDCtx_advanced()
		{
			ZSTD_DCtx dctx = new ZSTD_DCtx();
			//dctx.customMem = customMem;
			//dctx.legacyContext = null;
			//dctx.previousLegacyVersion = 0;
			ZSTD_initDCtx_internal(dctx);
			return dctx;
		}

		static ZSTD_DCtx CreateDCtx()
		{
			return ZSTD_createDCtx_advanced();
		}

		//size_t FreeDCtx(ZSTD_DCtx dctx)
		//{
		//    if (dctx==null) return 0;   /* support free on null */
		//    if (dctx->staticSize) return  ERROR( Error.memory_allocation);   /* not compatible with static DCtx */
		//    {   ZSTD_customMem  cMem = dctx->customMem;
		//        FreeDDict(dctx->ddictLocal);
		//        dctx->ddictLocal = null;
		//        Free(dctx->inBuff, cMem);
		//        dctx->inBuff = null;
		//#if defined(ZSTD_LEGACY_SUPPORT) && (ZSTD_LEGACY_SUPPORT >= 1)
		//        if (dctx->legacyContext)
		//            FreeLegacyStreamContext(dctx->legacyContext, dctx->previousLegacyVersion);
		//#endif
		//        Free(dctx, cMem);
		//        return 0;
		//    }
		//}

		///* no longer useful */
		//void CopyDCtx(ZSTD_DCtx dstDCtx,  ZSTD_DCtx srcDCtx)
		//{
		//    size_t  toCopy = (size_t)((sbyte*)(&dstDCtx->inBuff) - (sbyte*)dstDCtx);
		//    memcpy(dstDCtx, srcDCtx, toCopy);  /* no need to copy workspace */
		//}


		///*-*************************************************************
		// *   Frame header decoding
		// ***************************************************************/

		///*! IsFrame() :
		// *  Tells if the content of `buffer` starts with a valid Frame Identifier.
		// *  Note : Frame Identifier is 4 bytes. If `size < 4`, @return will always be 0.
		// *  Note 2 : Legacy Frame Identifiers are considered valid only if Legacy Support is enabled.
		// *  Note 3 : Skippable Frame Identifiers are considered valid. */
		//unsigned IsFrame(void* buffer, size_t size)
		//{
		//    if (size <  ZSTD_frameIdSize) return 0;
		//    {   U32  magic = MEM_readLE32(buffer);
		//        if (magic == ZSTD_MAGICNUMBER) return 1;
		//        if ((magic & 0xFFFFFFF0U) == ZSTD_MAGIC_SKIPPABLE_START) return 1;
		//    }
		//#if defined(ZSTD_LEGACY_SUPPORT) && (ZSTD_LEGACY_SUPPORT >= 1)
		//    if (IsLegacy(buffer, size)) return 1;
		//#endif
		//    return 0;
		//}

		/** ZSTD_frameHeaderSize_internal() :
		 *  srcSize must be large enough to reach header size fields.
		 *  note : only works for formats ZSTD_format_e.ZSTD_f_zstd1 and ZSTD_format_e.ZSTD_f_zstd1_magicless.
		 * @return : size of the Frame Header
		 *           or an error code, which can be tested with  IsError() */
		static size_t ZSTD_frameHeaderSize_internal(void* src, size_t srcSize, ZSTD_format_e format)
		{
			size_t minInputSize = StartingInputLength(format);
			if (srcSize < minInputSize) return ERROR(Error.srcSize_wrong);

			{
				U32 fhd = ((BYTE*)src)[minInputSize - 1];
				U32 dictID = fhd & 3;
				bool singleSegment = ((fhd >> 5) & 1) != 0;
				U32 fcsId = fhd >> 6;
				return minInputSize + (!singleSegment ? (U32)1 : 0)
					 + ZSTD_did_fieldSize[dictID] + ZSTD_fcs_fieldSize[fcsId]
					 + (singleSegment && (fcsId == 0) ? (U32)1 : 0);
			}
		}

		/** FrameHeaderSize() :
		 *  srcSize must be >= ZSTD_frameHeaderSize_prefix.
		 * @return : size of the Frame Header,
		 *           or an error code (if srcSize is too small) */
		static size_t FrameHeaderSize(void* src, size_t srcSize)
		{
			return ZSTD_frameHeaderSize_internal(src, srcSize, ZSTD_format_e.ZSTD_f_zstd1);
		}


		/** ZSTD_getFrameHeader_advanced() :
		 *  decode Frame Header, or require larger `srcSize`.
		 *  note : only works for formats ZSTD_format_e.ZSTD_f_zstd1 and ZSTD_format_e.ZSTD_f_zstd1_magicless
		 * @return : 0, `zfhPtr` is correctly filled,
		 *          >0, `srcSize` is too small, value is wanted `srcSize` amount,
		 *           or an error code, which can be tested using  IsError() */
		static size_t ZSTD_getFrameHeader_advanced(ref FrameHeader zfhPtr, void* src, size_t srcSize, ZSTD_format_e format)
		{
			BYTE* ip = (BYTE*)src;
			size_t minInputSize = StartingInputLength(format);

			if (srcSize < minInputSize) return minInputSize;

			if ((format != ZSTD_format_e.ZSTD_f_zstd1_magicless)
			  && (MEM_readLE32(src) != ZSTD_MAGICNUMBER))
			{
				if ((MEM_readLE32(src) & 0xFFFFFFF0U) == ZSTD_MAGIC_SKIPPABLE_START)
				{
					/* skippable frame */
					if (srcSize < ZSTD_skippableHeaderSize)
						return ZSTD_skippableHeaderSize; /* magic number + frame length */
					zfhPtr.Reset();
					zfhPtr.frameContentSize = MEM_readLE32((sbyte*)src + ZSTD_frameIdSize);
					zfhPtr.frameType = ZSTD_frameType_e.ZSTD_skippableFrame;
					return 0;
				}
				return ERROR(Error.prefix_unknown);
			}

			/* ensure there is enough `srcSize` to fully read/decode frame header */
			{
				size_t fhsize = ZSTD_frameHeaderSize_internal(src, srcSize, format);
				if (srcSize < fhsize) return fhsize;
				zfhPtr.headerSize = (U32)fhsize;
			}

			{
				U32 fhdByte = ip[minInputSize - 1];
				size_t pos = minInputSize;
				U32 dictIDSizeCode = fhdByte & 3;
				U32 checksumFlag = (fhdByte >> 2) & 1;
				U32 singleSegment = (fhdByte >> 5) & 1;
				U32 fcsID = fhdByte >> 6;
				U64 windowSize = 0;
				U32 dictID = 0;
				U64 frameContentSize = ZSTD_CONTENTSIZE_UNKNOWN;
				if ((fhdByte & 0x08) != 0)
					return ERROR(Error.frameParameter_unsupported); /* reserved bits, must be zero */

				if (singleSegment == 0)
				{
					U32 wlByte = ip[pos++];
					U32 windowLog = (wlByte >> 3) + ZSTD_WINDOWLOG_ABSOLUTEMIN;
					if (windowLog > ZSTD_WINDOWLOG_MAX)
						return ERROR(Error.frameParameter_windowTooLarge);
					windowSize = ((ulong)1 << (int)windowLog);
					windowSize += (windowSize >> 3) * (wlByte & 7);
				}
				switch (dictIDSizeCode)
				{
					default: throw new InvalidOperationException();   /* impossible */
					case 0: break;
					case 1: dictID = ip[pos]; pos++; break;
					case 2: dictID = MEM_readLE16(ip + pos); pos += 2; break;
					case 3: dictID = MEM_readLE32(ip + pos); pos += 4; break;
				}
				switch (fcsID)
				{
					default: throw new InvalidOperationException();  /* impossible */
					case 0: if (singleSegment != 0) frameContentSize = ip[pos]; break;
					case 1: frameContentSize = MEM_readLE16(ip + pos) + 256; break;
					case 2: frameContentSize = MEM_readLE32(ip + pos); break;
					case 3: frameContentSize = MEM_readLE64(ip + pos); break;
				}
				if (singleSegment != 0) windowSize = frameContentSize;

				zfhPtr.frameType = ZSTD_frameType_e.ZSTD_frame;
				zfhPtr.frameContentSize = frameContentSize;
				zfhPtr.windowSize = windowSize;
				zfhPtr.blockSizeMax = (uint)Math.Min(windowSize, ZSTD_BLOCKSIZE_MAX);
				zfhPtr.dictID = dictID;
				zfhPtr.checksumFlag = checksumFlag;
			}
			return 0;
		}

		/** GetFrameHeader() :
		 *  decode Frame Header, or require larger `srcSize`.
		 *  note : this function does not consume input, it only reads it.
		 * @return : 0, `zfhPtr` is correctly filled,
		 *          >0, `srcSize` is too small, value is wanted `srcSize` amount,
		 *           or an error code, which can be tested using  IsError() */
		static size_t GetFrameHeader(ref FrameHeader zfhPtr, void* src, size_t srcSize)
		{
			return ZSTD_getFrameHeader_advanced(ref zfhPtr, src, srcSize, ZSTD_format_e.ZSTD_f_zstd1);
		}


		/** GetFrameContentSize() :
		 *  compatible with legacy mode
		 * @return : decompressed size of the single frame pointed to be `src` if known, otherwise
		 *         - ZSTD_CONTENTSIZE_UNKNOWN if the size cannot be determined
		 *         - ZSTD_CONTENTSIZE_ERROR if an error occurred (e.g. invalid magic number, srcSize too small) */
		static ulong GetFrameContentSize(void* src, size_t srcSize)
		{
			FrameHeader zfh = new FrameHeader();
			if (GetFrameHeader(ref zfh, src, srcSize) != 0)
				return ZSTD_CONTENTSIZE_ERROR;
			if (zfh.frameType == ZSTD_frameType_e.ZSTD_skippableFrame)
			{
				return 0;
			}
			else
			{
				return zfh.frameContentSize;
			}
		}

		///** FindDecompressedSize() :
		// *  compatible with legacy mode
		// *  `srcSize` must be the exact length of some number of ZSTD compressed and/or
		// *      skippable frames
		// *  @return : decompressed size of the frames contained */
		//unsigned long long FindDecompressedSize(void* src, size_t srcSize)
		//{
		//    unsigned long long totalDstSize = 0;

		//    while (srcSize >= ZSTD_frameHeaderSize_prefix) {
		//        U32  magicNumber = MEM_readLE32(src);

		//        if ((magicNumber & 0xFFFFFFF0U) == ZSTD_MAGIC_SKIPPABLE_START) {
		//            size_t skippableSize;
		//            if (srcSize < ZSTD_skippableHeaderSize)
		//                return  ERROR( Error.srcSize_wrong);
		//            skippableSize = MEM_readLE32(( BYTE *)src +  ZSTD_frameIdSize)
		//                          + ZSTD_skippableHeaderSize;
		//            if (srcSize < skippableSize) {
		//                return ZSTD_CONTENTSIZE_ERROR;
		//            }

		//            src = ( BYTE *)src + skippableSize;
		//            srcSize -= skippableSize;
		//            continue;
		//        }

		//        {   unsigned long long  ret = GetFrameContentSize(src, srcSize);
		//            if (ret >= ZSTD_CONTENTSIZE_ERROR) return ret;

		//            /* check for overflow */
		//            if (totalDstSize + ret < totalDstSize) return ZSTD_CONTENTSIZE_ERROR;
		//            totalDstSize += ret;
		//        }
		//        {   size_t  frameSrcSize = FindFrameCompressedSize(src, srcSize);
		//            if ( IsError(frameSrcSize)) {
		//                return ZSTD_CONTENTSIZE_ERROR;
		//            }

		//            src = ( BYTE *)src + frameSrcSize;
		//            srcSize -= frameSrcSize;
		//        }
		//    }  /* while (srcSize >= ZSTD_frameHeaderSize_prefix) */

		//    if (srcSize) return ZSTD_CONTENTSIZE_ERROR;

		//    return totalDstSize;
		//}

		/** GetDecompressedSize() :
		*   compatible with legacy mode
		*   @return : decompressed size if known, 0 otherwise
		              note : 0 can mean any of the following :
		                   - frame content is empty
		                   - decompressed size field is not present in frame header
		                   - frame header unknown / not supported
		                   - frame header not complete (`srcSize` too small) */
		public static ulong GetDecompressedSize(byte[] src)
		{
			return GetDecompressedSize(src, (size_t)src.Length);
		}

		/** GetDecompressedSize() :
		*   compatible with legacy mode
		*   @return : decompressed size if known, 0 otherwise
		              note : 0 can mean any of the following :
		                   - frame content is empty
		                   - decompressed size field is not present in frame header
		                   - frame header unknown / not supported
		                   - frame header not complete (`srcSize` too small) */
		public static ulong GetDecompressedSize(byte[] src, size_t srcSize)
		{
			fixed (byte* srcPtr = src)
				return GetDecompressedSize(srcPtr, srcSize);
		}

		/** GetDecompressedSize() :
		*   compatible with legacy mode
		*   @return : decompressed size if known, 0 otherwise
		              note : 0 can mean any of the following :
		                   - frame content is empty
		                   - decompressed size field is not present in frame header
		                   - frame header unknown / not supported
		                   - frame header not complete (`srcSize` too small) */
		static ulong GetDecompressedSize(void* src, size_t srcSize)
		{
			ulong ret = GetFrameContentSize(src, srcSize);
			//ZSTD_STATIC_ASSERT(ZSTD_CONTENTSIZE_ERROR < ZSTD_CONTENTSIZE_UNKNOWN);
			return (ret >= ZSTD_CONTENTSIZE_ERROR) ? 0 : ret;
		}


		/** DecodeFrameHeader() :
		*   `headerSize` must be the size provided by FrameHeaderSize().
		*   @return : 0 if success, or an error code, which can be tested using  IsError() */
		static size_t DecodeFrameHeader(ZSTD_DCtx dctx, void* src, size_t headerSize)
		{
			size_t result = ZSTD_getFrameHeader_advanced(ref dctx.fParams, src, headerSize, dctx.format);
			if (IsError(result)) return result;    /* invalid header */
			if (result > 0) return ERROR(Error.srcSize_wrong);  /* headerSize too small */
			if (dctx.fParams.dictID != 0 && (dctx.dictID != dctx.fParams.dictID))
				return ERROR(Error.dictionary_wrong);
			if (dctx.fParams.checksumFlag != 0) XXH64_reset(dctx.xxhState, 0);
			return 0;
		}


		///*-*************************************************************
		// *   Block decoding
		// ***************************************************************/

		/*! GetcBlockSize() :
		*   Provides the size of compressed block from block header `src` */
		static size_t GetcBlockSize(void* src, size_t srcSize, blockProperties_t* bpPtr)
		{
			if (srcSize < ZSTD_blockHeaderSize) return ERROR(Error.srcSize_wrong);
			{
				U32 cBlockHeader = MEM_readLE24(src);
				U32 cSize = cBlockHeader >> 3;
				bpPtr->lastBlock = cBlockHeader & 1;
				bpPtr->blockType = (blockType_e)((cBlockHeader >> 1) & 3);
				bpPtr->origSize = cSize;   /* only useful for RLE */
				if (bpPtr->blockType == blockType_e.bt_rle) return 1;
				if (bpPtr->blockType == blockType_e.bt_reserved) return ERROR(Error.corruption_detected);
				return cSize;
			}
		}


		static size_t CopyRawBlock(void* dst, size_t dstCapacity, void* src, size_t srcSize)
		{
			if (srcSize > dstCapacity) return ERROR(Error.dstSize_tooSmall);
			memcpy(dst, src, srcSize);
			return srcSize;
		}


		//static size_t SetRleBlock(void* dst, size_t dstCapacity,
		//                         void* src, size_t srcSize,
		//                               size_t regenSize)
		//{
		//    if (srcSize != 1) return  ERROR( Error.srcSize_wrong);
		//    if (regenSize > dstCapacity) return  ERROR( Error.dstSize_tooSmall);
		//    memset(dst, *( BYTE*)src, regenSize);
		//    return regenSize;
		//}

		/*! DecodeLiteralsBlock() :
		 * @return : nb of bytes read from src (< srcSize )
		 *  note : symbol not declared but exposed for fullbench */
		static size_t DecodeLiteralsBlock(ZSTD_DCtx dctx, void* src, size_t srcSize)   /* note : srcSize < BLOCKSIZE */
		{
			if (srcSize < MIN_CBLOCK_SIZE) return ERROR(Error.corruption_detected);

			{
				BYTE* istart = (BYTE*)src;
				symbolEncodingType_e litEncType = (symbolEncodingType_e)(istart[0] & 3);

				switch (litEncType)
				{
					case symbolEncodingType_e.set_repeat:
					case symbolEncodingType_e.set_compressed:
						/* fall-through */
						if (litEncType == symbolEncodingType_e.set_repeat)
							if (dctx.litEntropy == 0) return ERROR(Error.dictionary_corrupted);

						if (srcSize < 5) return ERROR(Error.corruption_detected);   /* srcSize >= MIN_CBLOCK_SIZE == 3; here we need up to 5 for case 3 */
						{
							size_t lhSize, litSize, litCSize;
							bool singleStream = false;
							U32 lhlCode = ((U32)istart[0] >> 2) & 3;
							U32 lhc = MEM_readLE32(istart);
							switch (lhlCode)
							{
								case 0:
								case 1:
								default:   /* note : default is impossible, since lhlCode into [0..3] */
										   /* 2 - 2 - 10 - 10 */
									singleStream = lhlCode == 0;
									lhSize = 3;
									litSize = (lhc >> 4) & 0x3FF;
									litCSize = (lhc >> 14) & 0x3FF;
									break;
								case 2:
									/* 2 - 2 - 14 - 14 */
									lhSize = 4;
									litSize = (lhc >> 4) & 0x3FFF;
									litCSize = lhc >> 18;
									break;
								case 3:
									/* 2 - 2 - 18 - 18 */
									lhSize = 5;
									litSize = (lhc >> 4) & 0x3FFFF;
									litCSize = (lhc >> 22) + ((U32)istart[4] << 10);
									break;
							}
							if (litSize > ZSTD_BLOCKSIZE_MAX) return ERROR(Error.corruption_detected);
							if (litCSize + lhSize > srcSize) return ERROR(Error.corruption_detected);

							if (IsError((litEncType == symbolEncodingType_e.set_repeat) ?
												(singleStream ?
													 HUF_decompress1X_usingDTable_bmi2(dctx.litBuffer, litSize, istart + lhSize, litCSize, dctx.HUFptr/*, dctx.bmi2*/) :
													 HUF_decompress4X_usingDTable_bmi2(dctx.litBuffer, litSize, istart + lhSize, litCSize, dctx.HUFptr/*, dctx.bmi2*/)) :
												(singleStream ?
													 HUF_decompress1X2_DCtx_wksp_bmi2(dctx.entropy.hufTable, dctx.litBuffer, litSize, istart + lhSize, litCSize,
																					 dctx.entropy.workspace, sizeof(U32) * HUF_DECOMPRESS_WORKSPACE_SIZE_U32/*, dctx.bmi2*/) :

													 HUF_decompress4X_hufOnly_wksp_bmi2(dctx.entropy.hufTable, dctx.litBuffer, litSize, istart + lhSize, litCSize,
																					   dctx.entropy.workspace, sizeof(U32) * HUF_DECOMPRESS_WORKSPACE_SIZE_U32/*, dctx.bmi2*/))))
								return ERROR(Error.corruption_detected);

							dctx.litPtr = dctx.litBuffer;
							dctx.litSize = litSize;
							dctx.litEntropy = 1;
							if (litEncType == symbolEncodingType_e.set_compressed) dctx.HUFptr = dctx.entropy.hufTable;
							memset(dctx.litBuffer + dctx.litSize, 0, WILDCOPY_OVERLENGTH);
							return litCSize + lhSize;
						}

					case symbolEncodingType_e.set_basic:
						{
							size_t litSize, lhSize;
							U32 lhlCode = ((U32)istart[0] >> 2) & 3;
							switch (lhlCode)
							{
								case 0:
								case 2:
								default:   /* note : default is impossible, since lhlCode into [0..3] */
									lhSize = 1;
									litSize = (U32)istart[0] >> 3;
									break;
								case 1:
									lhSize = 2;
									litSize = MEM_readLE16(istart) >> 4;
									break;
								case 3:
									lhSize = 3;
									litSize = MEM_readLE24(istart) >> 4;
									break;
							}

							if (lhSize + litSize + WILDCOPY_OVERLENGTH > srcSize)
							{  /* risk reading beyond src buffer with wildcopy */
								if (litSize + lhSize > srcSize) return ERROR(Error.corruption_detected);
								memcpy(dctx.litBuffer, istart + lhSize, litSize);
								dctx.litPtr = dctx.litBuffer;
								dctx.litSize = litSize;
								memset(dctx.litBuffer + dctx.litSize, 0, WILDCOPY_OVERLENGTH);
								return lhSize + litSize;
							}
							/* direct reference into compressed stream */
							dctx.litPtr = istart + lhSize;
							dctx.litSize = litSize;
							return lhSize + litSize;
						}

					case symbolEncodingType_e.set_rle:
						{
							U32 lhlCode = ((U32)istart[0] >> 2) & 3;
							size_t litSize, lhSize;
							switch (lhlCode)
							{
								case 0:
								case 2:
								default:   /* note : default is impossible, since lhlCode into [0..3] */
									lhSize = 1;
									litSize = (U32)istart[0] >> 3;
									break;
								case 1:
									lhSize = 2;
									litSize = MEM_readLE16(istart) >> 4;
									break;
								case 3:
									lhSize = 3;
									litSize = MEM_readLE24(istart) >> 4;
									if (srcSize < 4) return ERROR(Error.corruption_detected);   /* srcSize >= MIN_CBLOCK_SIZE == 3; here we need lhSize+1 = 4 */
									break;
							}
							if (litSize > ZSTD_BLOCKSIZE_MAX) return ERROR(Error.corruption_detected);
							memset(dctx.litBuffer, istart[lhSize], litSize + WILDCOPY_OVERLENGTH);
							dctx.litPtr = dctx.litBuffer;
							dctx.litSize = litSize;
							return lhSize + 1;
						}
					default:
						return ERROR(Error.corruption_detected);   /* impossible */
				}
			}
		}

		///* Default FSE distribution tables.
		// * These are pre-calculated FSE decoding tables using default distributions as defined in specification :
		// * https://github.com/facebook/zstd/blob/master/doc/zstd_compression_format.md#default-distributions
		// * They were generated programmatically with following method :
		// * - start from default distributions, present in /lib/common/zstd_internal.h
		// * - generate tables normally, using BuildFSETable()
		// * - printout the content of tables
		// * - pretify output, report below, test with fuzzer to ensure it's correct */

		/* Default FSE distribution table for Literal Lengths */
		static readonly SeqSymbol[] LL_defaultDTableArray = new SeqSymbol[(1 << LL_DEFAULTNORMLOG) + 1] {
			 new SeqSymbol(  1,  1,  1,  LL_DEFAULTNORMLOG),  /* header : fastMode, tableLog */
		     /* nextState, nbAddBits, nbBits, baseVal */
		     new SeqSymbol(  0,  0,  4,    0),  new SeqSymbol( 16,  0,  4,    0),
			 new SeqSymbol( 32,  0,  5,    1),  new SeqSymbol(  0,  0,  5,    3),
			 new SeqSymbol(  0,  0,  5,    4),  new SeqSymbol(  0,  0,  5,    6),
			 new SeqSymbol(  0,  0,  5,    7),  new SeqSymbol(  0,  0,  5,    9),
			 new SeqSymbol(  0,  0,  5,   10),  new SeqSymbol(  0,  0,  5,   12),
			 new SeqSymbol(  0,  0,  6,   14),  new SeqSymbol(  0,  1,  5,   16),
			 new SeqSymbol(  0,  1,  5,   20),  new SeqSymbol(  0,  1,  5,   22),
			 new SeqSymbol(  0,  2,  5,   28),  new SeqSymbol(  0,  3,  5,   32),
			 new SeqSymbol(  0,  4,  5,   48),  new SeqSymbol( 32,  6,  5,   64),
			 new SeqSymbol(  0,  7,  5,  128),  new SeqSymbol(  0,  8,  6,  256),
			 new SeqSymbol(  0, 10,  6, 1024),  new SeqSymbol(  0, 12,  6, 4096),
			 new SeqSymbol( 32,  0,  4,    0),  new SeqSymbol(  0,  0,  4,    1),
			 new SeqSymbol(  0,  0,  5,    2),  new SeqSymbol( 32,  0,  5,    4),
			 new SeqSymbol(  0,  0,  5,    5),  new SeqSymbol( 32,  0,  5,    7),
			 new SeqSymbol(  0,  0,  5,    8),  new SeqSymbol( 32,  0,  5,   10),
			 new SeqSymbol(  0,  0,  5,   11),  new SeqSymbol(  0,  0,  6,   13),
			 new SeqSymbol( 32,  1,  5,   16),  new SeqSymbol(  0,  1,  5,   18),
			 new SeqSymbol( 32,  1,  5,   22),  new SeqSymbol(  0,  2,  5,   24),
			 new SeqSymbol( 32,  3,  5,   32),  new SeqSymbol(  0,  3,  5,   40),
			 new SeqSymbol(  0,  6,  4,   64),  new SeqSymbol( 16,  6,  4,   64),
			 new SeqSymbol( 32,  7,  5,  128),  new SeqSymbol(  0,  9,  6,  512),
			 new SeqSymbol(  0, 11,  6, 2048),  new SeqSymbol( 48,  0,  4,    0),
			 new SeqSymbol( 16,  0,  4,    1),  new SeqSymbol( 32,  0,  5,    2),
			 new SeqSymbol( 32,  0,  5,    3),  new SeqSymbol( 32,  0,  5,    5),
			 new SeqSymbol( 32,  0,  5,    6),  new SeqSymbol( 32,  0,  5,    8),
			 new SeqSymbol( 32,  0,  5,    9),  new SeqSymbol( 32,  0,  5,   11),
			 new SeqSymbol( 32,  0,  5,   12),  new SeqSymbol(  0,  0,  6,   15),
			 new SeqSymbol( 32,  1,  5,   18),  new SeqSymbol( 32,  1,  5,   20),
			 new SeqSymbol( 32,  2,  5,   24),  new SeqSymbol( 32,  2,  5,   28),
			 new SeqSymbol( 32,  3,  5,   40),  new SeqSymbol( 32,  4,  5,   48),
			 new SeqSymbol(  0, 16,  6,65536),  new SeqSymbol(  0, 15,  6,32768),
			 new SeqSymbol(  0, 14,  6,16384),  new SeqSymbol(  0, 13,  6, 8192),
		};   /* LL_defaultDTable */

		private static SeqSymbol* LL_defaultDTable = (SeqSymbol*)GCHandle.Alloc(LL_defaultDTableArray, GCHandleType.Pinned).AddrOfPinnedObject();

		/* Default FSE distribution table for Offset Codes */
		static readonly SeqSymbol[] OF_defaultDTableArray = new SeqSymbol[(1 << OF_DEFAULTNORMLOG) + 1] {
			new SeqSymbol(  1,  1,  1,  OF_DEFAULTNORMLOG),  /* header : fastMode, tableLog */
		    /* nextState, nbAddBits, nbBits, baseVal */
		    new SeqSymbol(  0,  0,  5,    0),     new SeqSymbol(  0,  6,  4,   61),
			new SeqSymbol(  0,  9,  5,  509),     new SeqSymbol(  0, 15,  5,32765),
			new SeqSymbol(  0, 21,  5,2097149),   new SeqSymbol(  0,  3,  5,    5),
			new SeqSymbol(  0,  7,  4,  125),     new SeqSymbol(  0, 12,  5, 4093),
			new SeqSymbol(  0, 18,  5,262141),    new SeqSymbol(  0, 23,  5,8388605),
			new SeqSymbol(  0,  5,  5,   29),     new SeqSymbol(  0,  8,  4,  253),
			new SeqSymbol(  0, 14,  5,16381),     new SeqSymbol(  0, 20,  5,1048573),
			new SeqSymbol(  0,  2,  5,    1),     new SeqSymbol( 16,  7,  4,  125),
			new SeqSymbol(  0, 11,  5, 2045),     new SeqSymbol(  0, 17,  5,131069),
			new SeqSymbol(  0, 22,  5,4194301),   new SeqSymbol(  0,  4,  5,   13),
			new SeqSymbol( 16,  8,  4,  253),     new SeqSymbol(  0, 13,  5, 8189),
			new SeqSymbol(  0, 19,  5,524285),    new SeqSymbol(  0,  1,  5,    1),
			new SeqSymbol( 16,  6,  4,   61),     new SeqSymbol(  0, 10,  5, 1021),
			new SeqSymbol(  0, 16,  5,65533),     new SeqSymbol(  0, 28,  5,268435453),
			new SeqSymbol(  0, 27,  5,134217725), new SeqSymbol(  0, 26,  5,67108861),
			new SeqSymbol(  0, 25,  5,33554429),  new SeqSymbol(  0, 24,  5,16777213),
		};   /* OF_defaultDTable */

		private static SeqSymbol* OF_defaultDTable = (SeqSymbol*)GCHandle.Alloc(OF_defaultDTableArray, GCHandleType.Pinned).AddrOfPinnedObject();

		/* Default FSE distribution table for Match Lengths */
		static readonly SeqSymbol[] ML_defaultDTableArray = new SeqSymbol[(1 << ML_DEFAULTNORMLOG) + 1] {
			new SeqSymbol(  1,  1,  1,  ML_DEFAULTNORMLOG),  /* header : fastMode, tableLog */
		    /* nextState, nbAddBits, nbBits, baseVal */
		    new SeqSymbol(  0,  0,  6,    3),  new SeqSymbol(  0,  0,  4,    4),
			new SeqSymbol( 32,  0,  5,    5),  new SeqSymbol(  0,  0,  5,    6),
			new SeqSymbol(  0,  0,  5,    8),  new SeqSymbol(  0,  0,  5,    9),
			new SeqSymbol(  0,  0,  5,   11),  new SeqSymbol(  0,  0,  6,   13),
			new SeqSymbol(  0,  0,  6,   16),  new SeqSymbol(  0,  0,  6,   19),
			new SeqSymbol(  0,  0,  6,   22),  new SeqSymbol(  0,  0,  6,   25),
			new SeqSymbol(  0,  0,  6,   28),  new SeqSymbol(  0,  0,  6,   31),
			new SeqSymbol(  0,  0,  6,   34),  new SeqSymbol(  0,  1,  6,   37),
			new SeqSymbol(  0,  1,  6,   41),  new SeqSymbol(  0,  2,  6,   47),
			new SeqSymbol(  0,  3,  6,   59),  new SeqSymbol(  0,  4,  6,   83),
			new SeqSymbol(  0,  7,  6,  131),  new SeqSymbol(  0,  9,  6,  515),
			new SeqSymbol( 16,  0,  4,    4),  new SeqSymbol(  0,  0,  4,    5),
			new SeqSymbol( 32,  0,  5,    6),  new SeqSymbol(  0,  0,  5,    7),
			new SeqSymbol( 32,  0,  5,    9),  new SeqSymbol(  0,  0,  5,   10),
			new SeqSymbol(  0,  0,  6,   12),  new SeqSymbol(  0,  0,  6,   15),
			new SeqSymbol(  0,  0,  6,   18),  new SeqSymbol(  0,  0,  6,   21),
			new SeqSymbol(  0,  0,  6,   24),  new SeqSymbol(  0,  0,  6,   27),
			new SeqSymbol(  0,  0,  6,   30),  new SeqSymbol(  0,  0,  6,   33),
			new SeqSymbol(  0,  1,  6,   35),  new SeqSymbol(  0,  1,  6,   39),
			new SeqSymbol(  0,  2,  6,   43),  new SeqSymbol(  0,  3,  6,   51),
			new SeqSymbol(  0,  4,  6,   67),  new SeqSymbol(  0,  5,  6,   99),
			new SeqSymbol(  0,  8,  6,  259),  new SeqSymbol( 32,  0,  4,    4),
			new SeqSymbol( 48,  0,  4,    4),  new SeqSymbol( 16,  0,  4,    5),
			new SeqSymbol( 32,  0,  5,    7),  new SeqSymbol( 32,  0,  5,    8),
			new SeqSymbol( 32,  0,  5,   10),  new SeqSymbol( 32,  0,  5,   11),
			new SeqSymbol(  0,  0,  6,   14),  new SeqSymbol(  0,  0,  6,   17),
			new SeqSymbol(  0,  0,  6,   20),  new SeqSymbol(  0,  0,  6,   23),
			new SeqSymbol(  0,  0,  6,   26),  new SeqSymbol(  0,  0,  6,   29),
			new SeqSymbol(  0,  0,  6,   32),  new SeqSymbol(  0, 16,  6,65539),
			new SeqSymbol(  0, 15,  6,32771),  new SeqSymbol(  0, 14,  6,16387),
			new SeqSymbol(  0, 13,  6, 8195),  new SeqSymbol(  0, 12,  6, 4099),
			new SeqSymbol(  0, 11,  6, 2051),  new SeqSymbol(  0, 10,  6, 1027),
		};   /* ML_defaultDTable */

		private static readonly SeqSymbol* ML_defaultDTable = (SeqSymbol*)GCHandle.Alloc(ML_defaultDTableArray, GCHandleType.Pinned).AddrOfPinnedObject();


		static void ZSTD_buildSeqTable_rle(SeqSymbol* dt, U32 baseValue, U32 nbAddBits)
		{
			{
				void* ptr = dt;
				ZSTD_seqSymbol_header* DTableH = (ZSTD_seqSymbol_header*)ptr;
				SeqSymbol* cell = dt + 1;

				DTableH->tableLog = 0;
				DTableH->fastMode = 0;

				cell->nbBits = 0;
				cell->nextState = 0;
				Debug.Assert(nbAddBits < 255);
				cell->nbAdditionalBits = (BYTE)nbAddBits;
				cell->baseValue = baseValue;
			}
		}


		/* BuildFSETable() :
		 * generate FSE decoding table for one symbol (ll, ml or off) */
		static void BuildFSETable(SeqSymbol* dt,
			short[] normalizedCounter, uint maxSymbolValue,
			U32[] baseValue, U32[] nbAdditionalBits,
			uint tableLog)
		{
			{

				SeqSymbol* tableDecode = dt + 1;
				U16[] symbolNext = new U16[MaxSeq + 1];

				U32 maxSV1 = maxSymbolValue + 1;
				U32 tableSize = (U32)1 << (int)tableLog;
				U32 highThreshold = tableSize - 1;

				/* Sanity Checks */
				Debug.Assert(maxSymbolValue <= MaxSeq);
				Debug.Assert(tableLog <= MaxFSELog);

				/* Init, lay down lowprob symbols */
				{
					ZSTD_seqSymbol_header DTableH;
					DTableH.tableLog = tableLog;
					DTableH.fastMode = 1;
					{
						S16 largeLimit = (S16)(1 << (int)(tableLog - 1));
						U32 s;
						for (s = 0; s < maxSV1; s++)
						{
							if (normalizedCounter[s] == -1)
							{
								tableDecode[highThreshold--].baseValue = s;
								symbolNext[s] = 1;
							}
							else
							{
								if (normalizedCounter[s] >= largeLimit) DTableH.fastMode = 0;
								symbolNext[s] = (U16)normalizedCounter[s];
							}
						}
					}
					*(ZSTD_seqSymbol_header*)dt = DTableH; //memcpy(dt, &DTableH, sizeof(ZSTD_seqSymbol_header /*DTableH*/));
				}

				/* Spread symbols */
				{
					U32 tableMask = tableSize - 1;
					U32 step = Fse.FSE_TABLESTEP(tableSize);
					U32 s, position = 0;
					for (s = 0; s < maxSV1; s++)
					{
						int i;
						for (i = 0; i < normalizedCounter[s]; i++)
						{
							tableDecode[position].baseValue = s;
							position = (position + step) & tableMask;
							while (position > highThreshold) position = (position + step) & tableMask; /* lowprob area */
						}
					}
					Debug.Assert(position == 0); /* position must reach all cells once, otherwise normalizedCounter is incorrect */
				}

				/* Build Decoding table */
				{
					U32 u;
					for (u = 0; u < tableSize; u++)
					{
						U32 symbol = tableDecode[u].baseValue;
						U32 nextState = symbolNext[symbol]++;
						tableDecode[u].nbBits = (BYTE)(tableLog - BIT_highbit32(nextState));
						tableDecode[u].nextState = (U16)((nextState << tableDecode[u].nbBits) - tableSize);
						Debug.Assert(nbAdditionalBits[symbol] < 255);
						tableDecode[u].nbAdditionalBits = (BYTE)nbAdditionalBits[symbol];
						tableDecode[u].baseValue = baseValue[symbol];
					}
				}
			}
		}


		/*! BuildSeqTable() :
		 * @return : nb bytes read from src,
		 *           or an error code if it fails */
		static size_t BuildSeqTable(SeqSymbol* DTableSpace, ref SeqSymbol* DTablePtr,
										  symbolEncodingType_e type, U32 max, U32 maxLog,
										 void* src, size_t srcSize,
										 U32[] baseValue, U32[] nbAdditionalBits,
										 SeqSymbol* defaultTable, U32 flagRepeatTable)
		{
			switch (type)
			{
				case symbolEncodingType_e.set_rle:
					if (srcSize == 0) return ERROR(Error.srcSize_wrong);
					if ((*(BYTE*)src) > max) return ERROR(Error.corruption_detected);
					{
						U32 symbol = *(BYTE*)src;
						U32 baseline = baseValue[symbol];
						U32 nbBits = nbAdditionalBits[symbol];
						ZSTD_buildSeqTable_rle(DTableSpace, baseline, nbBits);
					}
					DTablePtr = DTableSpace;
					return 1;
				case symbolEncodingType_e.set_basic:
					DTablePtr = defaultTable;
					return 0;
				case symbolEncodingType_e.set_repeat:
					if (flagRepeatTable == 0) return ERROR(Error.corruption_detected);
					return 0;
				case symbolEncodingType_e.set_compressed:
					{
						U32 tableLog;
						S16[] norm = new S16[MaxSeq + 1];
						size_t headerSize = ReadNCount(norm, &max, &tableLog, src, srcSize);
						if (IsError(headerSize)) return ERROR(Error.corruption_detected);
						if (tableLog > maxLog) return ERROR(Error.corruption_detected);
						BuildFSETable(DTableSpace, norm, max, baseValue, nbAdditionalBits, tableLog);
						DTablePtr = DTableSpace;
						return headerSize;
					}
				default: /* impossible */
					return ERROR(Error.GENERIC);
			}
		}

		static readonly U32[] LL_base = new U32[MaxLL + 1] {
						 0,    1,    2,     3,     4,     5,     6,      7,
						 8,    9,   10,    11,    12,    13,    14,     15,
						16,   18,   20,    22,    24,    28,    32,     40,
						48,   64, 0x80, 0x100, 0x200, 0x400, 0x800, 0x1000,
						0x2000, 0x4000, 0x8000, 0x10000 };

		static readonly U32[] OF_base = new U32[MaxOff + 1] {
						 0,        1,       1,       5,     0xD,     0x1D,     0x3D,     0x7D,
						 0xFD,   0x1FD,   0x3FD,   0x7FD,   0xFFD,   0x1FFD,   0x3FFD,   0x7FFD,
						 0xFFFD, 0x1FFFD, 0x3FFFD, 0x7FFFD, 0xFFFFD, 0x1FFFFD, 0x3FFFFD, 0x7FFFFD,
						 0xFFFFFD, 0x1FFFFFD, 0x3FFFFFD, 0x7FFFFFD, 0xFFFFFFD, 0x1FFFFFFD, 0x3FFFFFFD, 0x7FFFFFFD };

		static readonly U32[] OF_bits = new U32[MaxOff + 1] {
							 0,  1,  2,  3,  4,  5,  6,  7,
							 8,  9, 10, 11, 12, 13, 14, 15,
							16, 17, 18, 19, 20, 21, 22, 23,
							24, 25, 26, 27, 28, 29, 30, 31 };

		static readonly U32[] ML_base = new U32[MaxML + 1] {
							 3,  4,  5,    6,     7,     8,     9,    10,
							11, 12, 13,   14,    15,    16,    17,    18,
							19, 20, 21,   22,    23,    24,    25,    26,
							27, 28, 29,   30,    31,    32,    33,    34,
							35, 37, 39,   41,    43,    47,    51,    59,
							67, 83, 99, 0x83, 0x103, 0x203, 0x403, 0x803,
							0x1003, 0x2003, 0x4003, 0x8003, 0x10003 };


		static size_t DecodeSeqHeaders(ZSTD_DCtx dctx, int* nbSeqPtr, void* src, size_t srcSize)
		{
			BYTE* istart = (BYTE*)src;
			BYTE* iend = istart + srcSize;
			BYTE* ip = istart;

			/* check */
			if (srcSize < MIN_SEQUENCES_SIZE) return ERROR(Error.srcSize_wrong);

			/* SeqHead */
			{
				int nbSeq = *ip++;
				if (nbSeq == 0) { *nbSeqPtr = 0; return 1; }
				if (nbSeq > 0x7F)
				{
					if (nbSeq == 0xFF)
					{
						if (ip + 2 > iend) return ERROR(Error.srcSize_wrong);
						nbSeq = (int)MEM_readLE16(ip) + LONGNBSEQ; ip += 2;
					}
					else
					{
						if (ip >= iend) return ERROR(Error.srcSize_wrong);
						nbSeq = ((nbSeq - 0x80) << 8) + *ip++;
					}
				}
				*nbSeqPtr = nbSeq;
			}

			/* FSE table descriptors */
			if (ip + 4 > iend) return ERROR(Error.srcSize_wrong); /* minimum possible size */
			{
				symbolEncodingType_e LLtype = (symbolEncodingType_e)(*ip >> 6);
				symbolEncodingType_e OFtype = (symbolEncodingType_e)((*ip >> 4) & 3);
				symbolEncodingType_e MLtype = (symbolEncodingType_e)((*ip >> 2) & 3);
				ip++;

				/* Build DTables */
				{
					size_t llhSize = BuildSeqTable(dctx.entropy.LLTable, ref dctx.LLTptr,
															  LLtype, MaxLL, LLFSELog,
															  ip, (size_t)(iend - ip),
															  LL_base, LL_bits,
															  LL_defaultDTable, dctx.fseEntropy);
					if (IsError(llhSize)) return ERROR(Error.corruption_detected);
					ip += llhSize;
				}

				{
					size_t ofhSize = BuildSeqTable(dctx.entropy.OFTable, ref dctx.OFTptr,
															  OFtype, MaxOff, OffFSELog,
															  ip, (size_t)(iend - ip),
															  OF_base, OF_bits,
															  OF_defaultDTable, dctx.fseEntropy);
					if (IsError(ofhSize)) return ERROR(Error.corruption_detected);
					ip += ofhSize;
				}

				{
					size_t mlhSize = BuildSeqTable(dctx.entropy.MLTable, ref dctx.MLTptr,
															  MLtype, MaxML, MLFSELog,
															  ip, (size_t)(iend - ip),
															  ML_base, ML_bits,
															  ML_defaultDTable, dctx.fseEntropy);
					if (IsError(mlhSize)) return ERROR(Error.corruption_detected);
					ip += mlhSize;
				}
			}

			return (size_t)(ip - istart);
		}


		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
		struct seq_t
		{
			public size_t litLength;
			public size_t matchLength;
			public size_t offset;
			public BYTE* match;
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
		struct ZSTD_fseState
		{
			public size_t state;
			public SeqSymbol* table;
		}

		class seqState_t
		{
			public BIT_DStream_t DStream = new BIT_DStream_t();
			public ZSTD_fseState stateLL;
			public ZSTD_fseState stateOffb;
			public ZSTD_fseState stateML;
			public size_t[] prevOffset = new size_t[ZSTD_REP_NUM];
			public BYTE* prefixStart;
			public BYTE* dictEnd;
			public size_t pos;
		}


		static size_t ZSTD_execSequenceLast7(BYTE* op,
									  BYTE* oend, seq_t sequence,
									  BYTE** litPtr, BYTE* litLimit,
									  BYTE* baseField, BYTE* vBase, BYTE* dictEnd)
		{
			BYTE* oLitEnd = op + sequence.litLength;
			size_t sequenceLength = sequence.litLength + sequence.matchLength;
			BYTE* oMatchEnd = op + sequenceLength;   /* risk : address space overflow (32-bits) */
			BYTE* oend_w = oend - WILDCOPY_OVERLENGTH;
			BYTE* iLitEnd = *litPtr + sequence.litLength;
			BYTE* match = oLitEnd - sequence.offset;

			/* check */
			if (oMatchEnd > oend) return ERROR(Error.dstSize_tooSmall); /* last match must start at a minimum distance of  WILDCOPY_OVERLENGTH from oend */
			if (iLitEnd > litLimit) return ERROR(Error.corruption_detected);   /* over-read beyond lit buffer */
			if (oLitEnd <= oend_w) return ERROR(Error.GENERIC);   /* Precondition */

			/* copy literals */
			if (op < oend_w)
			{
				Wildcopy(op, *litPtr, (int)(oend_w - op));
				*litPtr += oend_w - op;
				op = oend_w;
			}
			while (op < oLitEnd) *op++ = *(*litPtr)++;

			/* copy Match */
			if (sequence.offset > (size_t)(oLitEnd - baseField))
			{
				/* offset beyond prefix */
				if (sequence.offset > (size_t)(oLitEnd - vBase)) return ERROR(Error.corruption_detected);
				match = dictEnd - (baseField - match);
				if (match + sequence.matchLength <= dictEnd)
				{
					memmove(oLitEnd, match, sequence.matchLength);
					return sequenceLength;
				}
				/* span extDict & currentPrefixSegment */
				{
					size_t length1 = (size_t)(dictEnd - match);
					memmove(oLitEnd, match, length1);
					op = oLitEnd + length1;
					sequence.matchLength -= length1;
					match = baseField;
				}
			}
			while (op < oMatchEnd) *op++ = *match++;
			return sequenceLength;
		}

		static readonly U32[] dec32table = new U32[] { 0, 1, 2, 1, 4, 4, 4, 4 };   /* added */
		static readonly int[] dec64table = new int[] { 8, 8, 8, 7, 8, 9, 10, 11 };   /* subtracted */

		static size_t ExecSequence(BYTE* op,
								 BYTE* oend, seq_t sequence,
								 BYTE** litPtr, BYTE* litLimit,
								 BYTE* baseField, BYTE* vBase, BYTE* dictEnd)
		{
			BYTE* oLitEnd = op + sequence.litLength;
			size_t sequenceLength = sequence.litLength + sequence.matchLength;
			BYTE* oMatchEnd = op + sequenceLength;   /* risk : address space overflow (32-bits) */
			BYTE* oend_w = oend - WILDCOPY_OVERLENGTH;
			BYTE* iLitEnd = *litPtr + sequence.litLength;
			BYTE* match = oLitEnd - sequence.offset;

			/* check */
			if (oMatchEnd > oend) return ERROR(Error.dstSize_tooSmall); /* last match must start at a minimum distance of  WILDCOPY_OVERLENGTH from oend */
			if (iLitEnd > litLimit) return ERROR(Error.corruption_detected);   /* over-read beyond lit buffer */
			if (oLitEnd > oend_w) return ZSTD_execSequenceLast7(op, oend, sequence, litPtr, litLimit, baseField, vBase, dictEnd);

			/* copy Literals */
			ZSTD_copy8(op, *litPtr);
			if (sequence.litLength > 8)
				Wildcopy(op + 8, (*litPtr) + 8, (int)(sequence.litLength - 8));   /* note : since oLitEnd <= oend- WILDCOPY_OVERLENGTH, no risk of overwrite beyond oend */
			op = oLitEnd;
			*litPtr = iLitEnd;   /* update for next sequence */

			/* copy Match */
			if (sequence.offset > (size_t)(oLitEnd - baseField))
			{
				/* offset beyond prefix -> go into extDict */
				if (sequence.offset > (size_t)(oLitEnd - vBase))
					return ERROR(Error.corruption_detected);
				match = dictEnd + (match - baseField);
				if (match + sequence.matchLength <= dictEnd)
				{
					memmove(oLitEnd, match, sequence.matchLength);
					return sequenceLength;
				}
				/* span extDict & currentPrefixSegment */
				{
					size_t length1 = (size_t)(dictEnd - match);
					memmove(oLitEnd, match, length1);
					op = oLitEnd + length1;
					sequence.matchLength -= length1;
					match = baseField;
					if (op > oend_w || sequence.matchLength < MINMATCH)
					{
						U32 i;
						for (i = 0; i < sequence.matchLength; ++i) op[i] = match[i];
						return sequenceLength;
					}
				}
			}
			/* Requirement: op <= oend_w && sequence.matchLength >= MINMATCH */

			/* match within prefix */
			if (sequence.offset < 8)
			{
				/* close range match, overlap */
				int sub2 = dec64table[sequence.offset];
				op[0] = match[0];
				op[1] = match[1];
				op[2] = match[2];
				op[3] = match[3];
				match += dec32table[sequence.offset];
				ZSTD_copy4(op + 4, match);
				match -= sub2;
			}
			else
			{
				ZSTD_copy8(op, match);
			}
			op += 8; match += 8;

			if (oMatchEnd > oend - (16 - MINMATCH))
			{
				if (op < oend_w)
				{
					Wildcopy(op, match, (int)(oend_w - op));
					match += oend_w - op;
					op = oend_w;
				}
				while (op < oMatchEnd) *op++ = *match++;
			}
			else
			{
				Wildcopy(op, match, (int)(sequence.matchLength - 8));   /* works even if matchLength < 8 */
			}
			return sequenceLength;
		}


		static size_t ExecSequenceLong(BYTE* op,
									 BYTE* oend, seq_t sequence,
									 BYTE** litPtr, BYTE* litLimit,
									 BYTE* prefixStart, BYTE* dictStart, BYTE* dictEnd)
		{
			BYTE* oLitEnd = op + sequence.litLength;
			size_t sequenceLength = sequence.litLength + sequence.matchLength;
			BYTE* oMatchEnd = op + sequenceLength;   /* risk : address space overflow (32-bits) */
			BYTE* oend_w = oend - WILDCOPY_OVERLENGTH;
			BYTE* iLitEnd = *litPtr + sequence.litLength;
			BYTE* match = sequence.match;

			/* check */
			if (oMatchEnd > oend) return ERROR(Error.dstSize_tooSmall); /* last match must start at a minimum distance of  WILDCOPY_OVERLENGTH from oend */
			if (iLitEnd > litLimit) return ERROR(Error.corruption_detected);   /* over-read beyond lit buffer */
			if (oLitEnd > oend_w) return ZSTD_execSequenceLast7(op, oend, sequence, litPtr, litLimit, prefixStart, dictStart, dictEnd);

			/* copy Literals */
			ZSTD_copy8(op, *litPtr);  /* note : op <= oLitEnd <= oend_w == oend - 8 */
			if (sequence.litLength > 8)
				Wildcopy(op + 8, (*litPtr) + 8, (int)(sequence.litLength - 8));   /* note : since oLitEnd <= oend- WILDCOPY_OVERLENGTH, no risk of overwrite beyond oend */
			op = oLitEnd;
			*litPtr = iLitEnd;   /* update for next sequence */

			/* copy Match */
			if (sequence.offset > (size_t)(oLitEnd - prefixStart))
			{
				/* offset beyond prefix */
				if (sequence.offset > (size_t)(oLitEnd - dictStart)) return ERROR(Error.corruption_detected);
				if (match + sequence.matchLength <= dictEnd)
				{
					memmove(oLitEnd, match, sequence.matchLength);
					return sequenceLength;
				}
				/* span extDict & currentPrefixSegment */
				{
					size_t length1 = (size_t)(dictEnd - match);
					memmove(oLitEnd, match, length1);
					op = oLitEnd + length1;
					sequence.matchLength -= length1;
					match = prefixStart;
					if (op > oend_w || sequence.matchLength < MINMATCH)
					{
						U32 i;
						for (i = 0; i < sequence.matchLength; ++i) op[i] = match[i];
						return sequenceLength;
					}
				}
			}
			Debug.Assert(op <= oend_w);
			Debug.Assert(sequence.matchLength >= MINMATCH);

			/* match within prefix */
			if (sequence.offset < 8)
			{
				/* close range match, overlap */
				int sub2 = dec64table[sequence.offset];
				op[0] = match[0];
				op[1] = match[1];
				op[2] = match[2];
				op[3] = match[3];
				match += dec32table[sequence.offset];
				ZSTD_copy4(op + 4, match);
				match -= sub2;
			}
			else
			{
				ZSTD_copy8(op, match);
			}
			op += 8; match += 8;

			if (oMatchEnd > oend - (16 - MINMATCH))
			{
				if (op < oend_w)
				{
					Wildcopy(op, match, (int)(oend_w - op));
					match += oend_w - op;
					op = oend_w;
				}
				while (op < oMatchEnd) *op++ = *match++;
			}
			else
			{
				Wildcopy(op, match, (int)(sequence.matchLength - 8));   /* works even if matchLength < 8 */
			}
			return sequenceLength;
		}

		static void InitFseState(ref ZSTD_fseState DStatePtr, BIT_DStream_t bitD, SeqSymbol* dt)
		{
			{
				void* ptr = dt;
				ZSTD_seqSymbol_header* DTableH = (ZSTD_seqSymbol_header*)ptr;
				DStatePtr.state = ReadBits(bitD, DTableH->tableLog);
				ReloadDStream(bitD);
				DStatePtr.table = dt + 1;
			}
		}

		static void UpdateFseState(ref ZSTD_fseState DStatePtr, BIT_DStream_t bitD)
		{
			SeqSymbol DInfo = DStatePtr.table[DStatePtr.state];
			U32 nbBits = DInfo.nbBits;
			size_t lowBits = ReadBits(bitD, nbBits);
			DStatePtr.state = DInfo.nextState + lowBits;
		}

		///* We need to add at most (ZSTD_WINDOWLOG_MAX_32 - 1) bits to read the maximum
		// * offset bits. But we can only read at most (STREAM_ACCUMULATOR_MIN_32 - 1)
		// * bits before reloading. This value is the maximum number of bytes we read
		// * after reloading when we are decoding long offets.
		// */
		internal const uint LONG_OFFSETS_MAX_EXTRA_BITS_32 = (ZSTD_WINDOWLOG_MAX_32 > STREAM_ACCUMULATOR_MIN_32
			? ZSTD_WINDOWLOG_MAX_32 - STREAM_ACCUMULATOR_MIN_32
			: 0);

		enum ZSTD_longOffset_e : int { ZSTD_lo_isRegularOffset, ZSTD_lo_isLongOffset = 1 }

		static seq_t DecodeSequence(seqState_t seqState, ZSTD_longOffset_e longOffsets)
		{
			seq_t seq = new seq_t();
			U32 llBits = seqState.stateLL.table[seqState.stateLL.state].nbAdditionalBits;
			U32 mlBits = seqState.stateML.table[seqState.stateML.state].nbAdditionalBits;
			U32 ofBits = seqState.stateOffb.table[seqState.stateOffb.state].nbAdditionalBits;
			U32 totalBits = llBits + mlBits + ofBits;
			U32 llBase = seqState.stateLL.table[seqState.stateLL.state].baseValue;
			U32 mlBase = seqState.stateML.table[seqState.stateML.state].baseValue;
			U32 ofBase = seqState.stateOffb.table[seqState.stateOffb.state].baseValue;

			/* sequence */
			{
				size_t offset;
				if (ofBits == 0)
					offset = 0;
				else
				{
					//ZSTD_STATIC_ASSERT(ZSTD_lo_isLongOffset == 1);
					//ZSTD_STATIC_ASSERT(LONG_OFFSETS_MAX_EXTRA_BITS_32 == 5);
					Debug.Assert(ofBits <= MaxOff);
					if (MEM_32bits() && longOffsets != 0 && (ofBits >= STREAM_ACCUMULATOR_MIN_32))
					{
						U32 extraBits = ofBits - Math.Min(ofBits, 32 - seqState.DStream.bitsConsumed);
						offset = ofBase + (ReadBitsFast(seqState.DStream, ofBits - extraBits) << (int)extraBits);
						ReloadDStream(seqState.DStream);
						if (extraBits != 0) offset += ReadBitsFast(seqState.DStream, extraBits);
						Debug.Assert(extraBits <= LONG_OFFSETS_MAX_EXTRA_BITS_32);   /* to avoid another reload */
					}
					else
					{
						offset = ofBase + ReadBitsFast(seqState.DStream, ofBits/*>0*/);   /* <=  (ZSTD_WINDOWLOG_MAX-1) bits */
						if (MEM_32bits()) ReloadDStream(seqState.DStream);
					}
				}

				if (ofBits <= 1)
				{
					offset += (llBase == 0 ? (size_t)1 : 0);
					if (offset != 0)
					{
						size_t temp = (offset == 3) ? seqState.prevOffset[0] - 1 : seqState.prevOffset[offset];
						temp += temp == 0 ? (size_t)1 : 0;   /* 0 is not valid; input is corrupted; force offset to 1 */
						if (offset != 1) seqState.prevOffset[2] = seqState.prevOffset[1];
						seqState.prevOffset[1] = seqState.prevOffset[0];
						seqState.prevOffset[0] = offset = temp;
					}
					else
					{  /* offset == 0 */
						offset = seqState.prevOffset[0];
					}
				}
				else
				{
					seqState.prevOffset[2] = seqState.prevOffset[1];
					seqState.prevOffset[1] = seqState.prevOffset[0];
					seqState.prevOffset[0] = offset;
				}
				seq.offset = offset;
			}

			seq.matchLength = mlBase + ((mlBits > 0) ? ReadBitsFast(seqState.DStream, mlBits/*>0*/) : 0);  /* <=  16 bits */
			if (MEM_32bits() && (mlBits + llBits >= STREAM_ACCUMULATOR_MIN_32 - LONG_OFFSETS_MAX_EXTRA_BITS_32))
				ReloadDStream(seqState.DStream);
			if (MEM_64bits() && (totalBits >= STREAM_ACCUMULATOR_MIN_64 - (LLFSELog + MLFSELog + OffFSELog)))
				ReloadDStream(seqState.DStream);
			/* Ensure there are enough bits to read the rest of data in 64-bit mode. */
			//ZSTD_STATIC_ASSERT(16 +  LLFSELog +  MLFSELog +  OffFSELog < STREAM_ACCUMULATOR_MIN_64);

			seq.litLength = llBase + ((llBits > 0) ? ReadBitsFast(seqState.DStream, llBits/*>0*/) : 0);    /* <=  16 bits */
			if (MEM_32bits())
				ReloadDStream(seqState.DStream);

			/* ANS state update */
			UpdateFseState(ref seqState.stateLL, seqState.DStream);    /* <=  9 bits */
			UpdateFseState(ref seqState.stateML, seqState.DStream);    /* <=  9 bits */
			if (MEM_32bits()) ReloadDStream(seqState.DStream);    /* <= 18 bits */
			UpdateFseState(ref seqState.stateOffb, seqState.DStream);  /* <=  8 bits */

			return seq;
		}

		static size_t ZSTD_decompressSequences_body(ZSTD_DCtx dctx,
									   void* dst, size_t maxDstSize,
								 void* seqStart, size_t seqSize, int nbSeq,
								 ZSTD_longOffset_e isLongOffset)
		{
			BYTE* ip = (BYTE*)seqStart;
			BYTE* iend = ip + seqSize;
			BYTE* ostart = (BYTE*)dst;
			BYTE* oend = ostart + maxDstSize;
			BYTE* op = ostart;
			BYTE* litPtr = dctx.litPtr;
			BYTE* litEnd = litPtr + dctx.litSize;
			BYTE* baseField = (BYTE*)(dctx.baseField);
			BYTE* vBase = (BYTE*)(dctx.vBase);
			BYTE* dictEnd = (BYTE*)(dctx.dictEnd);

			/* Regen sequences */
			if (nbSeq != 0)
			{
				seqState_t seqState = new seqState_t();
				dctx.fseEntropy = 1;
				{ U32 i; for (i = 0; i < ZSTD_REP_NUM; i++) seqState.prevOffset[i] = dctx.entropy.rep[i]; }
				{ size_t errcod = InitDStream(seqState.DStream, ip, (size_t)(iend - ip)); if (IsError(errcod)) return ERROR(Error.corruption_detected); }
				InitFseState(ref seqState.stateLL, seqState.DStream, dctx.LLTptr);
				InitFseState(ref seqState.stateOffb, seqState.DStream, dctx.OFTptr);
				InitFseState(ref seqState.stateML, seqState.DStream, dctx.MLTptr);

				for (; (ReloadDStream(seqState.DStream) <= BIT_DStream_status.BIT_DStream_completed) && nbSeq != 0;)
				{
					nbSeq--;
					{
						seq_t sequence = DecodeSequence(seqState, isLongOffset);
						size_t oneSeqSize = ExecSequence(op, oend, sequence, &litPtr, litEnd, baseField, vBase, dictEnd);
						if (IsError(oneSeqSize)) return oneSeqSize;
						op += oneSeqSize;
					}
				}

				/* check if reached exact end */
				if (nbSeq != 0) return ERROR(Error.corruption_detected);
				/* save reps for next block */
				{ U32 i; for (i = 0; i < ZSTD_REP_NUM; i++) dctx.entropy.rep[i] = (U32)(seqState.prevOffset[i]); }
			}

			/* last literal segment */
			{
				size_t lastLLSize = (size_t)(litEnd - litPtr);
				if (lastLLSize > (size_t)(oend - op)) return ERROR(Error.dstSize_tooSmall);
				memcpy(op, litPtr, lastLLSize);
				op += lastLLSize;
			}

			return (size_t)(op - ostart);
		}

		static size_t ZSTD_decompressSequences_default(ZSTD_DCtx dctx,
										 void* dst, size_t maxDstSize,
								   void* seqStart, size_t seqSize, int nbSeq,
								   ZSTD_longOffset_e isLongOffset)
		{
			return ZSTD_decompressSequences_body(dctx, dst, maxDstSize, seqStart, seqSize, nbSeq, isLongOffset);
		}



		static seq_t DecodeSequenceLong(seqState_t seqState, ZSTD_longOffset_e longOffsets)
		{
			seq_t seq;
			U32 llBits = seqState.stateLL.table[seqState.stateLL.state].nbAdditionalBits;
			U32 mlBits = seqState.stateML.table[seqState.stateML.state].nbAdditionalBits;
			U32 ofBits = seqState.stateOffb.table[seqState.stateOffb.state].nbAdditionalBits;
			U32 totalBits = llBits + mlBits + ofBits;
			U32 llBase = seqState.stateLL.table[seqState.stateLL.state].baseValue;
			U32 mlBase = seqState.stateML.table[seqState.stateML.state].baseValue;
			U32 ofBase = seqState.stateOffb.table[seqState.stateOffb.state].baseValue;

			/* sequence */
			{
				size_t offset;
				if (ofBits == 0)
					offset = 0;
				else
				{
					//ZSTD_STATIC_ASSERT(ZSTD_lo_isLongOffset == 1);
					//ZSTD_STATIC_ASSERT(LONG_OFFSETS_MAX_EXTRA_BITS_32 == 5);
					Debug.Assert(ofBits <= MaxOff);
					if (MEM_32bits() && longOffsets != 0)
					{
						U32 extraBits = ofBits - Math.Min(ofBits, STREAM_ACCUMULATOR_MIN_32 - 1);
						offset = ofBase + (ReadBitsFast(seqState.DStream, ofBits - extraBits) << (int)extraBits);
						if (MEM_32bits() || extraBits != 0) ReloadDStream(seqState.DStream);
						if (extraBits != 0) offset += ReadBitsFast(seqState.DStream, extraBits);
					}
					else
					{
						offset = ofBase + ReadBitsFast(seqState.DStream, ofBits);   /* <=  (ZSTD_WINDOWLOG_MAX-1) bits */
						if (MEM_32bits()) ReloadDStream(seqState.DStream);
					}
				}

				if (ofBits <= 1)
				{
					offset += (llBase == 0 ? (size_t)1 : 0);
					if (offset != 0)
					{
						size_t temp = (offset == 3) ? seqState.prevOffset[0] - 1 : seqState.prevOffset[offset];
						temp += temp == 0 ? (size_t)1 : 0;   /* 0 is not valid; input is corrupted; force offset to 1 */
						if (offset != 1) seqState.prevOffset[2] = seqState.prevOffset[1];
						seqState.prevOffset[1] = seqState.prevOffset[0];
						seqState.prevOffset[0] = offset = temp;
					}
					else
					{
						offset = seqState.prevOffset[0];
					}
				}
				else
				{
					seqState.prevOffset[2] = seqState.prevOffset[1];
					seqState.prevOffset[1] = seqState.prevOffset[0];
					seqState.prevOffset[0] = offset;
				}
				seq.offset = offset;
			}

			seq.matchLength = mlBase + ((mlBits > 0) ? ReadBitsFast(seqState.DStream, mlBits) : 0);  /* <=  16 bits */
			if (MEM_32bits() && (mlBits + llBits >= STREAM_ACCUMULATOR_MIN_32 - LONG_OFFSETS_MAX_EXTRA_BITS_32))
				ReloadDStream(seqState.DStream);
			if (MEM_64bits() && (totalBits >= STREAM_ACCUMULATOR_MIN_64 - (LLFSELog + MLFSELog + OffFSELog)))
				ReloadDStream(seqState.DStream);
			/* Verify that there is enough bits to read the rest of the data in 64-bit mode. */
			//ZSTD_STATIC_ASSERT(16 + LLFSELog + MLFSELog + OffFSELog < STREAM_ACCUMULATOR_MIN_64);

			seq.litLength = llBase + ((llBits > 0) ? ReadBitsFast(seqState.DStream, llBits) : 0);    /* <=  16 bits */
			if (MEM_32bits())
				ReloadDStream(seqState.DStream);

			{
				size_t pos = seqState.pos + seq.litLength;
				BYTE* matchBase = (seq.offset > pos) ? seqState.dictEnd : seqState.prefixStart;
				seq.match = matchBase + pos - seq.offset;  /* note : this operation can overflow when seq.offset is really too large, which can only happen when input is corrupted.
		                                                    * No consequence though : no memory access will occur, overly large offset will be detected in ExecSequenceLong() */
				seqState.pos = pos + seq.matchLength;
			}

			/* ANS state update */
			UpdateFseState(ref seqState.stateLL, seqState.DStream);    /* <=  9 bits */
			UpdateFseState(ref seqState.stateML, seqState.DStream);    /* <=  9 bits */
			if (MEM_32bits()) ReloadDStream(seqState.DStream);    /* <= 18 bits */
			UpdateFseState(ref seqState.stateOffb, seqState.DStream);  /* <=  8 bits */

			return seq;
		}

		static size_t ZSTD_decompressSequencesLong_body(
									   ZSTD_DCtx dctx,
									   void* dst, size_t maxDstSize,
								 void* seqStart, size_t seqSize, int nbSeq,
								 ZSTD_longOffset_e isLongOffset)
		{
			BYTE* ip = (BYTE*)seqStart;
			BYTE* iend = ip + seqSize;
			BYTE* ostart = (BYTE*)dst;
			BYTE* oend = ostart + maxDstSize;
			BYTE* op = ostart;
			BYTE* litPtr = dctx.litPtr;
			BYTE* litEnd = litPtr + dctx.litSize;
			BYTE* prefixStart = (BYTE*)(dctx.baseField);
			BYTE* dictStart = (BYTE*)(dctx.vBase);
			BYTE* dictEnd = (BYTE*)(dctx.dictEnd);

			/* Regen sequences */
			if (nbSeq != 0)
			{
				const int STORED_SEQS = 4;
				const int STOSEQ_MASK = (STORED_SEQS - 1);
				const int ADVANCED_SEQS = 4;
				seq_t[] sequences = new seq_t[STORED_SEQS];
				int seqAdvance = Math.Min(nbSeq, ADVANCED_SEQS);
				seqState_t seqState = new seqState_t();
				int seqNb;
				dctx.fseEntropy = 1;
				{ U32 i; for (i = 0; i < ZSTD_REP_NUM; i++) seqState.prevOffset[i] = dctx.entropy.rep[i]; }
				seqState.prefixStart = prefixStart;
				seqState.pos = (size_t)(op - prefixStart);
				seqState.dictEnd = dictEnd;
				{ size_t errcod = InitDStream(seqState.DStream, ip, (size_t)(iend - ip)); if (IsError(errcod)) return ERROR(Error.corruption_detected); }
				InitFseState(ref seqState.stateLL, seqState.DStream, dctx.LLTptr);
				InitFseState(ref seqState.stateOffb, seqState.DStream, dctx.OFTptr);
				InitFseState(ref seqState.stateML, seqState.DStream, dctx.MLTptr);

				/* prepare in advance */
				for (seqNb = 0; (ReloadDStream(seqState.DStream) <= BIT_DStream_status.BIT_DStream_completed) && (seqNb < seqAdvance); seqNb++)
				{
					sequences[seqNb] = DecodeSequenceLong(seqState, isLongOffset);
				}
				if (seqNb < seqAdvance) return ERROR(Error.corruption_detected);

				/* decode and decompress */
				for (; (ReloadDStream(seqState.DStream) <= BIT_DStream_status.BIT_DStream_completed) && (seqNb < nbSeq); seqNb++)
				{
					seq_t sequence = DecodeSequenceLong(seqState, isLongOffset);
					size_t oneSeqSize = ExecSequenceLong(op, oend, sequences[(seqNb - ADVANCED_SEQS) & STOSEQ_MASK], &litPtr, litEnd, prefixStart, dictStart, dictEnd);
					if (IsError(oneSeqSize)) return oneSeqSize;
					//PREFETCH(sequence.match);  /* note : it's safe to invoke PREFETCH() on any memory address, including invalid ones */
					sequences[seqNb & STOSEQ_MASK] = sequence;
					op += oneSeqSize;
				}
				if (seqNb < nbSeq) return ERROR(Error.corruption_detected);

				/* finish queue */
				seqNb -= seqAdvance;
				for (; seqNb < nbSeq; seqNb++)
				{
					size_t oneSeqSize = ExecSequenceLong(op, oend, sequences[seqNb & STOSEQ_MASK], &litPtr, litEnd, prefixStart, dictStart, dictEnd);
					if (IsError(oneSeqSize)) return oneSeqSize;
					op += oneSeqSize;
				}

				/* save reps for next block */
				{ U32 i; for (i = 0; i < ZSTD_REP_NUM; i++) dctx.entropy.rep[i] = (U32)(seqState.prevOffset[i]); }
			}

			/* last literal segment */
			{
				size_t lastLLSize = (size_t)(litEnd - litPtr);
				if (lastLLSize > (size_t)(oend - op)) return ERROR(Error.dstSize_tooSmall);
				memcpy(op, litPtr, lastLLSize);
				op += lastLLSize;
			}

			return (size_t)(op - ostart);
		}

		static size_t ZSTD_decompressSequencesLong_default(ZSTD_DCtx dctx,
										 void* dst, size_t maxDstSize,
								   void* seqStart, size_t seqSize, int nbSeq,
								   ZSTD_longOffset_e isLongOffset)
		{
			return ZSTD_decompressSequencesLong_body(dctx, dst, maxDstSize, seqStart, seqSize, nbSeq, isLongOffset);
		}



		//#if DYNAMIC_BMI2

		//static TARGET_ATTRIBUTE("bmi2") size_t
		//ZSTD_decompressSequences_bmi2(ZSTD_DCtx dctx,
		//                                 void* dst, size_t maxDstSize,
		//                           void* seqStart, size_t seqSize, int nbSeq,
		//                            ZSTD_longOffset_e isLongOffset)
		//{
		//    return ZSTD_decompressSequences_body(dctx, dst, maxDstSize, seqStart, seqSize, nbSeq, isLongOffset);
		//}

		//static TARGET_ATTRIBUTE("bmi2") size_t
		//ZSTD_decompressSequencesLong_bmi2(ZSTD_DCtx dctx,
		//                                 void* dst, size_t maxDstSize,
		//                           void* seqStart, size_t seqSize, int nbSeq,
		//                            ZSTD_longOffset_e isLongOffset)
		//{
		//    return ZSTD_decompressSequencesLong_body(dctx, dst, maxDstSize, seqStart, seqSize, nbSeq, isLongOffset);
		//}

		//#endif

		//typedef size_t (*ZSTD_decompressSequences_t)(
		//    ZSTD_DCtxdctx, void *dst, size_t maxDstSize,
		//     void *seqStart, size_t seqSize, int nbSeq,
		//     ZSTD_longOffset_e isLongOffset);

		static size_t DecompressSequences(ZSTD_DCtx dctx, void* dst, size_t maxDstSize,
										void* seqStart, size_t seqSize, int nbSeq,
										ZSTD_longOffset_e isLongOffset)
		{
			return ZSTD_decompressSequences_default(dctx, dst, maxDstSize, seqStart, seqSize, nbSeq, isLongOffset);
		}

		static size_t DecompressSequencesLong(ZSTD_DCtx dctx,
										void* dst, size_t maxDstSize,
										void* seqStart, size_t seqSize, int nbSeq,
										ZSTD_longOffset_e isLongOffset)
		{
			return ZSTD_decompressSequencesLong_default(dctx, dst, maxDstSize, seqStart, seqSize, nbSeq, isLongOffset);
		}

		/* GetLongOffsetsShare() :
		 * condition : offTable must be valid
		 * @return : "share" of long offsets (arbitrarily defined as > (1<<23))
		 *           compared to maximum possible of (1<< OffFSELog) */
		static uint GetLongOffsetsShare(SeqSymbol* offTable)
		{
			{
				void* ptr = offTable;
				U32 tableLog = ((ZSTD_seqSymbol_header*)ptr)[0].tableLog;
				SeqSymbol* table = offTable + 1;
				U32 max = (U32)1 << (int)tableLog;
				U32 u, total = 0;

				Debug.Assert(max <= (1 << OffFSELog)); /* max not too large */
				for (u = 0; u < max; u++)
				{
					if (table[u].nbAdditionalBits > 22) total += 1;
				}

				Debug.Assert(tableLog <= OffFSELog);
				total <<= (int)(OffFSELog - tableLog); /* scale to  OffFSELog */

				return total;
			}
		}


		static size_t ZSTD_decompressBlock_internal(ZSTD_DCtx dctx,
									void* dst, size_t dstCapacity,
							  void* src, size_t srcSize, int frame)
		{   /* blockType == blockCompressed */
			BYTE* ip = (BYTE*)src;
			/* isLongOffset must be true if there are long offsets.
			 * Offsets are long if they are larger than 2^STREAM_ACCUMULATOR_MIN.
			 * We don't expect that to be the case in 64-bit mode.
			 * In block mode, window size is not known, so we have to be conservative. (note: but it could be evaluated from current-lowLimit)
			 */
			ZSTD_longOffset_e isLongOffset = (ZSTD_longOffset_e)(MEM_32bits() && (frame == 0 || dctx.fParams.windowSize > ((U64)1 << (int)STREAM_ACCUMULATOR_MIN)) ? 1 : 0);

			if (srcSize >= ZSTD_BLOCKSIZE_MAX) return ERROR(Error.srcSize_wrong);

			/* Decode literals section */
			{
				size_t litCSize = DecodeLiteralsBlock(dctx, src, srcSize);
				if (IsError(litCSize)) return litCSize;
				ip += litCSize;
				srcSize -= litCSize;
			}

			/* Build Decoding Tables */
			{
				int nbSeq;
				size_t seqHSize = DecodeSeqHeaders(dctx, &nbSeq, ip, srcSize);
				if (IsError(seqHSize)) return seqHSize;
				ip += seqHSize;
				srcSize -= seqHSize;

				if ((frame == 0 || dctx.fParams.windowSize > (1 << 24))
				  && (nbSeq > 0))
				{  /* could probably use a larger nbSeq limit */
					U32 shareLongOffsets = GetLongOffsetsShare(dctx.OFTptr);
					U32 minShare = MEM_64bits() ? (U32)7 : 20; /* heuristic values, correspond to 2.73% and 7.81% */
					if (shareLongOffsets >= minShare)
						return DecompressSequencesLong(dctx, dst, dstCapacity, ip, srcSize, nbSeq, isLongOffset);
				}

				return DecompressSequences(dctx, dst, dstCapacity, ip, srcSize, nbSeq, isLongOffset);
			}
		}


		static void CheckContinuity(ZSTD_DCtx dctx, void* dst)
		{
			if (dst != dctx.previousDstEnd)
			{   /* not contiguous */
				dctx.dictEnd = dctx.previousDstEnd;
				dctx.vBase = (sbyte*)dst - ((sbyte*)(dctx.previousDstEnd) - (sbyte*)(dctx.baseField));
				dctx.baseField = dst;
				dctx.previousDstEnd = dst;
			}
		}

		//size_t DecompressBlock(ZSTD_DCtx dctx,
		//                            void* dst, size_t dstCapacity,
		//                      void* src, size_t srcSize)
		//{
		//    size_t dSize;
		//    CheckContinuity(dctx, dst);
		//    dSize = ZSTD_decompressBlock_internal(dctx, dst, dstCapacity, src, srcSize, /* frame */ 0);
		//    dctx->previousDstEnd = (sbyte*)dst + dSize;
		//    return dSize;
		//}


		///** InsertBlock() :
		//    insert `src` block into `dctx` history. Useful to track uncompressed blocks. */
		//ZSTDLIB_API size_t InsertBlock(ZSTD_DCtx dctx, void* blockStart, size_t blockSize)
		//{
		//    CheckContinuity(dctx, blockStart);
		//    dctx->previousDstEnd = ( sbyte*)blockStart + blockSize;
		//    return blockSize;
		//}


		static size_t GenerateNxBytes(void* dst, size_t dstCapacity, BYTE byteValue, size_t length)
		{
			if (length > dstCapacity) return ERROR(Error.dstSize_tooSmall);
			memset(dst, byteValue, length);
			return length;
		}

		///** FindFrameCompressedSize() :
		// *  compatible with legacy mode
		// *  `src` must point to the start of a ZSTD frame, ZSTD legacy frame, or skippable frame
		// *  `srcSize` must be at least as large as the frame contained
		// *  @return : the compressed size of the frame starting at `src` */
		//size_t FindFrameCompressedSize( void *src, size_t srcSize)
		//{
		//#if defined(ZSTD_LEGACY_SUPPORT) && (ZSTD_LEGACY_SUPPORT >= 1)
		//    if (IsLegacy(src, srcSize))
		//        return FindFrameCompressedSizeLegacy(src, srcSize);
		//#endif
		//    if ( (srcSize >= ZSTD_skippableHeaderSize)
		//      && (MEM_readLE32(src) & 0xFFFFFFF0U) == ZSTD_MAGIC_SKIPPABLE_START ) {
		//        return ZSTD_skippableHeaderSize + MEM_readLE32(( BYTE*)src +  ZSTD_frameIdSize);
		//    } else {
		//         BYTE* ip = ( BYTE*)src;
		//         BYTE*  ipstart = ip;
		//        size_t remainingSize = srcSize;
		//        FrameHeader zfh;

		//        /* Extract Frame Header */
		//        {   size_t  ret = GetFrameHeader(&zfh, src, srcSize);
		//            if ( IsError(ret)) return ret;
		//            if (ret > 0) return  ERROR( Error.srcSize_wrong);
		//        }

		//        ip += zfh.headerSize;
		//        remainingSize -= zfh.headerSize;

		//        /* Loop on each block */
		//        while (1) {
		//             blockProperties_t blockProperties;
		//            size_t  cBlockSize = GetcBlockSize(ip, remainingSize, &blockProperties);
		//            if ( IsError(cBlockSize)) return cBlockSize;

		//            if ( ZSTD_blockHeaderSize + cBlockSize > remainingSize)
		//                return  ERROR( Error.srcSize_wrong);

		//            ip +=  ZSTD_blockHeaderSize + cBlockSize;
		//            remainingSize -=  ZSTD_blockHeaderSize + cBlockSize;

		//            if (blockProperties.lastBlock) break;
		//        }

		//        if (zfh.checksumFlag) {   /* Final frame content checksum */
		//            if (remainingSize < 4) return  ERROR( Error.srcSize_wrong);
		//            ip += 4;
		//            remainingSize -= 4;
		//        }

		//        return ip - ipstart;
		//    }
		//}

		/*! DecompressFrame() :
		*   @dctx must be properly initialized */
		static size_t DecompressFrame(ZSTD_DCtx dctx,
										   void* dst, size_t dstCapacity,
									 void** srcPtr, size_t* srcSizePtr)
		{
			BYTE* ip = (BYTE*)(*srcPtr);
			BYTE* ostart = (BYTE*)dst;
			BYTE* oend = ostart + dstCapacity;
			BYTE* op = ostart;
			size_t remainingSize = *srcSizePtr;

			/* check */
			if (remainingSize < ZSTD_frameHeaderSize_min + ZSTD_blockHeaderSize)
				return ERROR(Error.srcSize_wrong);

			/* Frame Header */
			{
				size_t frameHeaderSize = FrameHeaderSize(ip, ZSTD_frameHeaderSize_prefix);
				if (IsError(frameHeaderSize)) return frameHeaderSize;
				if (remainingSize < frameHeaderSize + ZSTD_blockHeaderSize)
					return ERROR(Error.srcSize_wrong);
				{ size_t errcod = DecodeFrameHeader(dctx, ip, frameHeaderSize); if (IsError(errcod)) return errcod; }
				ip += frameHeaderSize; remainingSize -= frameHeaderSize;
			}

			/* Loop on each block */
			while (true)
			{
				size_t decodedSize;
				blockProperties_t blockProperties;
				size_t cBlockSize = GetcBlockSize(ip, remainingSize, &blockProperties);
				if (IsError(cBlockSize)) return cBlockSize;

				ip += ZSTD_blockHeaderSize;
				remainingSize -= ZSTD_blockHeaderSize;
				if (cBlockSize > remainingSize) return ERROR(Error.srcSize_wrong);

				switch (blockProperties.blockType)
				{
					case blockType_e.bt_compressed:
						decodedSize = ZSTD_decompressBlock_internal(dctx, op, (size_t)(oend - op), ip, cBlockSize, /* frame */ 1);
						break;
					case blockType_e.bt_raw:
						decodedSize = CopyRawBlock(op, (size_t)(oend - op), ip, cBlockSize);
						break;
					case blockType_e.bt_rle:
						decodedSize = GenerateNxBytes(op, (size_t)(oend - op), *ip, blockProperties.origSize);
						break;
					case blockType_e.bt_reserved:
					default:
						return ERROR(Error.corruption_detected);
				}

				if (IsError(decodedSize)) return decodedSize;
				if (dctx.fParams.checksumFlag != 0)
					XXH64_update(dctx.xxhState, op, decodedSize);
				op += decodedSize;
				ip += cBlockSize;
				remainingSize -= cBlockSize;
				if (blockProperties.lastBlock != 0) break;
			}

			if (dctx.fParams.frameContentSize != ZSTD_CONTENTSIZE_UNKNOWN)
			{
				if ((U64)(op - ostart) != dctx.fParams.frameContentSize)
				{
					return ERROR(Error.corruption_detected);
				}
			}
			if (dctx.fParams.checksumFlag != 0)
			{ /* Frame content checksum verification */
				U32 checkCalc = (U32)XXH64_digest(dctx.xxhState);
				U32 checkRead;
				if (remainingSize < 4) return ERROR(Error.checksum_wrong);
				checkRead = MEM_readLE32(ip);
				if (checkRead != checkCalc) return ERROR(Error.checksum_wrong);
				ip += 4;
				remainingSize -= 4;
			}

			/* Allow caller to get size read */
			*srcPtr = ip;
			*srcSizePtr = remainingSize;
			return (size_t)(op - ostart);
		}

		//static void* ZSTD_DDictDictContent( ZSTD_DDict* ddict);
		//static size_t ZSTD_DDictDictSize( ZSTD_DDict* ddict);

		static size_t DecompressMultiFrame(ZSTD_DCtx dctx,
												void* dst, size_t dstCapacity,
										  void* src, size_t srcSize,
										  void* dict, size_t dictSize
										  /*,ZSTD_DDict ddict is constant null*/)
		{
			void* dststart = dst;
			Debug.Assert(dict == null /*|| ddict == null*/);  /* either dict or ddict set, not both */

			//if (ddict != 0)
			//{
			//	dict = ZSTD_DDictDictContent(ddict);
			//	dictSize = ZSTD_DDictDictSize(ddict);
			//}

			while (srcSize >= ZSTD_frameHeaderSize_prefix)
			{
				U32 magicNumber;

				magicNumber = MEM_readLE32(src);
				if (magicNumber != ZSTD_MAGICNUMBER)
				{
					if ((magicNumber & 0xFFFFFFF0U) == ZSTD_MAGIC_SKIPPABLE_START)
					{
						size_t skippableSize;
						if (srcSize < ZSTD_skippableHeaderSize)
							return ERROR(Error.srcSize_wrong);
						skippableSize = MEM_readLE32((BYTE*)src + ZSTD_frameIdSize) + ZSTD_skippableHeaderSize;
						if (srcSize < skippableSize)
							return ERROR(Error.srcSize_wrong);

						src = (BYTE*)src + skippableSize;
						srcSize -= skippableSize;
						continue;
					}
					return ERROR(Error.prefix_unknown);
				}

				//if (ddict)
				//{
				//	/* we were called from ZSTD_decompress_usingDDict */
				//	{ size_t errcod = ZSTD_decompressBegin_usingDDict(dctx, ddict); if ( IsError(errcod)) return errcod; }
				//}
				//else
				{
					/* this will initialize correctly with no dict if dict == null, so
					 * use this in all cases but ddict */
					{ size_t errcod = ZSTD_decompressBegin_usingDict(dctx, dict, dictSize); if (IsError(errcod)) return errcod; }
				}
				CheckContinuity(dctx, dst);

				{
					size_t res = DecompressFrame(dctx, dst, dstCapacity, &src, &srcSize);
					if (IsError(res)) return res;
					/* no need to bound check, ZSTD_decompressFrame already has */
					dst = (BYTE*)dst + res;
					dstCapacity -= res;
				}
			}  /* while (srcSize >= ZSTD_frameHeaderSize_prefix) */

			if (srcSize != 0)
				return ERROR(Error.srcSize_wrong); /* input not entirely consumed */

			return (size_t)((BYTE*)dst - (BYTE*)dststart);
		}

		static size_t ZSTD_decompress_usingDict(ZSTD_DCtx dctx, void* dst, size_t dstCapacity,
								   void* src, size_t srcSize,
								   void* dict, size_t dictSize)
		{
			return DecompressMultiFrame(dctx, dst, dstCapacity, src, srcSize, dict, dictSize/*, null*/);
		}

		static size_t DecompressDCtx(ZSTD_DCtx dctx, void* dst, size_t dstCapacity, void* src, size_t srcSize)
		{
			return ZSTD_decompress_usingDict(dctx, dst, dstCapacity, src, srcSize, null, 0);
		}

		static size_t Decompress(void* dst, size_t dstCapacity, void* src, size_t srcSize)
		{
			using (ZSTD_DCtx dctx = CreateDCtx())
			{
				return DecompressDCtx(dctx, dst, dstCapacity, src, srcSize);
			}
		}

		public static size_t Decompress(byte[] dst, size_t dstCapacity, byte[] src, size_t srcSize)
		{
			fixed (byte* dstPtr = dst, srcPtr = src)
				return Decompress(dstPtr, dstCapacity, srcPtr, srcSize);
		}

		public static size_t Decompress(byte[] dst, byte[] src)
		{
			return Decompress(dst, (size_t)dst.Length, src, (size_t)src.Length);
		}



		///*-**************************************
		//*   Advanced Streaming Decompression API
		//*   Bufferless and synchronous
		//****************************************/
		//size_t NextSrcSizeToDecompress(ZSTD_DCtx dctx) { return dctx->expected; }

		//ZSTD_nextInputType_e NextInputType(ZSTD_DCtx dctx) {
		//    switch(dctx->stage)
		//    {
		//    default:   /* should not happen */
		//        Debug.Assert(0);
		//    case ZSTDds_getFrameHeaderSize:
		//    case ZSTDds_decodeFrameHeader:
		//        return ZSTDnit_frameHeader;
		//    case ZSTDds_decodeBlockHeader:
		//        return ZSTDnit_blockHeader;
		//    case ZSTDds_decompressBlock:
		//        return ZSTDnit_block;
		//    case ZSTDds_decompressLastBlock:
		//        return ZSTDnit_lastBlock;
		//    case ZSTDds_checkChecksum:
		//        return ZSTDnit_checksum;
		//    case ZSTDds_decodeSkippableHeader:
		//    case ZSTDds_skipFrame:
		//        return ZSTDnit_skippableFrame;
		//    }
		//}

		//static int IsSkipFrame(ZSTD_DCtx dctx) { return dctx->stage == ZSTDds_skipFrame; }

		///** DecompressContinue() :
		// *  srcSize : must be the exact nb of bytes expected (see NextSrcSizeToDecompress())
		// *  @return : nb of bytes generated into `dst` (necessarily <= `dstCapacity)
		// *            or an error code, which can be tested using  IsError() */
		//size_t DecompressContinue(ZSTD_DCtx dctx, void* dst, size_t dstCapacity, void* src, size_t srcSize)
		//{
		//    DEBUGLOG(5, "DecompressContinue(srcSize:%u)", (U32)srcSize);
		//    /* Sanity check */
		//    if (srcSize != dctx->expected) return  ERROR( Error.srcSize_wrong);  /* not allowed */
		//    if (dstCapacity) CheckContinuity(dctx, dst);

		//    switch (dctx->stage)
		//    {
		//    case ZSTDds_getFrameHeaderSize :
		//        Debug.Assert(src != null);
		//        if (dctx->format == ZSTD_format_e.ZSTD_f_zstd1) {  /* allows header */
		//            Debug.Assert(srcSize >=  ZSTD_frameIdSize);  /* to read skippable magic number */
		//            if ((MEM_readLE32(src) & 0xFFFFFFF0U) == ZSTD_MAGIC_SKIPPABLE_START) {        /* skippable frame */
		//                memcpy(dctx->headerBuffer, src, srcSize);
		//                dctx->expected = ZSTD_skippableHeaderSize - srcSize;  /* remaining to load to get full skippable frame header */
		//                dctx->stage = ZSTDds_decodeSkippableHeader;
		//                return 0;
		//        }   }
		//        dctx->headerSize = ZSTD_frameHeaderSize_internal(src, srcSize, dctx->format);
		//        if ( IsError(dctx->headerSize)) return dctx->headerSize;
		//        memcpy(dctx->headerBuffer, src, srcSize);
		//        dctx->expected = dctx->headerSize - srcSize;
		//        dctx->stage = ZSTDds_decodeFrameHeader;
		//        return 0;

		//    case ZSTDds_decodeFrameHeader:
		//        Debug.Assert(src != null);
		//        memcpy(dctx->headerBuffer + (dctx->headerSize - srcSize), src, srcSize);
		//        { size_t errcod = DecodeFrameHeader(dctx, dctx->headerBuffer, dctx->headerSize); if ( IsError(errcod)) return errcod; }
		//        dctx->expected =  ZSTD_blockHeaderSize;
		//        dctx->stage = ZSTDds_decodeBlockHeader;
		//        return 0;

		//    case ZSTDds_decodeBlockHeader:
		//        {    blockProperties_t bp;
		//            size_t  cBlockSize = GetcBlockSize(src,  ZSTD_blockHeaderSize, &bp);
		//            if ( IsError(cBlockSize)) return cBlockSize;
		//            dctx->expected = cBlockSize;
		//            dctx->bType = bp.blockType;
		//            dctx->rleSize = bp.origSize;
		//            if (cBlockSize) {
		//                dctx->stage = bp.lastBlock ? ZSTDds_decompressLastBlock : ZSTDds_decompressBlock;
		//                return 0;
		//            }
		//            /* empty block */
		//            if (bp.lastBlock) {
		//                if (dctx->fParams.checksumFlag) {
		//                    dctx->expected = 4;
		//                    dctx->stage = ZSTDds_checkChecksum;
		//                } else {
		//                    dctx->expected = 0; /* end of frame */
		//                    dctx->stage = ZSTDds_getFrameHeaderSize;
		//                }
		//            } else {
		//                dctx->expected =  ZSTD_blockHeaderSize;  /* jump to next header */
		//                dctx->stage = ZSTDds_decodeBlockHeader;
		//            }
		//            return 0;
		//        }

		//    case ZSTDds_decompressLastBlock:
		//    case ZSTDds_decompressBlock:
		//        DEBUGLOG(5, "ZSTD_decompressContinue: case ZSTDds_decompressBlock");
		//        {   size_t rSize;
		//            switch(dctx->bType)
		//            {
		//            case bt_compressed:
		//                DEBUGLOG(5, "ZSTD_decompressContinue: case bt_compressed");
		//                rSize = ZSTD_decompressBlock_internal(dctx, dst, dstCapacity, src, srcSize, /* frame */ 1);
		//                break;
		//            case bt_raw :
		//                rSize = CopyRawBlock(dst, dstCapacity, src, srcSize);
		//                break;
		//            case bt_rle :
		//                rSize = SetRleBlock(dst, dstCapacity, src, srcSize, dctx->rleSize);
		//                break;
		//            case bt_reserved :   /* should never happen */
		//            default:
		//                return  ERROR( Error.corruption_detected);
		//            }
		//            if ( IsError(rSize)) return rSize;
		//            DEBUGLOG(5, "ZSTD_decompressContinue: decoded size from block : %u", (U32)rSize);
		//            dctx->decodedSize += rSize;
		//            if (dctx->fParams.checksumFlag) XXH64_update(&dctx->xxhState, dst, rSize);

		//            if (dctx->stage == ZSTDds_decompressLastBlock) {   /* end of frame */
		//                DEBUGLOG(4, "ZSTD_decompressContinue: decoded size from frame : %u", (U32)dctx->decodedSize);
		//                if (dctx->fParams.frameContentSize != ZSTD_CONTENTSIZE_UNKNOWN) {
		//                    if (dctx->decodedSize != dctx->fParams.frameContentSize) {
		//                        return  ERROR( Error.corruption_detected);
		//                }   }
		//                if (dctx->fParams.checksumFlag) {  /* another round for frame checksum */
		//                    dctx->expected = 4;
		//                    dctx->stage = ZSTDds_checkChecksum;
		//                } else {
		//                    dctx->expected = 0;   /* ends here */
		//                    dctx->stage = ZSTDds_getFrameHeaderSize;
		//                }
		//            } else {
		//                dctx->stage = ZSTDds_decodeBlockHeader;
		//                dctx->expected =  ZSTD_blockHeaderSize;
		//                dctx->previousDstEnd = (sbyte*)dst + rSize;
		//            }
		//            return rSize;
		//        }

		//    case ZSTDds_checkChecksum:
		//        Debug.Assert(srcSize == 4);  /* guaranteed by dctx->expected */
		//        {   U32  h32 = (U32)XXH64_digest(&dctx->xxhState);
		//            U32  check32 = MEM_readLE32(src);
		//            DEBUGLOG(4, "ZSTD_decompressContinue: checksum : calculated %08X :: %08X read", h32, check32);
		//            if (check32 != h32) return  ERROR( Error.checksum_wrong);
		//            dctx->expected = 0;
		//            dctx->stage = ZSTDds_getFrameHeaderSize;
		//            return 0;
		//        }

		//    case ZSTDds_decodeSkippableHeader:
		//        Debug.Assert(src != null);
		//        Debug.Assert(srcSize <= ZSTD_skippableHeaderSize);
		//        memcpy(dctx->headerBuffer + (ZSTD_skippableHeaderSize - srcSize), src, srcSize);   /* complete skippable header */
		//        dctx->expected = MEM_readLE32(dctx->headerBuffer +  ZSTD_frameIdSize);   /* note : dctx->expected can grow seriously large, beyond local buffer size */
		//        dctx->stage = ZSTDds_skipFrame;
		//        return 0;

		//    case ZSTDds_skipFrame:
		//        dctx->expected = 0;
		//        dctx->stage = ZSTDds_getFrameHeaderSize;
		//        return 0;

		//    default:
		//        return  ERROR( Error.GENERIC);   /* impossible */
		//    }
		//}


		static size_t RefDictContent(ZSTD_DCtx dctx, void* dict, size_t dictSize)
		{
			dctx.dictEnd = dctx.previousDstEnd;
			dctx.vBase = (sbyte*)dict - ((sbyte*)(dctx.previousDstEnd) - (sbyte*)(dctx.baseField));
			dctx.baseField = dict;
			dctx.previousDstEnd = (sbyte*)dict + dictSize;
			return 0;
		}

		/* LoadEntropy() :
		 * dict : must point at beginning of a valid zstd dictionary
		 * @return : size of entropy tables read */
		static size_t LoadEntropy(ZSTD_entropyDTables_t entropy, void* dict, size_t dictSize)
		{
			BYTE* dictPtr = (BYTE*)dict;
			BYTE* dictEnd = dictPtr + dictSize;

			if (dictSize <= 8) return ERROR(Error.dictionary_corrupted);
			dictPtr += 8;   /* skip header = magic + dictID */


			{
				size_t hSize = HUF_readDTableX4_wksp(
					entropy.hufTable, dictPtr, (size_t)(dictEnd - dictPtr),
					entropy.workspace, sizeof(U32) * HUF_DECOMPRESS_WORKSPACE_SIZE_U32);
				if (IsError(hSize)) return ERROR(Error.dictionary_corrupted);
				dictPtr += hSize;
			}

			{
				short[] offcodeNCount = new short[MaxOff + 1];
				U32 offcodeMaxValue = MaxOff, offcodeLog;
				size_t offcodeHeaderSize = ReadNCount(offcodeNCount, &offcodeMaxValue, &offcodeLog, dictPtr, (size_t)(dictEnd - dictPtr));
				if (IsError(offcodeHeaderSize)) return ERROR(Error.dictionary_corrupted);
				if (offcodeMaxValue > MaxOff) return ERROR(Error.dictionary_corrupted);
				if (offcodeLog > OffFSELog) return ERROR(Error.dictionary_corrupted);
				BuildFSETable(entropy.OFTable,
									offcodeNCount, offcodeMaxValue,
									OF_base, OF_bits,
									offcodeLog);
				dictPtr += offcodeHeaderSize;
			}

			{
				short[] matchlengthNCount = new short[MaxML + 1];
				uint matchlengthMaxValue = MaxML, matchlengthLog;
				size_t matchlengthHeaderSize = ReadNCount(matchlengthNCount, &matchlengthMaxValue, &matchlengthLog, dictPtr, (size_t)(dictEnd - dictPtr));
				if (IsError(matchlengthHeaderSize)) return ERROR(Error.dictionary_corrupted);
				if (matchlengthMaxValue > MaxML) return ERROR(Error.dictionary_corrupted);
				if (matchlengthLog > MLFSELog) return ERROR(Error.dictionary_corrupted);
				BuildFSETable(entropy.MLTable,
									matchlengthNCount, matchlengthMaxValue,
									ML_base, ML_bits,
									matchlengthLog);
				dictPtr += matchlengthHeaderSize;
			}

			{
				short[] litlengthNCount = new short[MaxLL + 1];
				uint litlengthMaxValue = MaxLL, litlengthLog;
				size_t litlengthHeaderSize = ReadNCount(litlengthNCount, &litlengthMaxValue, &litlengthLog, dictPtr, (size_t)(dictEnd - dictPtr));
				if (IsError(litlengthHeaderSize)) return ERROR(Error.dictionary_corrupted);
				if (litlengthMaxValue > MaxLL) return ERROR(Error.dictionary_corrupted);
				if (litlengthLog > LLFSELog) return ERROR(Error.dictionary_corrupted);
				BuildFSETable(entropy.LLTable,
									litlengthNCount, litlengthMaxValue,
									LL_base, LL_bits,
									litlengthLog);
				dictPtr += litlengthHeaderSize;
			}

			if (dictPtr + 12 > dictEnd) return ERROR(Error.dictionary_corrupted);
			{
				int i;
				size_t dictContentSize = (size_t)(dictEnd - (dictPtr + 12));
				for (i = 0; i < 3; i++)
				{
					U32 rep = MEM_readLE32(dictPtr); dictPtr += 4;
					if (rep == 0 || rep >= dictContentSize) return ERROR(Error.dictionary_corrupted);
					entropy.rep[i] = rep;
				}
			}

			return (size_t)(dictPtr - (BYTE*)dict);
		}

		static size_t ZSTD_decompress_insertDictionary(ZSTD_DCtx dctx, void* dict, size_t dictSize)
		{
			if (dictSize < 8) return RefDictContent(dctx, dict, dictSize);
			{
				U32 magic = MEM_readLE32(dict);
				if (magic != ZSTD_MAGIC_DICTIONARY)
				{
					return RefDictContent(dctx, dict, dictSize);   /* pure content mode */
				}
			}
			dctx.dictID = MEM_readLE32((sbyte*)dict + ZSTD_frameIdSize);

			/* load entropy tables */
			{
				size_t eSize = LoadEntropy(dctx.entropy, dict, dictSize);
				if (IsError(eSize)) return ERROR(Error.dictionary_corrupted);
				dict = (sbyte*)dict + eSize;
				dictSize -= eSize;
			}
			dctx.litEntropy = dctx.fseEntropy = 1;

			/* reference dictionary content */
			return RefDictContent(dctx, dict, dictSize);
		}

		/* Note : this function cannot fail */
		static size_t DecompressBegin(ZSTD_DCtx dctx)
		{
			Debug.Assert(dctx != null);
			dctx.expected = StartingInputLength(dctx.format);  /* dctx->format must be properly set */
			dctx.stage = ZSTD_dStage.ZSTDds_getFrameHeaderSize;
			dctx.decodedSize = 0;
			dctx.previousDstEnd = null;
			dctx.baseField = null;
			dctx.vBase = null;
			dctx.dictEnd = null;
			dctx.entropy.hufTable[0] = (HUF_DTable)((HufLog) * 0x1000001);  /* cover both little and big endian */
			dctx.litEntropy = dctx.fseEntropy = 0;
			dctx.dictID = 0;
			//ZSTD_STATIC_ASSERT(sizeof(dctx.entropy.rep) == sizeof(repStartValue));
			fixed (size_t* repStartValuePtr = repStartValue)
				memcpy(dctx.entropy.rep, repStartValuePtr, (size_t)(sizeof(size_t) * repStartValue.Length));  /* initial repcodes */
			dctx.LLTptr = dctx.entropy.LLTable;
			dctx.MLTptr = dctx.entropy.MLTable;
			dctx.OFTptr = dctx.entropy.OFTable;
			dctx.HUFptr = dctx.entropy.hufTable;
			return 0;
		}

		static size_t ZSTD_decompressBegin_usingDict(ZSTD_DCtx dctx, void* dict, size_t dictSize)
		{
			{ size_t errcod = DecompressBegin(dctx); if (IsError(errcod)) return errcod; }
			if (dict != null && dictSize != 0)
			{ size_t errcod = ZSTD_decompress_insertDictionary(dctx, dict, dictSize); if (IsError(errcod)) return ERROR(Error.dictionary_corrupted); }
			return 0;
		}


		///* ======   ZSTD_DDict   ====== */

		//struct ZSTD_DDict_s {
		//    void* dictBuffer;
		//    void* dictContent;
		//    size_t dictSize;
		//    ZSTD_entropyDTables_t entropy;
		//    U32 dictID;
		//    U32 entropyPresent;
		//    ZSTD_customMem cMem;
		//};  /* typedef'd to ZSTD_DDict within "zstd.h" */

		//static void* ZSTD_DDictDictContent(ZSTD_DDict ddict)
		//{
		//	return ddict.dictContent;
		//}

		//static size_t ZSTD_DDictDictSize(ZSTD_DDict ddict)
		//{
		//	return ddict.dictSize;
		//}

		//size_t ZSTD_decompressBegin_usingDDict(ZSTD_DCtx dstDCtx,  ZSTD_DDict* ddict)
		//{
		//    { size_t errcod =  DecompressBegin(dstDCtx) ; if ( IsError(errcod)) return errcod; }
		//    if (ddict) {   /* support begin on null */
		//        dstDCtx->dictID = ddict->dictID;
		//        dstDCtx->base = ddict->dictContent;
		//        dstDCtx->vBase = ddict->dictContent;
		//        dstDCtx->dictEnd = ( BYTE*)ddict->dictContent + ddict->dictSize;
		//        dstDCtx->previousDstEnd = dstDCtx->dictEnd;
		//        if (ddict->entropyPresent) {
		//            dstDCtx->litEntropy = 1;
		//            dstDCtx->fseEntropy = 1;
		//            dstDCtx->LLTptr = ddict->entropy.LLTable;
		//            dstDCtx->MLTptr = ddict->entropy.MLTable;
		//            dstDCtx->OFTptr = ddict->entropy.OFTable;
		//            dstDCtx->HUFptr = ddict->entropy.hufTable;
		//            dstDCtx->entropy.rep[0] = ddict->entropy.rep[0];
		//            dstDCtx->entropy.rep[1] = ddict->entropy.rep[1];
		//            dstDCtx->entropy.rep[2] = ddict->entropy.rep[2];
		//        } else {
		//            dstDCtx->litEntropy = 0;
		//            dstDCtx->fseEntropy = 0;
		//        }
		//    }
		//    return 0;
		//}

		//static size_t ZSTD_loadEntropy_inDDict(ZSTD_DDict* ddict, ZSTD_dictContentType_e dictContentType)
		//{
		//    ddict->dictID = 0;
		//    ddict->entropyPresent = 0;
		//    if (dictContentType == ZSTD_dct_rawContent) return 0;

		//    if (ddict->dictSize < 8) {
		//        if (dictContentType == ZSTD_dct_fullDict)
		//            return  ERROR( Error.dictionary_corrupted);   /* only accept specified dictionaries */
		//        return 0;   /* pure content mode */
		//    }
		//    {   U32  magic = MEM_readLE32(ddict->dictContent);
		//        if (magic != ZSTD_MAGIC_DICTIONARY) {
		//            if (dictContentType == ZSTD_dct_fullDict)
		//                return  ERROR( Error.dictionary_corrupted);   /* only accept specified dictionaries */
		//            return 0;   /* pure content mode */
		//        }
		//    }
		//    ddict->dictID = MEM_readLE32(( sbyte*)ddict->dictContent +  ZSTD_frameIdSize);

		//    /* load entropy tables */
		//    { size_t errcod =  LoadEntropy(&ddict->entropy, ddict->dictContent, ddict->dictSize); if ( IsError(errcod)) return  ERROR( Error.dictionary_corrupted); }
		//    ddict->entropyPresent = 1;
		//    return 0;
		//}


		//static size_t ZSTD_initDDict_internal(ZSTD_DDict* ddict,
		//                                      void* dict, size_t dictSize,
		//                                      ZSTD_dictLoadMethod_e dictLoadMethod,
		//                                      ZSTD_dictContentType_e dictContentType)
		//{
		//    if ((dictLoadMethod == ZSTD_dlm_byRef) || (!dict) || (!dictSize)) {
		//        ddict->dictBuffer = null;
		//        ddict->dictContent = dict;
		//    } else {
		//        void*  internalBuffer = Malloc(dictSize, ddict->cMem);
		//        ddict->dictBuffer = internalBuffer;
		//        ddict->dictContent = internalBuffer;
		//        if (!internalBuffer) return  ERROR( Error.memory_allocation);
		//        memcpy(internalBuffer, dict, dictSize);
		//    }
		//    ddict->dictSize = dictSize;
		//    ddict->entropy.hufTable[0] = (HUF_DTable)(( HufLog)*0x1000001);  /* cover both little and big endian */

		//    /* parse dictionary content */
		//    { size_t errcod =  ZSTD_loadEntropy_inDDict(ddict, dictContentType) ; if ( IsError(errcod)) return errcod; }

		//    return 0;
		//}

		//ZSTD_DDict* ZSTD_createDDict_advanced(void* dict, size_t dictSize,
		//                                      ZSTD_dictLoadMethod_e dictLoadMethod,
		//                                      ZSTD_dictContentType_e dictContentType,
		//                                      ZSTD_customMem customMem)
		//{
		//    if (!customMem.customAlloc ^ !customMem.customFree) return null;

		//    {   ZSTD_DDict*  ddict = (ZSTD_DDict*) Malloc(sizeof(ZSTD_DDict), customMem);
		//        if (!ddict) return null;
		//        ddict->cMem = customMem;

		//        if ( IsError( ZSTD_initDDict_internal(ddict, dict, dictSize, dictLoadMethod, dictContentType) )) {
		//            FreeDDict(ddict);
		//            return null;
		//        }

		//        return ddict;
		//    }
		//}

		///*! CreateDDict() :
		//*   Create a digested dictionary, to start decompression without startup delay.
		//*   `dict` content is copied inside DDict.
		//*   Consequently, `dict` can be released after `ZSTD_DDict` creation */
		//ZSTD_DDict* CreateDDict(void* dict, size_t dictSize)
		//{
		//    ZSTD_customMem  allocator = { null, null, null };
		//    return ZSTD_createDDict_advanced(dict, dictSize, ZSTD_dlm_byCopy, ZSTD_dct_auto, allocator);
		//}

		///*! ZSTD_createDDict_byReference() :
		// *  Create a digested dictionary, to start decompression without startup delay.
		// *  Dictionary content is simply referenced, it will be accessed during decompression.
		// *  Warning : dictBuffer must outlive DDict (DDict must be freed before dictBuffer) */
		//ZSTD_DDict* ZSTD_createDDict_byReference(void* dictBuffer, size_t dictSize)
		//{
		//    ZSTD_customMem  allocator = { null, null, null };
		//    return ZSTD_createDDict_advanced(dictBuffer, dictSize, ZSTD_dlm_byRef, ZSTD_dct_auto, allocator);
		//}


		// ZSTD_DDict* InitStaticDDict(
		//                                void* workspace, size_t workspaceSize,
		//                                void* dict, size_t dictSize,
		//                                ZSTD_dictLoadMethod_e dictLoadMethod,
		//                                ZSTD_dictContentType_e dictContentType)
		//{
		//    size_t  neededSpace =
		//            sizeof(ZSTD_DDict) + (dictLoadMethod == ZSTD_dlm_byRef ? 0 : dictSize);
		//    ZSTD_DDict*  ddict = (ZSTD_DDict*)workspace;
		//    Debug.Assert(workspace != null);
		//    Debug.Assert(dict != null);
		//    if ((size_t)workspace & 7) return null;  /* 8-aligned */
		//    if (workspaceSize < neededSpace) return null;
		//    if (dictLoadMethod == ZSTD_dlm_byCopy) {
		//        memcpy(ddict+1, dict, dictSize);  /* local copy */
		//        dict = ddict+1;
		//    }
		//    if ( IsError( ZSTD_initDDict_internal(ddict, dict, dictSize, ZSTD_dlm_byRef, dictContentType) ))
		//        return null;
		//    return ddict;
		//}


		//size_t FreeDDict(ZSTD_DDict* ddict)
		//{
		//    if (ddict==null) return 0;   /* support free on null */
		//    {   ZSTD_customMem  cMem = ddict->cMem;
		//        Free(ddict->dictBuffer, cMem);
		//        Free(ddict, cMem);
		//        return 0;
		//    }
		//}

		///*! EstimateDDictSize() :
		// *  Estimate amount of memory that will be needed to create a dictionary for decompression.
		// *  Note : dictionary created by reference using ZSTD_dlm_byRef are smaller */
		//size_t EstimateDDictSize(size_t dictSize, ZSTD_dictLoadMethod_e dictLoadMethod)
		//{
		//    return sizeof(ZSTD_DDict) + (dictLoadMethod == ZSTD_dlm_byRef ? 0 : dictSize);
		//}

		//size_t ZSTD_sizeof_DDict( ZSTD_DDict* ddict)
		//{
		//    if (ddict==null) return 0;   /* support sizeof on null */
		//    return sizeof(*ddict) + (ddict->dictBuffer ? ddict->dictSize : 0) ;
		//}

		///*! ZSTD_getDictID_fromDict() :
		// *  Provides the dictID stored within dictionary.
		// *  if @return == 0, the dictionary is not conformant with Zstandard specification.
		// *  It can still be loaded, but as a content-only dictionary. */
		//unsigned ZSTD_getDictID_fromDict(void* dict, size_t dictSize)
		//{
		//    if (dictSize < 8) return 0;
		//    if (MEM_readLE32(dict) != ZSTD_MAGIC_DICTIONARY) return 0;
		//    return MEM_readLE32(( sbyte*)dict +  ZSTD_frameIdSize);
		//}

		///*! ZSTD_getDictID_fromDDict() :
		// *  Provides the dictID of the dictionary loaded into `ddict`.
		// *  If @return == 0, the dictionary is not conformant to Zstandard specification, or empty.
		// *  Non-conformant dictionaries can still be loaded, but as content-only dictionaries. */
		//unsigned ZSTD_getDictID_fromDDict( ZSTD_DDict* ddict)
		//{
		//    if (ddict==null) return 0;
		//    return ZSTD_getDictID_fromDict(ddict->dictContent, ddict->dictSize);
		//}

		///*! ZSTD_getDictID_fromFrame() :
		// *  Provides the dictID required to decompresse frame stored within `src`.
		// *  If @return == 0, the dictID could not be decoded.
		// *  This could for one of the following reasons :
		// *  - The frame does not require a dictionary (most common case).
		// *  - The frame was built with dictID intentionally removed.
		// *    Needed dictionary is a hidden information.
		// *    Note : this use case also happens when using a non-conformant dictionary.
		// *  - `srcSize` is too small, and as a result, frame header could not be decoded.
		// *    Note : possible if `srcSize < ZSTD_FRAMEHEADERSIZE_MAX`.
		// *  - This is not a Zstandard frame.
		// *  When identifying the exact failure cause, it's possible to use
		// *  GetFrameHeader(), which will provide a more precise error code. */
		//unsigned ZSTD_getDictID_fromFrame(void* src, size_t srcSize)
		//{
		//    FrameHeader zfp = { 0, 0, 0, ZSTD_frameType_e.ZSTD_frame, 0, 0, 0 };
		//    size_t  hError = GetFrameHeader(&zfp, src, srcSize);
		//    if ( IsError(hError)) return 0;
		//    return zfp.dictID;
		//}


		///*! ZSTD_decompress_usingDDict() :
		//*   Decompression using a pre-digested Dictionary
		//*   Use dictionary without significant overhead. */
		//size_t ZSTD_decompress_usingDDict(ZSTD_DCtx dctx,
		//                                  void* dst, size_t dstCapacity,
		//                            void* src, size_t srcSize,
		//                             ZSTD_DDict* ddict)
		//{
		//    /* pass content and size in case legacy frames are encountered */
		//    return DecompressMultiFrame(dctx, dst, dstCapacity, src, srcSize,
		//                                     null, 0,
		//                                     ddict);
		//}


		///*=====================================
		//*   Streaming decompression
		//*====================================*/

		//ZSTD_DStream* CreateDStream(void)
		//{
		//    DEBUGLOG(3, "ZSTD_createDStream");
		//    return ZSTD_createDStream_advanced(ZSTD_defaultCMem);
		//}

		//ZSTD_DStream* InitStaticDStream(void *workspace, size_t workspaceSize)
		//{
		//    return InitStaticDCtx(workspace, workspaceSize);
		//}

		//ZSTD_DStream* ZSTD_createDStream_advanced(ZSTD_customMem customMem)
		//{
		//    return ZSTD_createDCtx_advanced(customMem);
		//}

		//size_t FreeDStream(ZSTD_DStream* zds)
		//{
		//    return FreeDCtx(zds);
		//}


		///* *** Initialization *** */

		//size_t ZSTD_DStreamInSize(void)  { return ZSTD_BLOCKSIZE_MAX +  ZSTD_blockHeaderSize; }
		//size_t ZSTD_DStreamOutSize(void) { return ZSTD_BLOCKSIZE_MAX; }

		//size_t ZSTD_DCtx_loadDictionary_advanced(ZSTD_DCtx dctx, void* dict, size_t dictSize, ZSTD_dictLoadMethod_e dictLoadMethod, ZSTD_dictContentType_e dictContentType)
		//{
		//    if (dctx->streamStage != zdss_init) return  ERROR( Error.stage_wrong);
		//    FreeDDict(dctx->ddictLocal);
		//    if (dict && dictSize >= 8) {
		//        dctx->ddictLocal = ZSTD_createDDict_advanced(dict, dictSize, dictLoadMethod, dictContentType, dctx->customMem);
		//        if (dctx->ddictLocal == null) return  ERROR( Error.memory_allocation);
		//    } else {
		//        dctx->ddictLocal = null;
		//    }
		//    dctx->ddict = dctx->ddictLocal;
		//    return 0;
		//}

		//size_t ZSTD_DCtx_loadDictionary_byReference(ZSTD_DCtx dctx, void* dict, size_t dictSize)
		//{
		//    return ZSTD_DCtx_loadDictionary_advanced(dctx, dict, dictSize, ZSTD_dlm_byRef, ZSTD_dct_auto);
		//}

		//size_t ZSTD_DCtx_loadDictionary(ZSTD_DCtx dctx, void* dict, size_t dictSize)
		//{
		//    return ZSTD_DCtx_loadDictionary_advanced(dctx, dict, dictSize, ZSTD_dlm_byCopy, ZSTD_dct_auto);
		//}

		//size_t ZSTD_DCtx_refPrefix_advanced(ZSTD_DCtx dctx, void* prefix, size_t prefixSize, ZSTD_dictContentType_e dictContentType)
		//{
		//    return ZSTD_DCtx_loadDictionary_advanced(dctx, prefix, prefixSize, ZSTD_dlm_byRef, dictContentType);
		//}

		//size_t ZSTD_DCtx_refPrefix(ZSTD_DCtx dctx, void* prefix, size_t prefixSize)
		//{
		//    return ZSTD_DCtx_refPrefix_advanced(dctx, prefix, prefixSize, ZSTD_dct_rawContent);
		//}


		///* ZSTD_initDStream_usingDict() :
		// * return : expected size, aka ZSTD_frameHeaderSize_prefix.
		// * this function cannot fail */
		//size_t ZSTD_initDStream_usingDict(ZSTD_DStream* zds, void* dict, size_t dictSize)
		//{
		//    DEBUGLOG(4, "ZSTD_initDStream_usingDict");
		//    zds->streamStage = zdss_init;
		//    { size_t errcod =  ZSTD_DCtx_loadDictionary(zds, dict, dictSize) ; if ( IsError(errcod)) return errcod; }
		//    return ZSTD_frameHeaderSize_prefix;
		//}

		///* note : this variant can't fail */
		//size_t InitDStream(ZSTD_DStream* zds)
		//{
		//    DEBUGLOG(4, "ZSTD_initDStream");
		//    return ZSTD_initDStream_usingDict(zds, null, 0);
		//}

		//size_t ZSTD_DCtx_refDDict(ZSTD_DCtx dctx,  ZSTD_DDict* ddict)
		//{
		//    if (dctx->streamStage != zdss_init) return  ERROR( Error.stage_wrong);
		//    dctx->ddict = ddict;
		//    return 0;
		//}

		///* ZSTD_initDStream_usingDDict() :
		// * ddict will just be referenced, and must outlive decompression session
		// * this function cannot fail */
		//size_t ZSTD_initDStream_usingDDict(ZSTD_DStream* dctx,  ZSTD_DDict* ddict)
		//{
		//    size_t  initResult = InitDStream(dctx);
		//    dctx->ddict = ddict;
		//    return initResult;
		//}

		///* ResetDStream() :
		// * return : expected size, aka ZSTD_frameHeaderSize_prefix.
		// * this function cannot fail */
		//size_t ResetDStream(ZSTD_DStream* dctx)
		//{
		//    DEBUGLOG(4, "ZSTD_resetDStream");
		//    dctx->streamStage = zdss_loadHeader;
		//    dctx->lhSize = dctx->inPos = dctx->outStart = dctx->outEnd = 0;
		//    dctx->legacyVersion = 0;
		//    dctx->hostageByte = 0;
		//    return ZSTD_frameHeaderSize_prefix;
		//}

		//size_t SetDStreamParameter(ZSTD_DStream* dctx,
		//                                ZSTD_DStreamParameter_e paramType, unsigned paramValue)
		//{
		//    if (dctx->streamStage != zdss_init) return  ERROR( Error.stage_wrong);
		//    switch(paramType)
		//    {
		//        default : return  ERROR( Error.parameter_unsupported);
		//        case DStream_p_maxWindowSize :
		//            DEBUGLOG(4, "setting maxWindowSize = %u KB", paramValue >> 10);
		//            dctx->maxWindowSize = paramValue ? paramValue : (U32)(-1);
		//            break;
		//    }
		//    return 0;
		//}

		//size_t ZSTD_DCtx_setMaxWindowSize(ZSTD_DCtx dctx, size_t maxWindowSize)
		//{
		//    if (dctx->streamStage != zdss_init) return  ERROR( Error.stage_wrong);
		//    dctx->maxWindowSize = maxWindowSize;
		//    return 0;
		//}

		//size_t ZSTD_DCtx_setFormat(ZSTD_DCtx dctx, ZSTD_format_e format)
		//{
		//    DEBUGLOG(4, "ZSTD_DCtx_setFormat : %u", (unsigned)format);
		//    if (dctx->streamStage != zdss_init) return  ERROR( Error.stage_wrong);
		//    dctx->format = format;
		//    return 0;
		//}


		//size_t ZSTD_sizeof_DStream( ZSTD_DStream* dctx)
		//{
		//    return ZSTD_sizeof_DCtx(dctx);
		//}

		//size_t ZSTD_decodingBufferSize_min(unsigned long long windowSize, unsigned long long frameContentSize)
		//{
		//    size_t  blockSize = (size_t) MIN(windowSize, ZSTD_BLOCKSIZE_MAX);
		//    unsigned long long  neededRBSize = windowSize + blockSize + ( WILDCOPY_OVERLENGTH * 2);
		//    unsigned long long  neededSize = MIN(frameContentSize, neededRBSize);
		//    size_t  minRBSize = (size_t) neededSize;
		//    if ((unsigned long long)minRBSize != neededSize) return  ERROR( Error.frameParameter_windowTooLarge);
		//    return minRBSize;
		//}

		//size_t EstimateDStreamSize(size_t windowSize)
		//{
		//    size_t  blockSize = MIN(windowSize, ZSTD_BLOCKSIZE_MAX);
		//    size_t  inBuffSize = blockSize;  /* no block can be larger */
		//    size_t  outBuffSize = ZSTD_decodingBufferSize_min(windowSize, ZSTD_CONTENTSIZE_UNKNOWN);
		//    return EstimateDCtxSize() + inBuffSize + outBuffSize;
		//}

		//size_t ZSTD_estimateDStreamSize_fromFrame(void* src, size_t srcSize)
		//{
		//    U32  windowSizeMax = 1U << ZSTD_WINDOWLOG_MAX;   /* note : should be user-selectable */
		//    FrameHeader zfh;
		//    size_t  err = GetFrameHeader(&zfh, src, srcSize);
		//    if ( IsError(err)) return err;
		//    if (err>0) return  ERROR( Error.srcSize_wrong);
		//    if (zfh.windowSize > windowSizeMax)
		//        return  ERROR( Error.frameParameter_windowTooLarge);
		//    return EstimateDStreamSize((size_t)zfh.windowSize);
		//}


		///* *****   Decompression   ***** */

		//MEM_STATIC size_t LimitCopy(void* dst, size_t dstCapacity, void* src, size_t srcSize)
		//{
		//    size_t  length = MIN(dstCapacity, srcSize);
		//    memcpy(dst, src, length);
		//    return length;
		//}


		//size_t DecompressStream(ZSTD_DStream* zds, ZSTD_outBuffer* output, ZSTD_inBuffer* input)
		//{
		//     sbyte*  istart = ( sbyte*)(input->src) + input->pos;
		//     sbyte*  iend = ( sbyte*)(input->src) + input->size;
		//     sbyte* ip = istart;
		//    sbyte*  ostart = (sbyte*)(output->dst) + output->pos;
		//    sbyte*  oend = (sbyte*)(output->dst) + output->size;
		//    sbyte* op = ostart;
		//    U32 someMoreWork = 1;

		//    DEBUGLOG(5, "ZSTD_decompressStream");
		//    if (input->pos > input->size) {  /* forbidden */
		//        DEBUGLOG(5, "in: pos: %u   vs size: %u",
		//                    (U32)input->pos, (U32)input->size);
		//        return  ERROR( Error.srcSize_wrong);
		//    }
		//    if (output->pos > output->size) {  /* forbidden */
		//        DEBUGLOG(5, "out: pos: %u   vs size: %u",
		//                    (U32)output->pos, (U32)output->size);
		//        return  ERROR( Error.dstSize_tooSmall);
		//    }
		//    DEBUGLOG(5, "input size : %u", (U32)(input->size - input->pos));

		//    while (someMoreWork) {
		//        switch(zds->streamStage)
		//        {
		//        case zdss_init :
		//            DEBUGLOG(5, "stage zdss_init => transparent reset ");
		//            ResetDStream(zds);   /* transparent reset on starting decoding a new frame */
		//            /* fall-through */

		//        case zdss_loadHeader :
		//            DEBUGLOG(5, "stage zdss_loadHeader (srcSize : %u)", (U32)(iend - ip));
		//#if defined(ZSTD_LEGACY_SUPPORT) && (ZSTD_LEGACY_SUPPORT>=1)
		//            if (zds->legacyVersion) {
		//                /* legacy support is incompatible with static dctx */
		//                if (zds->staticSize) return  ERROR( Error.memory_allocation);
		//                {   size_t  hint = DecompressLegacyStream(zds->legacyContext, zds->legacyVersion, output, input);
		//                    if (hint==0) zds->streamStage = zdss_init;
		//                    return hint;
		//            }   }
		//#endif
		//            {   size_t  hSize = ZSTD_getFrameHeader_advanced(&zds->fParams, zds->headerBuffer, zds->lhSize, zds->format);
		//                DEBUGLOG(5, "header size : %u", (U32)hSize);
		//                if ( IsError(hSize)) {
		//#if defined(ZSTD_LEGACY_SUPPORT) && (ZSTD_LEGACY_SUPPORT>=1)
		//                    U32  legacyVersion = IsLegacy(istart, iend-istart);
		//                    if (legacyVersion) {
		//                        void*  dict = zds->ddict ? zds->ddict->dictContent : null;
		//                        size_t  dictSize = zds->ddict ? zds->ddict->dictSize : 0;
		//                        DEBUGLOG(5, "ZSTD_decompressStream: detected legacy version v0.%u", legacyVersion);
		//                        /* legacy support is incompatible with static dctx */
		//                        if (zds->staticSize) return  ERROR( Error.memory_allocation);
		//                        { size_t errcod = InitLegacyStream(&zds->legacyContext, zds->previousLegacyVersion, legacyVersion, dict, dictSize); if ( IsError(errcod)) return errcod; }
		//                        zds->legacyVersion = zds->previousLegacyVersion = legacyVersion;
		//                        {   size_t  hint = DecompressLegacyStream(zds->legacyContext, legacyVersion, output, input);
		//                            if (hint==0) zds->streamStage = zdss_init;   /* or stay in stage zdss_loadHeader */
		//                            return hint;
		//                    }   }
		//#endif
		//                    return hSize;   /* error */
		//                }
		//                if (hSize != 0) {   /* need more input */
		//                    size_t  toLoad = hSize - zds->lhSize;   /* if hSize!=0, hSize > zds->lhSize */
		//                    size_t  remainingInput = (size_t)(iend-ip);
		//                    Debug.Assert(iend >= ip);
		//                    if (toLoad > remainingInput) {   /* not enough input to load full header */
		//                        if (remainingInput > 0) {
		//                            memcpy(zds->headerBuffer + zds->lhSize, ip, remainingInput);
		//                            zds->lhSize += remainingInput;
		//                        }
		//                        input->pos = input->size;
		//                        return (MAX(ZSTD_frameHeaderSize_min, hSize) - zds->lhSize) +  ZSTD_blockHeaderSize;   /* remaining header bytes + next block header */
		//                    }
		//                    Debug.Assert(ip != null);
		//                    memcpy(zds->headerBuffer + zds->lhSize, ip, toLoad); zds->lhSize = hSize; ip += toLoad;
		//                    break;
		//            }   }

		//            /* check for single-pass mode opportunity */
		//            if (zds->fParams.frameContentSize && zds->fParams.windowSize /* skippable frame if == 0 */
		//                && (U64)(size_t)(oend-op) >= zds->fParams.frameContentSize) {
		//                size_t  cSize = FindFrameCompressedSize(istart, iend-istart);
		//                if (cSize <= (size_t)(iend-istart)) {
		//                    /* shortcut : using single-pass mode */
		//                    size_t  decompressedSize = ZSTD_decompress_usingDDict(zds, op, oend-op, istart, cSize, zds->ddict);
		//                    if ( IsError(decompressedSize)) return decompressedSize;
		//                    DEBUGLOG(4, "shortcut to single-pass ZSTD_decompress_usingDDict()")
		//                    ip = istart + cSize;
		//                    op += decompressedSize;
		//                    zds->expected = 0;
		//                    zds->streamStage = zdss_init;
		//                    someMoreWork = 0;
		//                    break;
		//            }   }

		//            /* Consume header (see ZSTDds_decodeFrameHeader) */
		//            DEBUGLOG(4, "Consume header");
		//            { size_t errcod = ZSTD_decompressBegin_usingDDict(zds, zds->ddict); if ( IsError(errcod)) return errcod; }

		//            if ((MEM_readLE32(zds->headerBuffer) & 0xFFFFFFF0U) == ZSTD_MAGIC_SKIPPABLE_START) {  /* skippable frame */
		//                zds->expected = MEM_readLE32(zds->headerBuffer +  ZSTD_frameIdSize);
		//                zds->stage = ZSTDds_skipFrame;
		//            } else {
		//                { size_t errcod = DecodeFrameHeader(zds, zds->headerBuffer, zds->lhSize); if ( IsError(errcod)) return errcod; }
		//                zds->expected =  ZSTD_blockHeaderSize;
		//                zds->stage = ZSTDds_decodeBlockHeader;
		//            }

		//            /* control buffer memory usage */
		//            DEBUGLOG(4, "Control max memory usage (%u KB <= max %u KB)",
		//                        (U32)(zds->fParams.windowSize >>10),
		//                        (U32)(zds->maxWindowSize >> 10) );
		//            zds->fParams.windowSize = MAX(zds->fParams.windowSize, 1U <<  ZSTD_WINDOWLOG_ABSOLUTEMIN);
		//            if (zds->fParams.windowSize > zds->maxWindowSize) return  ERROR( Error.frameParameter_windowTooLarge);

		//            /* Adapt buffer sizes to frame header instructions */
		//            {   size_t  neededInBuffSize = MAX(zds->fParams.blockSizeMax, 4 /* frame checksum */);
		//                size_t  neededOutBuffSize = ZSTD_decodingBufferSize_min(zds->fParams.windowSize, zds->fParams.frameContentSize);
		//                if ((zds->inBuffSize < neededInBuffSize) || (zds->outBuffSize < neededOutBuffSize)) {
		//                    size_t  bufferSize = neededInBuffSize + neededOutBuffSize;
		//                    DEBUGLOG(4, "inBuff  : from %u to %u",
		//                                (U32)zds->inBuffSize, (U32)neededInBuffSize);
		//                    DEBUGLOG(4, "outBuff : from %u to %u",
		//                                (U32)zds->outBuffSize, (U32)neededOutBuffSize);
		//                    if (zds->staticSize) {  /* static DCtx */
		//                        DEBUGLOG(4, "staticSize : %u", (U32)zds->staticSize);
		//                        Debug.Assert(zds->staticSize >= sizeof(ZSTD_DCtx));  /* controlled at init */
		//                        if (bufferSize > zds->staticSize - sizeof(ZSTD_DCtx))
		//                            return  ERROR( Error.memory_allocation);
		//                    } else {
		//                        Free(zds->inBuff, zds->customMem);
		//                        zds->inBuffSize = 0;
		//                        zds->outBuffSize = 0;
		//                        zds->inBuff = (sbyte*)Malloc(bufferSize, zds->customMem);
		//                        if (zds->inBuff == null) return  ERROR( Error.memory_allocation);
		//                    }
		//                    zds->inBuffSize = neededInBuffSize;
		//                    zds->outBuff = zds->inBuff + zds->inBuffSize;
		//                    zds->outBuffSize = neededOutBuffSize;
		//            }   }
		//            zds->streamStage = zdss_read;
		//            /* fall-through */

		//        case zdss_read:
		//            DEBUGLOG(5, "stage zdss_read");
		//            {   size_t  neededInSize = NextSrcSizeToDecompress(zds);
		//                DEBUGLOG(5, "neededInSize = %u", (U32)neededInSize);
		//                if (neededInSize==0) {  /* end of frame */
		//                    zds->streamStage = zdss_init;
		//                    someMoreWork = 0;
		//                    break;
		//                }
		//                if ((size_t)(iend-ip) >= neededInSize) {  /* decode directly from src */
		//                    int  isSkipFrame = IsSkipFrame(zds);
		//                    size_t  decodedSize = DecompressContinue(zds,
		//                        zds->outBuff + zds->outStart, (isSkipFrame ? 0 : zds->outBuffSize - zds->outStart),
		//                        ip, neededInSize);
		//                    if ( IsError(decodedSize)) return decodedSize;
		//                    ip += neededInSize;
		//                    if (!decodedSize && !isSkipFrame) break;   /* this was just a header */
		//                    zds->outEnd = zds->outStart + decodedSize;
		//                    zds->streamStage = zdss_flush;
		//                    break;
		//            }   }
		//            if (ip==iend) { someMoreWork = 0; break; }   /* no more input */
		//            zds->streamStage = zdss_load;
		//            /* fall-through */

		//        case zdss_load:
		//            {   size_t  neededInSize = NextSrcSizeToDecompress(zds);
		//                size_t  toLoad = neededInSize - zds->inPos;
		//                int  isSkipFrame = IsSkipFrame(zds);
		//                size_t loadedSize;
		//                if (isSkipFrame) {
		//                    loadedSize = MIN(toLoad, (size_t)(iend-ip));
		//                } else {
		//                    if (toLoad > zds->inBuffSize - zds->inPos) return  ERROR( Error.corruption_detected);   /* should never happen */
		//                    loadedSize = LimitCopy(zds->inBuff + zds->inPos, toLoad, ip, iend-ip);
		//                }
		//                ip += loadedSize;
		//                zds->inPos += loadedSize;
		//                if (loadedSize < toLoad) { someMoreWork = 0; break; }   /* not enough input, wait for more */

		//                /* decode loaded input */
		//                {   size_t  decodedSize = DecompressContinue(zds,
		//                        zds->outBuff + zds->outStart, zds->outBuffSize - zds->outStart,
		//                        zds->inBuff, neededInSize);
		//                    if ( IsError(decodedSize)) return decodedSize;
		//                    zds->inPos = 0;   /* input is consumed */
		//                    if (!decodedSize && !isSkipFrame) { zds->streamStage = zdss_read; break; }   /* this was just a header */
		//                    zds->outEnd = zds->outStart +  decodedSize;
		//            }   }
		//            zds->streamStage = zdss_flush;
		//            /* fall-through */

		//        case zdss_flush:
		//            {   size_t  toFlushSize = zds->outEnd - zds->outStart;
		//                size_t  flushedSize = LimitCopy(op, oend-op, zds->outBuff + zds->outStart, toFlushSize);
		//                op += flushedSize;
		//                zds->outStart += flushedSize;
		//                if (flushedSize == toFlushSize) {  /* flush completed */
		//                    zds->streamStage = zdss_read;
		//                    if ( (zds->outBuffSize < zds->fParams.frameContentSize)
		//                      && (zds->outStart + zds->fParams.blockSizeMax > zds->outBuffSize) ) {
		//                        DEBUGLOG(5, "restart filling outBuff from beginning (left:%i, needed:%u)",
		//                                (int)(zds->outBuffSize - zds->outStart),
		//                                (U32)zds->fParams.blockSizeMax);
		//                        zds->outStart = zds->outEnd = 0;
		//                    }
		//                    break;
		//            }   }
		//            /* cannot complete flush */
		//            someMoreWork = 0;
		//            break;

		//        default: return  ERROR( Error.GENERIC);   /* impossible */
		//    }   }

		//    /* result */
		//    input->pos += (size_t)(ip-istart);
		//    output->pos += (size_t)(op-ostart);
		//    {   size_t nextSrcSizeHint = NextSrcSizeToDecompress(zds);
		//        if (!nextSrcSizeHint) {   /* frame fully decoded */
		//            if (zds->outEnd == zds->outStart) {  /* output fully flushed */
		//                if (zds->hostageByte) {
		//                    if (input->pos >= input->size) {
		//                        /* can't release hostage (not present) */
		//                        zds->streamStage = zdss_read;
		//                        return 1;
		//                    }
		//                    input->pos++;  /* release hostage */
		//                }   /* zds->hostageByte */
		//                return 0;
		//            }  /* zds->outEnd == zds->outStart */
		//            if (!zds->hostageByte) { /* output not fully flushed; keep last byte as hostage; will be released when all output is flushed */
		//                input->pos--;   /* note : pos > 0, otherwise, impossible to finish reading last block */
		//                zds->hostageByte=1;
		//            }
		//            return 1;
		//        }  /* nextSrcSizeHint==0 */
		//        nextSrcSizeHint +=  ZSTD_blockHeaderSize * (NextInputType(zds) == ZSTDnit_block);   /* preload header of next block */
		//        Debug.Assert(zds->inPos <= nextSrcSizeHint);
		//        nextSrcSizeHint -= zds->inPos;   /* part already loaded*/
		//        return nextSrcSizeHint;
		//    }
		//}


		//size_t ZSTD_decompress_generic(ZSTD_DCtx dctx, ZSTD_outBuffer* output, ZSTD_inBuffer* input)
		//{
		//    return DecompressStream(dctx, output, input);
		//}

		//size_t ZSTD_decompress_generic_simpleArgs (
		//                            ZSTD_DCtx dctx,
		//                            void* dst, size_t dstCapacity, size_t* dstPos,
		//                      void* src, size_t srcSize, size_t* srcPos)
		//{
		//    ZSTD_outBuffer output = { dst, dstCapacity, *dstPos };
		//    ZSTD_inBuffer  input  = { src, srcSize, *srcPos };
		//    /* ZSTD_compress_generic() will check validity of dstPos and srcPos */
		//    size_t  cErr = ZSTD_decompress_generic(dctx, &output, &input);
		//    *dstPos = output.pos;
		//    *srcPos = input.pos;
		//    return cErr;
		//}

		//void ZSTD_DCtx_reset(ZSTD_DCtx dctx)
		//{
		//    (void)InitDStream(dctx);
		//    dctx->format = ZSTD_format_e.ZSTD_f_zstd1;
		//    dctx->maxWindowSize = ZSTD_MAXWINDOWSIZE_DEFAULT;
		//}
	}
}
