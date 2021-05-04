/*
 * Copyright (c) 2016-present, Yann Collet, Facebook, Inc.
 * All rights reserved.
 *
 * This source code is licensed under both the BSD-style license (found in the
 * LICENSE file in the root directory of this source tree) and the GPLv2 (found
 * in the COPYING file in the root directory of this source tree).
 * You may select, at your option, one of the above-listed licenses.
 */

using System.Runtime.InteropServices;
using BYTE = System.Byte;
using size_t = System.UInt32;
using U16 = System.UInt16;
using U32 = System.UInt32;
using U64 = System.UInt64;

namespace EPAM.Deltix.ZStd
{
	internal static unsafe class ZStd
	{
		//#if defined (__cplusplus)
		//extern "C" {
		//#endif

		//#ifndef ZSTD_H_235446
		//#define ZSTD_H_235446

		///* ======   Dependency   ======*/
		//#include <stddef.h>   /* size_t */


		///* =====   ZSTDLIB_API : control library symbols visibility   ===== */
		//#ifndef ZSTDLIB_VISIBILITY
		//#  if defined(__GNUC__) && (__GNUC__ >= 4)
		//#    define ZSTDLIB_VISIBILITY __attribute__ ((visibility ("default")))
		//#  else
		//#    define ZSTDLIB_VISIBILITY
		//#  endif
		//#endif
		//#if defined(ZSTD_DLL_EXPORT) && (ZSTD_DLL_EXPORT==1)
		//#  define ZSTDLIB_API __declspec(dllexport) ZSTDLIB_VISIBILITY
		//#elif defined(ZSTD_DLL_IMPORT) && (ZSTD_DLL_IMPORT==1)
		//#  define ZSTDLIB_API __declspec(dllimport) ZSTDLIB_VISIBILITY /* It isn't required but allows to generate better code, saving a function pointer load from the IAT and an indirect jump.*/
		//#else
		//#  define ZSTDLIB_API ZSTDLIB_VISIBILITY
		//#endif


		///*******************************************************************************************************
		//  Introduction

		//  zstd, short for Zstandard, is a fast lossless compression algorithm,
		//  targeting real-time compression scenarios at zlib-level and better compression ratios.
		//  The zstd compression library provides in-memory compression and decompression functions.
		//  The library supports compression levels from 1 up to MaxCLevel() which is currently 22.
		//  Levels >= 20, labeled `--ultra`, should be used with caution, as they require more memory.
		//  Compression can be done in:
		//    - a single step (described as Simple API)
		//    - a single step, reusing a context (described as Explicit context)
		//    - unbounded multiple steps (described as Streaming compression)
		//  The compression ratio achievable on small data can be highly improved using a dictionary in:
		//    - a single step (described as Simple dictionary API)
		//    - a single step, reusing a dictionary (described as Bulk-processing dictionary API)

		//  Advanced experimental functions can be accessed using #define ZSTD_STATIC_LINKING_ONLY before including zstd.h.
		//  Advanced experimental APIs shall never be used with a dynamic library.
		//  They are not "stable", their definition may change in the future. Only static linking is allowed.
		//*********************************************************************************************************/

		///*------   Version   ------*/
		//#define ZSTD_VERSION_MAJOR    1
		//#define ZSTD_VERSION_MINOR    3
		//#define ZSTD_VERSION_RELEASE  4

		//#define ZSTD_VERSION_NUMBER  (ZSTD_VERSION_MAJOR *100*100 + ZSTD_VERSION_MINOR *100 + ZSTD_VERSION_RELEASE)
		//ZSTDLIB_API unsigned VersionNumber(void);   /**< useful to check dll version */

		//#define ZSTD_LIB_VERSION ZSTD_VERSION_MAJOR.ZSTD_VERSION_MINOR.ZSTD_VERSION_RELEASE
		//#define ZSTD_QUOTE(str) #str
		//#define ZSTD_EXPAND_AND_QUOTE(str) ZSTD_QUOTE(str)
		//#define ZSTD_VERSION_STRING ZSTD_EXPAND_AND_QUOTE(ZSTD_LIB_VERSION)
		//ZSTDLIB_API readonly char* VersionString(void);   /* added in v1.3.0 */


		///***************************************
		//*  Simple API
		//***************************************/
		///*! Compress() :
		// *  Compresses `src` content as a single zstd compressed frame into already allocated `dst`.
		// *  Hint : compression runs faster if `dstCapacity` >=  `CompressBound(srcSize)`.
		// *  @return : compressed size written into `dst` (<= `dstCapacity),
		// *            or an error code if it fails (which can be tested using IsError()). */
		//ZSTDLIB_API size_t Compress( void* dst, size_t dstCapacity,
		//                            void* src, size_t srcSize,
		//                                  int compressionLevel);

		///*! Decompress() :
		// *  `compressedSize` : must be the _exact_ size of some number of compressed and/or skippable frames.
		// *  `dstCapacity` is an upper bound of originalSize to regenerate.
		// *  If user cannot imply a maximum upper bound, it's better to use streaming mode to decompress data.
		// *  @return : the number of bytes decompressed into `dst` (<= `dstCapacity`),
		// *            or an errorCode if it fails (which can be tested using IsError()). */
		//ZSTDLIB_API size_t Decompress( void* dst, size_t dstCapacity,
		//                              void* src, size_t compressedSize);

		///*! GetFrameContentSize() : added in v1.3.0
		// *  `src` should point to the start of a ZSTD encoded frame.
		// *  `srcSize` must be at least as large as the frame header.
		// *            hint : any size >= `ZSTD_frameHeaderSize_max` is large enough.
		// *  @return : - decompressed size of the frame in `src`, if known
		// *            - ZSTD_CONTENTSIZE_UNKNOWN if the size cannot be determined
		// *            - ZSTD_CONTENTSIZE_ERROR if an error occurred (e.g. invalid magic number, srcSize too small)
		// *   note 1 : a 0 return value means the frame is valid but "empty".
		// *   note 2 : decompressed size is an optional field, it may not be present, typically in streaming mode.
		// *            When `return==ZSTD_CONTENTSIZE_UNKNOWN`, data to decompress could be any size.
		// *            In which case, it's necessary to use streaming mode to decompress data.
		// *            Optionally, application can rely on some implicit limit,
		// *            as Decompress() only needs an upper bound of decompressed size.
		// *            (For example, data could be necessarily cut into blocks <= 16 KB).
		// *   note 3 : decompressed size is always present when compression is done with Compress()
		// *   note 4 : decompressed size can be very large (64-bits value),
		// *            potentially larger than what local system can handle as a single memory segment.
		// *            In which case, it's necessary to use streaming mode to decompress data.
		// *   note 5 : If source is untrusted, decompressed size could be wrong or intentionally modified.
		// *            Always ensure return value fits within application's authorized limits.
		// *            Each application can set its own limits.
		// *   note 6 : This function replaces GetDecompressedSize() */
		public const ulong ZSTD_CONTENTSIZE_UNKNOWN = U64.MaxValue - 0;
		public const ulong ZSTD_CONTENTSIZE_ERROR = U64.MaxValue - 1;
		//ZSTDLIB_API unsigned long long GetFrameContentSize(readonly void *src, size_t srcSize);

		///*! GetDecompressedSize() :
		// *  NOTE: This function is now obsolete, in favor of GetFrameContentSize().
		// *  Both functions work the same way, but GetDecompressedSize() blends
		// *  "empty", "unknown" and "error" results to the same return value (0),
		// *  while GetFrameContentSize() gives them separate return values.
		// * `src` is the start of a zstd compressed frame.
		// * @return : content size to be decompressed, as a 64-bits value _if known and not empty_, 0 otherwise. */
		//ZSTDLIB_API unsigned long long GetDecompressedSize(void* src, size_t srcSize);


		///*======  Helper functions  ======*/
		//#define ZSTD_COMPRESSBOUND(srcSize)   ((srcSize) + ((srcSize)>>8) + (((srcSize) < (128<<10)) ? (((128<<10) - (srcSize)) >> 11) /* margin, from 64 to 0 */ : 0))  /* this formula ensures that bound(A) + bound(B) <= bound(A+B) as long as A and B >= 128 KB */
		//ZSTDLIB_API size_t      CompressBound(size_t srcSize); /*!< maximum compressed size in worst case single-pass scenario */
		//ZSTDLIB_API unsigned    IsError(size_t code);          /*!< tells if a `size_t` function result is an error code */
		//ZSTDLIB_API readonly char* GetErrorName(size_t code);     /*!< provides readable string from an error code */
		//ZSTDLIB_API int         MaxCLevel(void);               /*!< maximum compression level available */


		///***************************************
		//*  Explicit context
		//***************************************/
		///*= Compression context
		// *  When compressing many times,
		// *  it is recommended to allocate a context just once, and re-use it for each successive compression operation.
		// *  This will make workload friendlier for system's memory.
		// *  Use one context per thread for parallel execution in multi-threaded environments. */
		//typedef struct ZSTD_CCtx_s ZSTD_CCtx;
		//ZSTDLIB_API ZSTD_CCtx* CreateCCtx(void);
		//ZSTDLIB_API size_t     FreeCCtx(ZSTD_CCtx* cctx);

		///*! CompressCCtx() :
		// *  Same as Compress(), requires an allocated ZSTD_CCtx (see CreateCCtx()). */
		//ZSTDLIB_API size_t CompressCCtx(ZSTD_CCtx* ctx,
		//                                     void* dst, size_t dstCapacity,
		//                               void* src, size_t srcSize,
		//                                     int compressionLevel);

		///*= Decompression context
		// *  When decompressing many times,
		// *  it is recommended to allocate a context only once,
		// *  and re-use it for each successive compression operation.
		// *  This will make workload friendlier for system's memory.
		// *  Use one context per thread for parallel execution. */
		//typedef struct ZSTD_DCtx_s ZSTD_DCtx;
		//ZSTDLIB_API ZSTD_DCtx CreateDCtx(void);
		//ZSTDLIB_API size_t     FreeDCtx(ZSTD_DCtx dctx);

		///*! DecompressDCtx() :
		// *  Same as Decompress(), requires an allocated ZSTD_DCtx (see CreateDCtx()) */
		//ZSTDLIB_API size_t DecompressDCtx(ZSTD_DCtx ctx,
		//                                       void* dst, size_t dstCapacity,
		//                                 void* src, size_t srcSize);


		///**************************
		//*  Simple dictionary API
		//***************************/
		///*! ZSTD_compress_usingDict() :
		// *  Compression using a predefined Dictionary (see dictBuilder/zdict.h).
		// *  Note : This function loads the dictionary, resulting in significant startup delay.
		// *  Note : When `dict == null || dictSize < 8` no dictionary is used. */
		//ZSTDLIB_API size_t ZSTD_compress_usingDict(ZSTD_CCtx* ctx,
		//                                           void* dst, size_t dstCapacity,
		//                                     void* src, size_t srcSize,
		//                                     void* dict,size_t dictSize,
		//                                           int compressionLevel);

		///*! ZSTD_decompress_usingDict() :
		// *  Decompression using a predefined Dictionary (see dictBuilder/zdict.h).
		// *  Dictionary must be identical to the one used during compression.
		// *  Note : This function loads the dictionary, resulting in significant startup delay.
		// *  Note : When `dict == null || dictSize < 8` no dictionary is used. */
		//ZSTDLIB_API size_t ZSTD_decompress_usingDict(ZSTD_DCtx dctx,
		//                                             void* dst, size_t dstCapacity,
		//                                       void* src, size_t srcSize,
		//                                       void* dict,size_t dictSize);


		///**********************************
		// *  Bulk processing dictionary API
		// *********************************/
		//typedef struct ZSTD_CDict_s ZSTD_CDict;

		///*! CreateCDict() :
		// *  When compressing multiple messages / blocks with the same dictionary, it's recommended to load it just once.
		// *  CreateCDict() will create a digested dictionary, ready to start future compression operations without startup delay.
		// *  ZSTD_CDict can be created once and shared by multiple threads concurrently, since its usage is read-only.
		// *  `dictBuffer` can be released after ZSTD_CDict creation, since its content is copied within CDict */
		//ZSTDLIB_API ZSTD_CDict* CreateCDict(void* dictBuffer, size_t dictSize,
		//                                         int compressionLevel);

		///*! FreeCDict() :
		// *  Function frees memory allocated by CreateCDict(). */
		//ZSTDLIB_API size_t      FreeCDict(ZSTD_CDict* CDict);

		///*! ZSTD_compress_usingCDict() :
		// *  Compression using a digested Dictionary.
		// *  Faster startup than ZSTD_compress_usingDict(), recommended when same dictionary is used multiple times.
		// *  Note that compression level is decided during dictionary creation.
		// *  Frame parameters are hardcoded (dictID=yes, contentSize=yes, checksum=no) */
		//ZSTDLIB_API size_t ZSTD_compress_usingCDict(ZSTD_CCtx* cctx,
		//                                            void* dst, size_t dstCapacity,
		//                                      void* src, size_t srcSize,
		//                                      readonly ZSTD_CDict* cdict);


		//typedef struct ZSTD_DDict_s ZSTD_DDict;

		///*! CreateDDict() :
		// *  Create a digested dictionary, ready to start decompression operation without startup delay.
		// *  dictBuffer can be released after DDict creation, as its content is copied inside DDict */
		//ZSTDLIB_API ZSTD_DDict* CreateDDict(void* dictBuffer, size_t dictSize);

		///*! FreeDDict() :
		// *  Function frees memory allocated with CreateDDict() */
		//ZSTDLIB_API size_t      FreeDDict(ZSTD_DDict* ddict);

		///*! ZSTD_decompress_usingDDict() :
		// *  Decompression using a digested Dictionary.
		// *  Faster startup than ZSTD_decompress_usingDict(), recommended when same dictionary is used multiple times. */
		//ZSTDLIB_API size_t ZSTD_decompress_usingDDict(ZSTD_DCtx dctx,
		//                                              void* dst, size_t dstCapacity,
		//                                        void* src, size_t srcSize,
		//                                        readonly ZSTD_DDict* ddict);


		///****************************
		//*  Streaming
		//****************************/

		//typedef struct ZSTD_inBuffer_s {
		//  void* src;    /**< start of input buffer */
		//  size_t size;        /**< size of input buffer */
		//  size_t pos;         /**< position where reading stopped. Will be updated. Necessarily 0 <= pos <= size */
		//} ZSTD_inBuffer;

		//typedef struct ZSTD_outBuffer_s {
		//  void*  dst;         /**< start of output buffer */
		//  size_t size;        /**< size of output buffer */
		//  size_t pos;         /**< position where writing stopped. Will be updated. Necessarily 0 <= pos <= size */
		//} ZSTD_outBuffer;



		///*-***********************************************************************
		//*  Streaming compression - HowTo
		//*
		//*  A ZSTD_CStream object is required to track streaming operation.
		//*  Use CreateCStream() and FreeCStream() to create/release resources.
		//*  ZSTD_CStream objects can be reused multiple times on consecutive compression operations.
		//*  It is recommended to re-use ZSTD_CStream in situations where many streaming operations will be achieved consecutively,
		//*  since it will play nicer with system's memory, by re-using already allocated memory.
		//*  Use one separate ZSTD_CStream per thread for parallel execution.
		//*
		//*  Start a new compression by initializing ZSTD_CStream.
		//*  Use InitCStream() to start a new compression operation.
		//*  Use ZSTD_initCStream_usingDict() or ZSTD_initCStream_usingCDict() for a compression which requires a dictionary (experimental section)
		//*
		//*  Use CompressStream() repetitively to consume input stream.
		//*  The function will automatically update both `pos` fields.
		//*  Note that it may not consume the entire input, in which case `pos < size`,
		//*  and it's up to the caller to present again remaining data.
		//*  @return : a size hint, preferred nb of bytes to use as input for next function call
		//*            or an error code, which can be tested using IsError().
		//*            Note 1 : it's just a hint, to help latency a little, any other value will work fine.
		//*            Note 2 : size hint is guaranteed to be <= ZSTD_CStreamInSize()
		//*
		//*  At any moment, it's possible to flush whatever data remains within internal buffer, using FlushStream().
		//*  `output->pos` will be updated.
		//*  Note that some content might still be left within internal buffer if `output->size` is too small.
		//*  @return : nb of bytes still present within internal buffer (0 if it's empty)
		//*            or an error code, which can be tested using IsError().
		//*
		//*  EndStream() instructs to finish a frame.
		//*  It will perform a flush and write frame epilogue.
		//*  The epilogue is required for decoders to consider a frame completed.
		//*  EndStream() may not be able to flush full data if `output->size` is too small.
		//*  In which case, call again EndStream() to complete the flush.
		//*  @return : 0 if frame fully completed and fully flushed,
		//             or >0 if some data is still present within internal buffer
		//                  (value is minimum size estimation for remaining data to flush, but it could be more)
		//*            or an error code, which can be tested using IsError().
		//*
		//* *******************************************************************/

		//typedef ZSTD_CCtx ZSTD_CStream;  /**< CCtx and CStream are now effectively same object (>= v1.3.0) */
		//                                 /* Continue to distinguish them for compatibility with versions <= v1.2.0 */
		///*===== ZSTD_CStream management functions =====*/
		//ZSTDLIB_API ZSTD_CStream* CreateCStream(void);
		//ZSTDLIB_API size_t FreeCStream(ZSTD_CStream* zcs);

		///*===== Streaming compression functions =====*/
		//ZSTDLIB_API size_t InitCStream(ZSTD_CStream* zcs, int compressionLevel);
		//ZSTDLIB_API size_t CompressStream(ZSTD_CStream* zcs, ZSTD_outBuffer* output, ZSTD_inBuffer* input);
		//ZSTDLIB_API size_t FlushStream(ZSTD_CStream* zcs, ZSTD_outBuffer* output);
		//ZSTDLIB_API size_t EndStream(ZSTD_CStream* zcs, ZSTD_outBuffer* output);

		//ZSTDLIB_API size_t ZSTD_CStreamInSize(void);    /**< recommended size for input buffer */
		//ZSTDLIB_API size_t ZSTD_CStreamOutSize(void);   /**< recommended size for output buffer. Guarantee to successfully flush at least one complete compressed block in all circumstances. */



		///*-***************************************************************************
		//*  Streaming decompression - HowTo
		//*
		//*  A ZSTD_DStream object is required to track streaming operations.
		//*  Use CreateDStream() and FreeDStream() to create/release resources.
		//*  ZSTD_DStream objects can be re-used multiple times.
		//*
		//*  Use InitDStream() to start a new decompression operation,
		//*   or ZSTD_initDStream_usingDict() if decompression requires a dictionary.
		//*   @return : recommended first input size
		//*
		//*  Use DecompressStream() repetitively to consume your input.
		//*  The function will update both `pos` fields.
		//*  If `input.pos < input.size`, some input has not been consumed.
		//*  It's up to the caller to present again remaining data.
		//*  If `output.pos < output.size`, decoder has flushed everything it could.
		//*  @return : 0 when a frame is completely decoded and fully flushed,
		//*            an error code, which can be tested using IsError(),
		//*            any other value > 0, which means there is still some decoding to do to complete current frame.
		//*            The return value is a suggested next input size (a hint to improve latency) that will never load more than the current frame.
		//* *******************************************************************************/

		//typedef ZSTD_DCtx ZSTD_DStream;  /**< DCtx and DStream are now effectively same object (>= v1.3.0) */
		//                                 /* For compatibility with versions <= v1.2.0, continue to consider them separated. */
		///*===== ZSTD_DStream management functions =====*/
		//ZSTDLIB_API ZSTD_DStream* CreateDStream(void);
		//ZSTDLIB_API size_t FreeDStream(ZSTD_DStream* zds);

		///*===== Streaming decompression functions =====*/
		//ZSTDLIB_API size_t InitDStream(ZSTD_DStream* zds);
		//ZSTDLIB_API size_t DecompressStream(ZSTD_DStream* zds, ZSTD_outBuffer* output, ZSTD_inBuffer* input);

		//ZSTDLIB_API size_t ZSTD_DStreamInSize(void);    /*!< recommended size for input buffer */
		//ZSTDLIB_API size_t ZSTD_DStreamOutSize(void);   /*!< recommended size for output buffer. Guarantee to successfully flush at least one complete block in all circumstances. */

		//#endif  /* ZSTD_H_235446 */



		///****************************************************************************************
		// * START OF ADVANCED AND EXPERIMENTAL FUNCTIONS
		// * The definitions in this section are considered experimental.
		// * They should never be used with a dynamic library, as prototypes may change in the future.
		// * They are provided for advanced scenarios.
		// * Use them only in association with static linking.
		// * ***************************************************************************************/

		//#if defined(ZSTD_STATIC_LINKING_ONLY) && !defined(ZSTD_H_ZSTD_STATIC_LINKING_ONLY)
		//#define ZSTD_H_ZSTD_STATIC_LINKING_ONLY

		///* --- Constants ---*/
		public const uint ZSTD_MAGICNUMBER = 0xFD2FB528;/* >= v0.8.0 */
		public const uint ZSTD_MAGIC_SKIPPABLE_START = 0x184D2A50U;
		public const uint ZSTD_MAGIC_DICTIONARY = 0xEC30A437;/* >= v0.7.0 */

		public const int ZSTD_WINDOWLOG_MAX_32 = 30;
		public const int ZSTD_WINDOWLOG_MAX_64 = 31;
		public const uint ZSTD_WINDOWLOG_MAX = ((uint)(sizeof(size_t) == 4 ? ZSTD_WINDOWLOG_MAX_32 : ZSTD_WINDOWLOG_MAX_64));
		public const int ZSTD_WINDOWLOG_MIN = 10;
		public const uint ZSTD_HASHLOG_MAX = ((ZSTD_WINDOWLOG_MAX < 30) ? ZSTD_WINDOWLOG_MAX : 30);
		public const int ZSTD_HASHLOG_MIN = 6;
		public const int ZSTD_CHAINLOG_MAX_32 = 29;
		public const int ZSTD_CHAINLOG_MAX_64 = 30;
		public const uint ZSTD_CHAINLOG_MAX = ((uint)(sizeof(size_t) == 4 ? ZSTD_CHAINLOG_MAX_32 : ZSTD_CHAINLOG_MAX_64));
		public const int ZSTD_CHAINLOG_MIN = ZSTD_HASHLOG_MIN;
		public const int ZSTD_HASHLOG3_MAX = 17;
		public const uint ZSTD_SEARCHLOG_MAX = (ZSTD_WINDOWLOG_MAX - 1);
		public const int ZSTD_SEARCHLOG_MIN = 1;
		public const int ZSTD_SEARCHLENGTH_MAX = 7;   /* only for ZSTD_fast, other strategies are limited to 6 */
		public const int ZSTD_SEARCHLENGTH_MIN = 3;   /* only for ZSTD_btopt, other strategies are limited to 4 */
		public const int ZSTD_TARGETLENGTH_MIN = 1;   /* only used by btopt, btultra and btfast */
		public const int ZSTD_LDM_MINMATCH_MIN = 4;
		public const int ZSTD_LDM_MINMATCH_MAX = 4096;
		public const int ZSTD_LDM_BUCKETSIZELOG_MAX = 8;

		public const int ZSTD_FRAMEHEADERSIZE_PREFIX = 5;   /* minimum input size to know frame header size */
		public const int ZSTD_FRAMEHEADERSIZE_MIN = 6;
		public const int ZSTD_FRAMEHEADERSIZE_MAX = 18;   /* for static allocation */
		public const size_t ZSTD_frameHeaderSize_prefix = ZSTD_FRAMEHEADERSIZE_PREFIX;
		public const size_t ZSTD_frameHeaderSize_min = ZSTD_FRAMEHEADERSIZE_MIN;
		public const size_t ZSTD_frameHeaderSize_max = ZSTD_FRAMEHEADERSIZE_MAX;
		public const size_t ZSTD_skippableHeaderSize = 8;  /* magic number + skippable frame length */


		///*--- Advanced types ---*/
		//typedef enum { ZSTD_fast=1, ZSTD_dfast, ZSTD_greedy, ZSTD_lazy, ZSTD_lazy2,
		//               ZSTD_btlazy2, ZSTD_btopt, ZSTD_btultra } ZSTD_strategy;   /* from faster to stronger */

		//typedef struct {
		//    unsigned windowLog;      /**< largest match distance : larger == more compression, more memory needed during decompression */
		//    unsigned chainLog;       /**< fully searched segment : larger == more compression, slower, more memory (useless for fast) */
		//    unsigned hashLog;        /**< dispatch table : larger == faster, more memory */
		//    unsigned searchLog;      /**< nb of searches : larger == more compression, slower */
		//    unsigned searchLength;   /**< match length searched : larger == faster decompression, sometimes less compression */
		//    unsigned targetLength;   /**< acceptable match size for optimal parser (only) : larger == more compression, slower */
		//    ZSTD_strategy strategy;
		//} ZSTD_compressionParameters;

		//typedef struct {
		//    unsigned contentSizeFlag; /**< 1: content size will be in frame header (when known) */
		//    unsigned checksumFlag;    /**< 1: generate a 32-bits checksum at end of frame, for error detection */
		//    unsigned noDictIDFlag;    /**< 1: no dictID will be saved into frame header (if dictionary compression) */
		//} ZSTD_frameParameters;

		//typedef struct {
		//    ZSTD_compressionParameters cParams;
		//    ZSTD_frameParameters fParams;
		//} ZSTD_parameters;

		//typedef struct ZSTD_CCtx_params_s ZSTD_CCtx_params;

		//typedef enum {
		//    ZSTD_dct_auto=0,      /* dictionary is "full" when starting with ZSTD_MAGIC_DICTIONARY, otherwise it is "rawContent" */
		//    ZSTD_dct_rawContent,  /* ensures dictionary is always loaded as rawContent, even if it starts with ZSTD_MAGIC_DICTIONARY */
		//    ZSTD_dct_fullDict     /* refuses to load a dictionary if it does not respect Zstandard's specification */
		//} ZSTD_dictContentType_e;

		//typedef enum {
		//    ZSTD_dlm_byCopy = 0, /**< Copy dictionary content internally */
		//    ZSTD_dlm_byRef,      /**< Reference dictionary content -- the dictionary buffer must outlive its users. */
		//} ZSTD_dictLoadMethod_e;



		///***************************************
		//*  Frame size functions
		//***************************************/

		///*! FindFrameCompressedSize() :
		// *  `src` should point to the start of a ZSTD encoded frame or skippable frame
		// *  `srcSize` must be >= first frame size
		// *  @return : the compressed size of the first frame starting at `src`,
		// *            suitable to pass to `ZSTD_decompress` or similar,
		// *            or an error code if input is invalid */
		//ZSTDLIB_API size_t FindFrameCompressedSize(void* src, size_t srcSize);

		///*! FindDecompressedSize() :
		// *  `src` should point the start of a series of ZSTD encoded and/or skippable frames
		// *  `srcSize` must be the _exact_ size of this series
		// *       (i.e. there should be a frame boundary exactly at `srcSize` bytes after `src`)
		// *  @return : - decompressed size of all data in all successive frames
		// *            - if the decompressed size cannot be determined: ZSTD_CONTENTSIZE_UNKNOWN
		// *            - if an error occurred: ZSTD_CONTENTSIZE_ERROR
		// *
		// *   note 1 : decompressed size is an optional field, that may not be present, especially in streaming mode.
		// *            When `return==ZSTD_CONTENTSIZE_UNKNOWN`, data to decompress could be any size.
		// *            In which case, it's necessary to use streaming mode to decompress data.
		// *   note 2 : decompressed size is always present when compression is done with Compress()
		// *   note 3 : decompressed size can be very large (64-bits value),
		// *            potentially larger than what local system can handle as a single memory segment.
		// *            In which case, it's necessary to use streaming mode to decompress data.
		// *   note 4 : If source is untrusted, decompressed size could be wrong or intentionally modified.
		// *            Always ensure result fits within application's authorized limits.
		// *            Each application can set its own limits.
		// *   note 5 : ZSTD_findDecompressedSize handles multiple frames, and so it must traverse the input to
		// *            read each contained frame header.  This is fast as most of the data is skipped,
		// *            however it does mean that all frame data must be present and valid. */
		//ZSTDLIB_API unsigned long long FindDecompressedSize(void* src, size_t srcSize);

		///** FrameHeaderSize() :
		// *  srcSize must be >= ZStd.ZSTD_frameHeaderSize_prefix.
		// * @return : size of the Frame Header,
		// *           or an error code (if srcSize is too small) */
		//ZSTDLIB_API size_t FrameHeaderSize(void* src, size_t srcSize);


		///***************************************
		//*  Memory management
		//***************************************/

		///*! ZSTD_sizeof_*() :
		// *  These functions give the current memory usage of selected object.
		// *  Object memory usage can evolve when re-used. */
		//ZSTDLIB_API size_t ZSTD_sizeof_CCtx(readonly ZSTD_CCtx* cctx);
		//ZSTDLIB_API size_t ZSTD_sizeof_DCtx(readonly ZSTD_DCtx dctx);
		//ZSTDLIB_API size_t ZSTD_sizeof_CStream(readonly ZSTD_CStream* zcs);
		//ZSTDLIB_API size_t ZSTD_sizeof_DStream(readonly ZSTD_DStream* zds);
		//ZSTDLIB_API size_t ZSTD_sizeof_CDict(readonly ZSTD_CDict* cdict);
		//ZSTDLIB_API size_t ZSTD_sizeof_DDict(readonly ZSTD_DDict* ddict);

		///*! ZSTD_estimate*() :
		// *  These functions make it possible to estimate memory usage
		// *  of a future {D,C}Ctx, before its creation.
		// *  EstimateCCtxSize() will provide a budget large enough for any compression level up to selected one.
		// *  It will also consider src size to be arbitrarily "large", which is worst case.
		// *  If srcSize is known to always be small, ZSTD_estimateCCtxSize_usingCParams() can provide a tighter estimation.
		// *  ZSTD_estimateCCtxSize_usingCParams() can be used in tandem with GetCParams() to create cParams from compressionLevel.
		// *  ZSTD_estimateCCtxSize_usingCCtxParams() can be used in tandem with ZSTD_CCtxParam_setParameter(). Only single-threaded compression is supported. This function will return an error code if ZSTD_p_nbWorkers is >= 1.
		// *  Note : CCtx size estimation is only correct for single-threaded compression. */
		//ZSTDLIB_API size_t EstimateCCtxSize(int compressionLevel);
		//ZSTDLIB_API size_t ZSTD_estimateCCtxSize_usingCParams(ZSTD_compressionParameters cParams);
		//ZSTDLIB_API size_t ZSTD_estimateCCtxSize_usingCCtxParams(readonly ZSTD_CCtx_params* params);
		//ZSTDLIB_API size_t EstimateDCtxSize(void);

		///*! EstimateCStreamSize() :
		// *  EstimateCStreamSize() will provide a budget large enough for any compression level up to selected one.
		// *  It will also consider src size to be arbitrarily "large", which is worst case.
		// *  If srcSize is known to always be small, ZSTD_estimateCStreamSize_usingCParams() can provide a tighter estimation.
		// *  ZSTD_estimateCStreamSize_usingCParams() can be used in tandem with GetCParams() to create cParams from compressionLevel.
		// *  ZSTD_estimateCStreamSize_usingCCtxParams() can be used in tandem with ZSTD_CCtxParam_setParameter(). Only single-threaded compression is supported. This function will return an error code if ZSTD_p_nbWorkers is >= 1.
		// *  Note : CStream size estimation is only correct for single-threaded compression.
		// *  ZSTD_DStream memory budget depends on window Size.
		// *  This information can be passed manually, using ZSTD_estimateDStreamSize,
		// *  or deducted from a valid frame Header, using ZSTD_estimateDStreamSize_fromFrame();
		// *  Note : if streaming is init with function ZSTD_init?Stream_usingDict(),
		// *         an internal ?Dict will be created, which additional size is not estimated here.
		// *         In this case, get total size by adding ZSTD_estimate?DictSize */
		//ZSTDLIB_API size_t EstimateCStreamSize(int compressionLevel);
		//ZSTDLIB_API size_t ZSTD_estimateCStreamSize_usingCParams(ZSTD_compressionParameters cParams);
		//ZSTDLIB_API size_t ZSTD_estimateCStreamSize_usingCCtxParams(readonly ZSTD_CCtx_params* params);
		//ZSTDLIB_API size_t EstimateDStreamSize(size_t windowSize);
		//ZSTDLIB_API size_t ZSTD_estimateDStreamSize_fromFrame(void* src, size_t srcSize);

		///*! ZSTD_estimate?DictSize() :
		// *  EstimateCDictSize() will bet that src size is relatively "small", and content is copied, like CreateCDict().
		// *  ZSTD_estimateCDictSize_advanced() makes it possible to control compression parameters precisely, like ZSTD_createCDict_advanced().
		// *  Note : dictionaries created by reference (`ZSTD_dlm_byRef`) are logically smaller.
		// */
		//ZSTDLIB_API size_t EstimateCDictSize(size_t dictSize, int compressionLevel);
		//ZSTDLIB_API size_t ZSTD_estimateCDictSize_advanced(size_t dictSize, ZSTD_compressionParameters cParams, ZSTD_dictLoadMethod_e dictLoadMethod);
		//ZSTDLIB_API size_t EstimateDDictSize(size_t dictSize, ZSTD_dictLoadMethod_e dictLoadMethod);

		///*! ZSTD_initStatic*() :
		// *  Initialize an object using a pre-allocated fixed-size buffer.
		// *  workspace: The memory area to emplace the object into.
		// *             Provided pointer *must be 8-bytes aligned*.
		// *             Buffer must outlive object.
		// *  workspaceSize: Use ZSTD_estimate*Size() to determine
		// *                 how large workspace must be to support target scenario.
		// * @return : pointer to object (same address as workspace, just different type),
		// *           or null if error (size too small, incorrect alignment, etc.)
		// *  Note : zstd will never resize nor malloc() when using a static buffer.
		// *         If the object requires more memory than available,
		// *         zstd will just error out (typically ZSTD_error_memory_allocation).
		// *  Note 2 : there is no corresponding "free" function.
		// *           Since workspace is allocated externally, it must be freed externally too.
		// *  Note 3 : cParams : use GetCParams() to convert a compression level
		// *           into its associated cParams.
		// *  Limitation 1 : currently not compatible with internal dictionary creation, triggered by
		// *                 ZSTD_CCtx_loadDictionary(), ZSTD_initCStream_usingDict() or ZSTD_initDStream_usingDict().
		// *  Limitation 2 : static cctx currently not compatible with multi-threading.
		// *  Limitation 3 : static dctx is incompatible with legacy support.
		// */
		//ZSTDLIB_API ZSTD_CCtx*    InitStaticCCtx(void* workspace, size_t workspaceSize);
		//ZSTDLIB_API ZSTD_CStream* InitStaticCStream(void* workspace, size_t workspaceSize);    /**< same as InitStaticCCtx() */

		//ZSTDLIB_API ZSTD_DCtx    InitStaticDCtx(void* workspace, size_t workspaceSize);
		//ZSTDLIB_API ZSTD_DStream* InitStaticDStream(void* workspace, size_t workspaceSize);    /**< same as InitStaticDCtx() */

		//ZSTDLIB_API readonly ZSTD_CDict* InitStaticCDict(
		//                                        void* workspace, size_t workspaceSize,
		//                                        void* dict, size_t dictSize,
		//                                        ZSTD_dictLoadMethod_e dictLoadMethod,
		//                                        ZSTD_dictContentType_e dictContentType,
		//                                        ZSTD_compressionParameters cParams);

		//ZSTDLIB_API readonly ZSTD_DDict* InitStaticDDict(
		//                                        void* workspace, size_t workspaceSize,
		//                                        void* dict, size_t dictSize,
		//                                        ZSTD_dictLoadMethod_e dictLoadMethod,
		//                                        ZSTD_dictContentType_e dictContentType);

		///*! Custom memory allocation :
		// *  These prototypes make it possible to pass your own allocation/free functions.
		// *  ZSTD_customMem is provided at creation time, using ZSTD_create*_advanced() variants listed below.
		// *  All allocation/free operations will be completed using these custom variants instead of regular <stdlib.h> ones.
		// */
		//typedef void* (*ZSTD_allocFunction) (void* opaque, size_t size);
		//typedef void  (*ZSTD_freeFunction) (void* opaque, void* address);
		//typedef struct { ZSTD_allocFunction customAlloc; ZSTD_freeFunction customFree; void* opaque; } ZSTD_customMem;
		//static readonly ZSTD_customMem readonly ZSTD_defaultCMem = { null, null, null };  /**< this constant defers to stdlib's functions */

		//ZSTDLIB_API ZSTD_CCtx*    ZSTD_createCCtx_advanced(ZSTD_customMem customMem);
		//ZSTDLIB_API ZSTD_CStream* ZSTD_createCStream_advanced(ZSTD_customMem customMem);
		//ZSTDLIB_API ZSTD_DCtx    ZSTD_createDCtx_advanced(ZSTD_customMem customMem);
		//ZSTDLIB_API ZSTD_DStream* ZSTD_createDStream_advanced(ZSTD_customMem customMem);

		//ZSTDLIB_API ZSTD_CDict* ZSTD_createCDict_advanced(void* dict, size_t dictSize,
		//                                                  ZSTD_dictLoadMethod_e dictLoadMethod,
		//                                                  ZSTD_dictContentType_e dictContentType,
		//                                                  ZSTD_compressionParameters cParams,
		//                                                  ZSTD_customMem customMem);

		//ZSTDLIB_API ZSTD_DDict* ZSTD_createDDict_advanced(void* dict, size_t dictSize,
		//                                                  ZSTD_dictLoadMethod_e dictLoadMethod,
		//                                                  ZSTD_dictContentType_e dictContentType,
		//                                                  ZSTD_customMem customMem);



		///***************************************
		//*  Advanced compression functions
		//***************************************/

		///*! ZSTD_createCDict_byReference() :
		// *  Create a digested dictionary for compression
		// *  Dictionary content is simply referenced, and therefore stays in dictBuffer.
		// *  It is important that dictBuffer outlives CDict, it must remain read accessible throughout the lifetime of CDict */
		//ZSTDLIB_API ZSTD_CDict* ZSTD_createCDict_byReference(void* dictBuffer, size_t dictSize, int compressionLevel);

		///*! GetCParams() :
		//*   @return ZSTD_compressionParameters structure for a selected compression level and estimated srcSize.
		//*   `estimatedSrcSize` value is optional, select 0 if not known */
		//ZSTDLIB_API ZSTD_compressionParameters GetCParams(int compressionLevel, unsigned long long estimatedSrcSize, size_t dictSize);

		///*! GetParams() :
		//*   same as GetCParams(), but @return a full `ZSTD_parameters` object instead of sub-component `ZSTD_compressionParameters`.
		//*   All fields of `ZSTD_frameParameters` are set to default : contentSize=1, checksum=0, noDictID=0 */
		//ZSTDLIB_API ZSTD_parameters GetParams(int compressionLevel, unsigned long long estimatedSrcSize, size_t dictSize);

		///*! CheckCParams() :
		//*   Ensure param values remain within authorized range */
		//ZSTDLIB_API size_t CheckCParams(ZSTD_compressionParameters params);

		///*! AdjustCParams() :
		// *  optimize params for a given `srcSize` and `dictSize`.
		// *  both values are optional, select `0` if unknown. */
		//ZSTDLIB_API ZSTD_compressionParameters AdjustCParams(ZSTD_compressionParameters cPar, unsigned long long srcSize, size_t dictSize);

		///*! ZSTD_compress_advanced() :
		//*   Same as ZSTD_compress_usingDict(), with fine-tune control over each compression parameter */
		//ZSTDLIB_API size_t ZSTD_compress_advanced (ZSTD_CCtx* cctx,
		//                                  void* dst, size_t dstCapacity,
		//                            void* src, size_t srcSize,
		//                            void* dict,size_t dictSize,
		//                                  ZSTD_parameters params);

		///*! ZSTD_compress_usingCDict_advanced() :
		//*   Same as ZSTD_compress_usingCDict(), with fine-tune control over frame parameters */
		//ZSTDLIB_API size_t ZSTD_compress_usingCDict_advanced(ZSTD_CCtx* cctx,
		//                                  void* dst, size_t dstCapacity,
		//                            void* src, size_t srcSize,
		//                            readonly ZSTD_CDict* cdict, ZSTD_frameParameters fParams);


		///*--- Advanced decompression functions ---*/

		///*! IsFrame() :
		// *  Tells if the content of `buffer` starts with a valid Frame Identifier.
		// *  Note : Frame Identifier is 4 bytes. If `size < 4`, @return will always be 0.
		// *  Note 2 : Legacy Frame Identifiers are considered valid only if Legacy Support is enabled.
		// *  Note 3 : Skippable Frame Identifiers are considered valid. */
		//ZSTDLIB_API unsigned IsFrame(void* buffer, size_t size);

		///*! ZSTD_createDDict_byReference() :
		// *  Create a digested dictionary, ready to start decompression operation without startup delay.
		// *  Dictionary content is referenced, and therefore stays in dictBuffer.
		// *  It is important that dictBuffer outlives DDict,
		// *  it must remain read accessible throughout the lifetime of DDict */
		//ZSTDLIB_API ZSTD_DDict* ZSTD_createDDict_byReference(void* dictBuffer, size_t dictSize);


		///*! ZSTD_getDictID_fromDict() :
		// *  Provides the dictID stored within dictionary.
		// *  if @return == 0, the dictionary is not conformant with Zstandard specification.
		// *  It can still be loaded, but as a content-only dictionary. */
		//ZSTDLIB_API unsigned ZSTD_getDictID_fromDict(void* dict, size_t dictSize);

		///*! ZSTD_getDictID_fromDDict() :
		// *  Provides the dictID of the dictionary loaded into `ddict`.
		// *  If @return == 0, the dictionary is not conformant to Zstandard specification, or empty.
		// *  Non-conformant dictionaries can still be loaded, but as content-only dictionaries. */
		//ZSTDLIB_API unsigned ZSTD_getDictID_fromDDict(readonly ZSTD_DDict* ddict);

		///*! ZSTD_getDictID_fromFrame() :
		// *  Provides the dictID required to decompressed the frame stored within `src`.
		// *  If @return == 0, the dictID could not be decoded.
		// *  This could for one of the following reasons :
		// *  - The frame does not require a dictionary to be decoded (most common case).
		// *  - The frame was built with dictID intentionally removed. Whatever dictionary is necessary is a hidden information.
		// *    Note : this use case also happens when using a non-conformant dictionary.
		// *  - `srcSize` is too small, and as a result, the frame header could not be decoded (only possible if `srcSize < ZSTD_FRAMEHEADERSIZE_MAX`).
		// *  - This is not a Zstandard frame.
		// *  When identifying the exact failure cause, it's possible to use GetFrameHeader(), which will provide a more precise error code. */
		//ZSTDLIB_API unsigned ZSTD_getDictID_fromFrame(void* src, size_t srcSize);


		///********************************************************************
		//*  Advanced streaming functions
		//********************************************************************/

		///*=====   Advanced Streaming compression functions  =====*/
		//ZSTDLIB_API size_t ZSTD_initCStream_srcSize(ZSTD_CStream* zcs, int compressionLevel, unsigned long long pledgedSrcSize);   /**< pledgedSrcSize must be correct. If it is not known at init time, use ZSTD_CONTENTSIZE_UNKNOWN. Note that, for compatibility with older programs, "0" also disables frame content size field. It may be enabled in the future. */
		//ZSTDLIB_API size_t ZSTD_initCStream_usingDict(ZSTD_CStream* zcs, void* dict, size_t dictSize, int compressionLevel); /**< creates of an internal CDict (incompatible with static CCtx), except if dict == null or dictSize < 8, in which case no dict is used. Note: dict is loaded with ZSTD_dm_auto (treated as a full zstd dictionary if it begins with ZSTD_MAGIC_DICTIONARY, else as raw content) and ZSTD_dlm_byCopy.*/
		//ZSTDLIB_API size_t ZSTD_initCStream_advanced(ZSTD_CStream* zcs, void* dict, size_t dictSize,
		//                                             ZSTD_parameters params, unsigned long long pledgedSrcSize);  /**< pledgedSrcSize must be correct. If srcSize is not known at init time, use value ZSTD_CONTENTSIZE_UNKNOWN. dict is loaded with ZSTD_dm_auto and ZSTD_dlm_byCopy. */
		//ZSTDLIB_API size_t ZSTD_initCStream_usingCDict(ZSTD_CStream* zcs, readonly ZSTD_CDict* cdict);  /**< note : cdict will just be referenced, and must outlive compression session */
		//ZSTDLIB_API size_t ZSTD_initCStream_usingCDict_advanced(ZSTD_CStream* zcs, readonly ZSTD_CDict* cdict, ZSTD_frameParameters fParams, unsigned long long pledgedSrcSize);  /**< same as ZSTD_initCStream_usingCDict(), with control over frame parameters. pledgedSrcSize must be correct. If srcSize is not known at init time, use value ZSTD_CONTENTSIZE_UNKNOWN. */

		///*! ResetCStream() :
		// *  start a new compression job, using same parameters from previous job.
		// *  This is typically useful to skip dictionary loading stage, since it will re-use it in-place..
		// *  Note that zcs must be init at least once before using ResetCStream().
		// *  If pledgedSrcSize is not known at reset time, use macro ZSTD_CONTENTSIZE_UNKNOWN.
		// *  If pledgedSrcSize > 0, its value must be correct, as it will be written in header, and controlled at the end.
		// *  For the time being, pledgedSrcSize==0 is interpreted as "srcSize unknown" for compatibility with older programs,
		// *  but it will change to mean "empty" in future version, so use macro ZSTD_CONTENTSIZE_UNKNOWN instead.
		// * @return : 0, or an error code (which can be tested using IsError()) */
		//ZSTDLIB_API size_t ResetCStream(ZSTD_CStream* zcs, unsigned long long pledgedSrcSize);


		//typedef struct {
		//    unsigned long long ingested;
		//    unsigned long long consumed;
		//    unsigned long long produced;
		//} ZSTD_frameProgression;

		///* GetFrameProgression():
		// * tells how much data has been ingested (read from input)
		// * consumed (input actually compressed) and produced (output) for current frame.
		// * Therefore, (ingested - consumed) is amount of input data buffered internally, not yet compressed.
		// * Can report progression inside worker threads (multi-threading and non-blocking mode).
		// */
		//ZSTD_frameProgression GetFrameProgression(readonly ZSTD_CCtx* cctx);



		///*=====   Advanced Streaming decompression functions  =====*/
		//typedef enum { DStream_p_maxWindowSize } ZSTD_DStreamParameter_e;
		//ZSTDLIB_API size_t SetDStreamParameter(ZSTD_DStream* zds, ZSTD_DStreamParameter_e paramType, unsigned paramValue);   /* obsolete : this API will be removed in a future version */
		//ZSTDLIB_API size_t ZSTD_initDStream_usingDict(ZSTD_DStream* zds, void* dict, size_t dictSize); /**< note: no dictionary will be used if dict == null or dictSize < 8 */
		//ZSTDLIB_API size_t ZSTD_initDStream_usingDDict(ZSTD_DStream* zds, readonly ZSTD_DDict* ddict);  /**< note : ddict is referenced, it must outlive decompression session */
		//ZSTDLIB_API size_t ResetDStream(ZSTD_DStream* zds);  /**< re-use decompression parameters from previous init; saves dictionary loading */


		///*********************************************************************
		//*  Buffer-less and synchronous inner streaming functions
		//*
		//*  This is an advanced API, giving full control over buffer management, for users which need direct control over memory.
		//*  But it's also a complex one, with several restrictions, documented below.
		//*  Prefer normal streaming API for an easier experience.
		//********************************************************************* */

		///**
		//  Buffer-less streaming compression (synchronous mode)

		//  A ZSTD_CCtx object is required to track streaming operations.
		//  Use CreateCCtx() / FreeCCtx() to manage resource.
		//  ZSTD_CCtx object can be re-used multiple times within successive compression operations.

		//  Start by initializing a context.
		//  Use CompressBegin(), or ZSTD_compressBegin_usingDict() for dictionary compression,
		//  or ZSTD_compressBegin_advanced(), for finer parameter control.
		//  It's also possible to duplicate a reference context which has already been initialized, using CopyCCtx()

		//  Then, consume your input using CompressContinue().
		//  There are some important considerations to keep in mind when using this advanced function :
		//  - CompressContinue() has no internal buffer. It uses externally provided buffers only.
		//  - Interface is synchronous : input is consumed entirely and produces 1+ compressed blocks.
		//  - Caller must ensure there is enough space in `dst` to store compressed data under worst case scenario.
		//    Worst case evaluation is provided by CompressBound().
		//    CompressContinue() doesn't guarantee recover after a failed compression.
		//  - CompressContinue() presumes prior input ***is still accessible and unmodified*** (up to maximum distance size, see WindowLog).
		//    It remembers all previous contiguous blocks, plus one separated memory segment (which can itself consists of multiple contiguous blocks)
		//  - CompressContinue() detects that prior input has been overwritten when `src` buffer overlaps.
		//    In which case, it will "discard" the relevant memory section from its history.

		//  Finish a frame with CompressEnd(), which will write the last block(s) and optional checksum.
		//  It's possible to use srcSize==0, in which case, it will write a final empty block to end the frame.
		//  Without last block mark, frames are considered unfinished (hence corrupted) by compliant decoders.

		//  `ZSTD_CCtx` object can be re-used (CompressBegin()) to compress again.
		//*/

		///*=====   Buffer-less streaming compression functions  =====*/
		//ZSTDLIB_API size_t CompressBegin(ZSTD_CCtx* cctx, int compressionLevel);
		//ZSTDLIB_API size_t ZSTD_compressBegin_usingDict(ZSTD_CCtx* cctx, void* dict, size_t dictSize, int compressionLevel);
		//ZSTDLIB_API size_t ZSTD_compressBegin_advanced(ZSTD_CCtx* cctx, void* dict, size_t dictSize, ZSTD_parameters params, unsigned long long pledgedSrcSize); /**< pledgedSrcSize : If srcSize is not known at init time, use ZSTD_CONTENTSIZE_UNKNOWN */
		//ZSTDLIB_API size_t ZSTD_compressBegin_usingCDict(ZSTD_CCtx* cctx, readonly ZSTD_CDict* cdict); /**< note: fails if cdict==null */
		//ZSTDLIB_API size_t ZSTD_compressBegin_usingCDict_advanced(ZSTD_CCtx* readonly cctx, readonly ZSTD_CDict* readonly cdict, ZSTD_frameParameters readonly fParams, unsigned long long readonly pledgedSrcSize);   /* compression parameters are already set within cdict. pledgedSrcSize must be correct. If srcSize is not known, use macro ZSTD_CONTENTSIZE_UNKNOWN */
		//ZSTDLIB_API size_t CopyCCtx(ZSTD_CCtx* cctx, readonly ZSTD_CCtx* preparedCCtx, unsigned long long pledgedSrcSize); /**<  note: if pledgedSrcSize is not known, use ZSTD_CONTENTSIZE_UNKNOWN */

		//ZSTDLIB_API size_t CompressContinue(ZSTD_CCtx* cctx, void* dst, size_t dstCapacity, void* src, size_t srcSize);
		//ZSTDLIB_API size_t CompressEnd(ZSTD_CCtx* cctx, void* dst, size_t dstCapacity, void* src, size_t srcSize);


		///*-
		//  Buffer-less streaming decompression (synchronous mode)

		//  A ZSTD_DCtx object is required to track streaming operations.
		//  Use CreateDCtx() / FreeDCtx() to manage it.
		//  A ZSTD_DCtx object can be re-used multiple times.

		//  First typical operation is to retrieve frame parameters, using GetFrameHeader().
		//  Frame header is extracted from the beginning of compressed frame, so providing only the frame's beginning is enough.
		//  Data fragment must be large enough to ensure successful decoding.
		// `ZSTD_frameHeaderSize_max` bytes is guaranteed to always be large enough.
		//  @result : 0 : successful decoding, the `FrameHeader` structure is correctly filled.
		//           >0 : `srcSize` is too small, please provide at least @result bytes on next attempt.
		//           errorCode, which can be tested using IsError().

		//  It fills a FrameHeader structure with important information to correctly decode the frame,
		//  such as the dictionary ID, content size, or maximum back-reference distance (`windowSize`).
		//  Note that these values could be wrong, either because of data corruption, or because a 3rd party deliberately spoofs false information.
		//  As a consequence, check that values remain within valid application range.
		//  For example, do not allocate memory blindly, check that `windowSize` is within expectation.
		//  Each application can set its own limits, depending on local restrictions.
		//  For extended interoperability, it is recommended to support `windowSize` of at least 8 MB.

		//  DecompressContinue() needs previous data blocks during decompression, up to `windowSize` bytes.
		//  DecompressContinue() is very sensitive to contiguity,
		//  if 2 blocks don't follow each other, make sure that either the compressor breaks contiguity at the same place,
		//  or that previous contiguous segment is large enough to properly handle maximum back-reference distance.
		//  There are multiple ways to guarantee this condition.

		//  The most memory efficient way is to use a round buffer of sufficient size.
		//  Sufficient size is determined by invoking ZSTD_decodingBufferSize_min(),
		//  which can @return an error code if required value is too large for current system (in 32-bits mode).
		//  In a round buffer methodology, DecompressContinue() decompresses each block next to previous one,
		//  up to the moment there is not enough room left in the buffer to guarantee decoding another full block,
		//  which maximum size is provided in `FrameHeader` structure, field `blockSizeMax`.
		//  At which point, decoding can resume from the beginning of the buffer.
		//  Note that already decoded data stored in the buffer should be flushed before being overwritten.

		//  There are alternatives possible, for example using two or more buffers of size `windowSize` each, though they consume more memory.

		//  Finally, if you control the compression process, you can also ignore all buffer size rules,
		//  as long as the encoder and decoder progress in "lock-step",
		//  aka use exactly the same buffer sizes, break contiguity at the same place, etc.

		//  Once buffers are setup, start decompression, with DecompressBegin().
		//  If decompression requires a dictionary, use ZSTD_decompressBegin_usingDict() or ZSTD_decompressBegin_usingDDict().

		//  Then use NextSrcSizeToDecompress() and DecompressContinue() alternatively.
		//  NextSrcSizeToDecompress() tells how many bytes to provide as 'srcSize' to DecompressContinue().
		//  DecompressContinue() requires this _exact_ amount of bytes, or it will fail.

		// @result of DecompressContinue() is the number of bytes regenerated within 'dst' (necessarily <= dstCapacity).
		//  It can be zero : it just means DecompressContinue() has decoded some metadata item.
		//  It can also be an error code, which can be tested with IsError().

		//  A frame is fully decoded when NextSrcSizeToDecompress() returns zero.
		//  Context can then be reset to start a new decompression.

		//  Note : it's possible to know if next input to present is a header or a block, using NextInputType().
		//  This information is not required to properly decode a frame.

		//  == Special case : skippable frames ==

		//  Skippable frames allow integration of user-defined data into a flow of concatenated frames.
		//  Skippable frames will be ignored (skipped) by decompressor.
		//  The format of skippable frames is as follows :
		//  a) Skippable frame ID - 4 Bytes, Little endian format, any value from 0x184D2A50 to 0x184D2A5F
		//  b) Frame Size - 4 Bytes, Little endian format, unsigned 32-bits
		//  c) Frame Content - any content (User Data) of length equal to Frame Size
		//  For skippable frames GetFrameHeader() returns zfhPtr->frameType==ZSTD_skippableFrame.
		//  For skippable frames DecompressContinue() always returns 0 : it only skips the content.
		//*/

		///*=====   Buffer-less streaming decompression functions  =====*/
		///** GetFrameHeader() :
		// *  decode Frame Header, or requires larger `srcSize`.
		// * @return : 0, `zfhPtr` is correctly filled,
		// *          >0, `srcSize` is too small, value is wanted `srcSize` amount,
		// *           or an error code, which can be tested using IsError() */
		//ZSTDLIB_API size_t GetFrameHeader(FrameHeader* zfhPtr, void* src, size_t srcSize);   /**< doesn't consume input */
		//ZSTDLIB_API size_t ZSTD_decodingBufferSize_min(unsigned long long windowSize, unsigned long long frameContentSize);  /**< when frame content size is not known, pass in frameContentSize == ZSTD_CONTENTSIZE_UNKNOWN */

		//ZSTDLIB_API size_t DecompressBegin(ZSTD_DCtx dctx);
		//ZSTDLIB_API size_t ZSTD_decompressBegin_usingDict(ZSTD_DCtx dctx, void* dict, size_t dictSize);
		//ZSTDLIB_API size_t ZSTD_decompressBegin_usingDDict(ZSTD_DCtx dctx, readonly ZSTD_DDict* ddict);

		//ZSTDLIB_API size_t NextSrcSizeToDecompress(ZSTD_DCtx dctx);
		//ZSTDLIB_API size_t DecompressContinue(ZSTD_DCtx dctx, void* dst, size_t dstCapacity, void* src, size_t srcSize);

		///* misc */
		//ZSTDLIB_API void   CopyDCtx(ZSTD_DCtx dctx, readonly ZSTD_DCtx preparedDCtx);
		//typedef enum { ZSTDnit_frameHeader, ZSTDnit_blockHeader, ZSTDnit_block, ZSTDnit_lastBlock, ZSTDnit_checksum, ZSTDnit_skippableFrame } ZSTD_nextInputType_e;
		//ZSTDLIB_API ZSTD_nextInputType_e NextInputType(ZSTD_DCtx dctx);



		///* ============================================ */
		///**       New advanced API (experimental)       */
		///* ============================================ */

		///* notes on API design :
		// *   In this proposal, parameters are pushed one by one into an existing context,
		// *   and then applied on all subsequent compression jobs.
		// *   When no parameter is ever provided, CCtx is created with compression level ZSTD_CLEVEL_DEFAULT.
		// *
		// *   This API is intended to replace all others advanced / experimental API entry points.
		// *   But it stands a reasonable chance to become "stable", after a reasonable testing period.
		// */

		///* note on naming convention :
		// *   Initially, the API favored names like SetCCtxParameter() .
		// *   In this proposal, convention is changed towards ZSTD_CCtx_setParameter() .
		// *   The main driver is that it identifies more clearly the target object type.
		// *   It feels clearer when considering multiple targets :
		// *   ZSTD_CDict_setParameter() (rather than SetCDictParameter())
		// *   ZSTD_CCtxParams_setParameter()  (rather than SetCCtxParamsParameter() )
		// *   etc...
		// */

		///* note on enum design :
		// * All enum will be pinned to explicit values before reaching "stable API" status */

		//typedef enum {
		//    /* compression format */
		//    ZSTD_p_format = 10,      /* See ZSTD_format_e enum definition.
		//                              * Cast selected format as unsigned for ZSTD_CCtx_setParameter() compatibility. */

		//    /* compression parameters */
		//    ZSTD_p_compressionLevel=100, /* Update all compression parameters according to pre-defined cLevel table
		//                              * Default level is ZSTD_CLEVEL_DEFAULT==3.
		//                              * Special: value 0 means "do not change cLevel".
		//                              * Note 1 : it's possible to pass a negative compression level by casting it to unsigned type.
		//                              * Note 2 : setting a level sets all default values of other compression parameters.
		//                              * Note 3 : setting compressionLevel automatically updates ZSTD_p_compressLiterals. */
		//    ZSTD_p_windowLog,        /* Maximum allowed back-reference distance, expressed as power of 2.
		//                              * Must be clamped between ZSTD_WINDOWLOG_MIN and ZSTD_WINDOWLOG_MAX.
		//                              * Special: value 0 means "use default windowLog".
		//                              * Note: Using a window size greater than ZSTD_MAXWINDOWSIZE_DEFAULT (default: 2^27)
		//                              *       requires explicitly allowing such window size during decompression stage. */
		//    ZSTD_p_hashLog,          /* Size of the probe table, as a power of 2.
		//                              * Resulting table size is (1 << (hashLog+2)).
		//                              * Must be clamped between ZSTD_HASHLOG_MIN and ZSTD_HASHLOG_MAX.
		//                              * Larger tables improve compression ratio of strategies <= dFast,
		//                              * and improve speed of strategies > dFast.
		//                              * Special: value 0 means "use default hashLog". */
		//    ZSTD_p_chainLog,         /* Size of the full-search table, as a power of 2.
		//                              * Resulting table size is (1 << (chainLog+2)).
		//                              * Larger tables result in better and slower compression.
		//                              * This parameter is useless when using "fast" strategy.
		//                              * Special: value 0 means "use default chainLog". */
		//    ZSTD_p_searchLog,        /* Number of search attempts, as a power of 2.
		//                              * More attempts result in better and slower compression.
		//                              * This parameter is useless when using "fast" and "dFast" strategies.
		//                              * Special: value 0 means "use default searchLog". */
		//    ZSTD_p_minMatch,         /* Minimum size of searched matches (note : repCode matches can be smaller).
		//                              * Larger values make faster compression and decompression, but decrease ratio.
		//                              * Must be clamped between ZSTD_SEARCHLENGTH_MIN and ZSTD_SEARCHLENGTH_MAX.
		//                              * Note that currently, for all strategies < btopt, effective minimum is 4.
		//                              *                    , for all strategies > fast, effective maximum is 6.
		//                              * Special: value 0 means "use default minMatchLength". */
		//    ZSTD_p_targetLength,     /* Impact of this field depends on strategy.
		//                              * For strategies btopt & btultra:
		//                              *     Length of Match considered "good enough" to stop search.
		//                              *     Larger values make compression stronger, and slower.
		//                              * For strategy fast:
		//                              *     Distance between match sampling.
		//                              *     Larger values make compression faster, and weaker.
		//                              * Special: value 0 means "use default targetLength". */
		//    ZSTD_p_compressionStrategy, /* See ZSTD_strategy enum definition.
		//                              * Cast selected strategy as unsigned for ZSTD_CCtx_setParameter() compatibility.
		//                              * The higher the value of selected strategy, the more complex it is,
		//                              * resulting in stronger and slower compression.
		//                              * Special: value 0 means "use default strategy". */

		//    ZSTD_p_enableLongDistanceMatching=160, /* Enable long distance matching.
		//                                         * This parameter is designed to improve compression ratio
		//                                         * for large inputs, by finding large matches at long distance.
		//                                         * It increases memory usage and window size.
		//                                         * Note: enabling this parameter increases ZSTD_p_windowLog to 128 MB
		//                                         * except when expressly set to a different value. */
		//    ZSTD_p_ldmHashLog,       /* Size of the table for long distance matching, as a power of 2.
		//                              * Larger values increase memory usage and compression ratio,
		//                              * but decrease compression speed.
		//                              * Must be clamped between ZSTD_HASHLOG_MIN and ZSTD_HASHLOG_MAX
		//                              * default: windowlog - 7.
		//                              * Special: value 0 means "automatically determine hashlog". */
		//    ZSTD_p_ldmMinMatch,      /* Minimum match size for long distance matcher.
		//                              * Larger/too small values usually decrease compression ratio.
		//                              * Must be clamped between ZSTD_LDM_MINMATCH_MIN and ZSTD_LDM_MINMATCH_MAX.
		//                              * Special: value 0 means "use default value" (default: 64). */
		//    ZSTD_p_ldmBucketSizeLog, /* Log size of each bucket in the LDM hash table for collision resolution.
		//                              * Larger values improve collision resolution but decrease compression speed.
		//                              * The maximum value is ZSTD_LDM_BUCKETSIZELOG_MAX .
		//                              * Special: value 0 means "use default value" (default: 3). */
		//    ZSTD_p_ldmHashEveryLog,  /* Frequency of inserting/looking up entries in the LDM hash table.
		//                              * Must be clamped between 0 and (ZSTD_WINDOWLOG_MAX - ZSTD_HASHLOG_MIN).
		//                              * Default is MAX(0, (windowLog - ldmHashLog)), optimizing hash table usage.
		//                              * Larger values improve compression speed.
		//                              * Deviating far from default value will likely result in a compression ratio decrease.
		//                              * Special: value 0 means "automatically determine hashEveryLog". */

		//    /* frame parameters */
		//    ZSTD_p_contentSizeFlag=200, /* Content size will be written into frame header _whenever known_ (default:1)
		//                              * Content size must be known at the beginning of compression,
		//                              * it is provided using ZSTD_CCtx_setPledgedSrcSize() */
		//    ZSTD_p_checksumFlag,     /* A 32-bits checksum of content is written at end of frame (default:0) */
		//    ZSTD_p_dictIDFlag,       /* When applicable, dictionary's ID is written into frame header (default:1) */

		//    /* multi-threading parameters */
		//    /* These parameters are only useful if multi-threading is enabled (ZSTD_MULTITHREAD).
		//     * They return an error otherwise. */
		//    ZSTD_p_nbWorkers=400,    /* Select how many threads will be spawned to compress in parallel.
		//                              * When nbWorkers >= 1, triggers asynchronous mode :
		//                              * ZSTD_compress_generic() consumes some input, flush some output if possible, and immediately gives back control to caller,
		//                              * while compression work is performed in parallel, within worker threads.
		//                              * (note : a strong exception to this rule is when first invocation sets ZSTD_e_end : it becomes a blocking call).
		//                              * More workers improve speed, but also increase memory usage.
		//                              * Default value is `0`, aka "single-threaded mode" : no worker is spawned, compression is performed inside Caller's thread, all invocations are blocking */
		//    ZSTD_p_jobSize,          /* Size of a compression job. This value is enforced only in non-blocking mode.
		//                              * Each compression job is completed in parallel, so this value indirectly controls the nb of active threads.
		//                              * 0 means default, which is dynamically determined based on compression parameters.
		//                              * Job size must be a minimum of overlapSize, or 1 MB, whichever is largest.
		//                              * The minimum size is automatically and transparently enforced */
		//    ZSTD_p_overlapSizeLog,   /* Size of previous input reloaded at the beginning of each job.
		//                              * 0 => no overlap, 6(default) => use 1/8th of windowSize, >=9 => use full windowSize */

		//    /* =================================================================== */
		//    /* experimental parameters - no stability guaranteed                   */
		//    /* =================================================================== */

		//    ZSTD_p_compressLiterals=1000, /* control huffman compression of literals (enabled) by default.
		//                              * disabling it improves speed and decreases compression ratio by a large amount.
		//                              * note : this setting is automatically updated when changing compression level.
		//                              *        positive compression levels set ZSTD_p_compressLiterals to 1.
		//                              *        negative compression levels set ZSTD_p_compressLiterals to 0. */

		//    ZSTD_p_forceMaxWindow=1100, /* Force back-reference distances to remain < windowSize,
		//                              * even when referencing into Dictionary content (default:0) */

		//} ZSTD_cParameter;


		///*! ZSTD_CCtx_setParameter() :
		// *  Set one compression parameter, selected by enum ZSTD_cParameter.
		// *  Setting a parameter is generally only possible during frame initialization (before starting compression),
		// *  except for a few exceptions which can be updated during compression: compressionLevel, hashLog, chainLog, searchLog, minMatch, targetLength and strategy.
		// *  Note : when `value` is an enum, cast it to unsigned for proper type checking.
		// *  @result : informational value (typically, value being set clamped correctly),
		// *            or an error code (which can be tested with IsError()). */
		//ZSTDLIB_API size_t ZSTD_CCtx_setParameter(ZSTD_CCtx* cctx, ZSTD_cParameter param, unsigned value);

		///*! ZSTD_CCtx_setPledgedSrcSize() :
		// *  Total input data size to be compressed as a single frame.
		// *  This value will be controlled at the end, and result in error if not respected.
		// * @result : 0, or an error code (which can be tested with IsError()).
		// *  Note 1 : 0 means zero, empty.
		// *           In order to mean "unknown content size", pass constant ZSTD_CONTENTSIZE_UNKNOWN.
		// *           ZSTD_CONTENTSIZE_UNKNOWN is default value for any new compression job.
		// *  Note 2 : If all data is provided and consumed in a single round,
		// *           this value is overriden by srcSize instead. */
		//ZSTDLIB_API size_t ZSTD_CCtx_setPledgedSrcSize(ZSTD_CCtx* cctx, unsigned long long pledgedSrcSize);

		///*! ZSTD_CCtx_loadDictionary() :
		// *  Create an internal CDict from `dict` buffer.
		// *  Decompression will have to use same dictionary.
		// * @result : 0, or an error code (which can be tested with IsError()).
		// *  Special: Adding a null (or 0-size) dictionary invalidates previous dictionary,
		// *           meaning "return to no-dictionary mode".
		// *  Note 1 : Dictionary will be used for all future compression jobs.
		// *           To return to "no-dictionary" situation, load a null dictionary
		// *  Note 2 : Loading a dictionary involves building tables, which are dependent on compression parameters.
		// *           For this reason, compression parameters cannot be changed anymore after loading a dictionary.
		// *           It's also a CPU consuming operation, with non-negligible impact on latency.
		// *  Note 3 :`dict` content will be copied internally.
		// *           Use ZSTD_CCtx_loadDictionary_byReference() to reference dictionary content instead.
		// *           In such a case, dictionary buffer must outlive its users.
		// *  Note 4 : Use ZSTD_CCtx_loadDictionary_advanced()
		// *           to precisely select how dictionary content must be interpreted. */
		//ZSTDLIB_API size_t ZSTD_CCtx_loadDictionary(ZSTD_CCtx* cctx, void* dict, size_t dictSize);
		//ZSTDLIB_API size_t ZSTD_CCtx_loadDictionary_byReference(ZSTD_CCtx* cctx, void* dict, size_t dictSize);
		//ZSTDLIB_API size_t ZSTD_CCtx_loadDictionary_advanced(ZSTD_CCtx* cctx, void* dict, size_t dictSize, ZSTD_dictLoadMethod_e dictLoadMethod, ZSTD_dictContentType_e dictContentType);


		///*! ZSTD_CCtx_refCDict() :
		// *  Reference a prepared dictionary, to be used for all next compression jobs.
		// *  Note that compression parameters are enforced from within CDict,
		// *  and supercede any compression parameter previously set within CCtx.
		// *  The dictionary will remain valid for future compression jobs using same CCtx.
		// * @result : 0, or an error code (which can be tested with IsError()).
		// *  Special : adding a null CDict means "return to no-dictionary mode".
		// *  Note 1 : Currently, only one dictionary can be managed.
		// *           Adding a new dictionary effectively "discards" any previous one.
		// *  Note 2 : CDict is just referenced, its lifetime must outlive CCtx. */
		//ZSTDLIB_API size_t ZSTD_CCtx_refCDict(ZSTD_CCtx* cctx, readonly ZSTD_CDict* cdict);

		///*! ZSTD_CCtx_refPrefix() :
		// *  Reference a prefix (single-usage dictionary) for next compression job.
		// *  Decompression need same prefix to properly regenerate data.
		// *  Prefix is **only used once**. Tables are discarded at end of compression job.
		// *  Subsequent compression jobs will be done without prefix (if none is explicitly referenced).
		// *  If there is a need to use same prefix multiple times, consider embedding it into a ZSTD_CDict instead.
		// * @result : 0, or an error code (which can be tested with IsError()).
		// *  Special: Adding any prefix (including null) invalidates any previous prefix or dictionary
		// *  Note 1 : Prefix buffer is referenced. It must outlive compression job.
		// *  Note 2 : Referencing a prefix involves building tables, which are dependent on compression parameters.
		// *           It's a CPU consuming operation, with non-negligible impact on latency.
		// *  Note 3 : By default, the prefix is treated as raw content (ZSTD_dm_rawContent).
		// *           Use ZSTD_CCtx_refPrefix_advanced() to alter dictMode. */
		//ZSTDLIB_API size_t ZSTD_CCtx_refPrefix(ZSTD_CCtx* cctx, void* prefix, size_t prefixSize);
		//ZSTDLIB_API size_t ZSTD_CCtx_refPrefix_advanced(ZSTD_CCtx* cctx, void* prefix, size_t prefixSize, ZSTD_dictContentType_e dictContentType);

		///*! ZSTD_CCtx_reset() :
		// *  Return a CCtx to clean state.
		// *  Useful after an error, or to interrupt an ongoing compression job and start a new one.
		// *  Any internal data not yet flushed is cancelled.
		// *  Dictionary (if any) is dropped.
		// *  All parameters are back to default values.
		// *  It's possible to modify compression parameters after a reset.
		// */
		//ZSTDLIB_API void ZSTD_CCtx_reset(ZSTD_CCtx* cctx);



		//typedef enum {
		//    ZSTD_e_continue=0, /* collect more data, encoder decides when to output compressed result, for optimal conditions */
		//    ZSTD_e_flush,      /* flush any data provided so far - frame will continue, future data can still reference previous data for better compression */
		//    ZSTD_e_end         /* flush any remaining data and close current frame. Any additional data starts a new frame. */
		//} ZSTD_EndDirective;

		///*! ZSTD_compress_generic() :
		// *  Behave about the same as ZSTD_compressStream. To note :
		// *  - Compression parameters are pushed into CCtx before starting compression, using ZSTD_CCtx_setParameter()
		// *  - Compression parameters cannot be changed once compression is started.
		// *  - outpot->pos must be <= dstCapacity, input->pos must be <= srcSize
		// *  - outpot->pos and input->pos will be updated. They are guaranteed to remain below their respective limit.
		// *  - In single-thread mode (default), function is blocking : it completed its job before returning to caller.
		// *  - In multi-thread mode, function is non-blocking : it just acquires a copy of input, and distribute job to internal worker threads,
		// *                                                     and then immediately returns, just indicating that there is some data remaining to be flushed.
		// *                                                     The function nonetheless guarantees forward progress : it will return only after it reads or write at least 1+ byte.
		// *  - Exception : in multi-threading mode, if the first call requests a ZSTD_e_end directive, it is blocking : it will complete compression before giving back control to caller.
		// *  - @return provides a minimum amount of data remaining to be flushed from internal buffers
		// *            or an error code, which can be tested using IsError().
		// *            if @return != 0, flush is not fully completed, there is still some data left within internal buffers.
		// *            This is useful for ZSTD_e_flush, since in this case more flushes are necessary to empty all buffers.
		// *            For ZSTD_e_end, @return == 0 when internal buffers are fully flushed and frame is completed.
		// *  - after a ZSTD_e_end directive, if internal buffer is not fully flushed (@return != 0),
		// *            only ZSTD_e_end or ZSTD_e_flush operations are allowed.
		// *            Before starting a new compression job, or changing compression parameters,
		// *            it is required to fully flush internal buffers.
		// */
		//ZSTDLIB_API size_t ZSTD_compress_generic (ZSTD_CCtx* cctx,
		//                                          ZSTD_outBuffer* output,
		//                                          ZSTD_inBuffer* input,
		//                                          ZSTD_EndDirective endOp);


		///*! ZSTD_compress_generic_simpleArgs() :
		// *  Same as ZSTD_compress_generic(),
		// *  but using only integral types as arguments.
		// *  Argument list is larger than ZSTD_{in,out}Buffer,
		// *  but can be helpful for binders from dynamic languages
		// *  which have troubles handling structures containing memory pointers.
		// */
		//ZSTDLIB_API size_t ZSTD_compress_generic_simpleArgs (
		//                            ZSTD_CCtx* cctx,
		//                            void* dst, size_t dstCapacity, size_t* dstPos,
		//                      void* src, size_t srcSize, size_t* srcPos,
		//                            ZSTD_EndDirective endOp);


		///*! ZSTD_CCtx_params :
		// *  Quick howto :
		// *  - CreateCCtxParams() : Create a ZSTD_CCtx_params structure
		// *  - ZSTD_CCtxParam_setParameter() : Push parameters one by one into
		// *                                    an existing ZSTD_CCtx_params structure.
		// *                                    This is similar to
		// *                                    ZSTD_CCtx_setParameter().
		// *  - ZSTD_CCtx_setParametersUsingCCtxParams() : Apply parameters to
		// *                                    an existing CCtx.
		// *                                    These parameters will be applied to
		// *                                    all subsequent compression jobs.
		// *  - ZSTD_compress_generic() : Do compression using the CCtx.
		// *  - FreeCCtxParams() : Free the memory.
		// *
		// *  This can be used with ZSTD_estimateCCtxSize_advanced_usingCCtxParams()
		// *  for static allocation for single-threaded compression.
		// */
		//ZSTDLIB_API ZSTD_CCtx_params* CreateCCtxParams(void);
		//ZSTDLIB_API size_t FreeCCtxParams(ZSTD_CCtx_params* params);


		///*! ZSTD_CCtxParams_reset() :
		// *  Reset params to default values.
		// */
		//ZSTDLIB_API size_t ZSTD_CCtxParams_reset(ZSTD_CCtx_params* params);

		///*! ZSTD_CCtxParams_init() :
		// *  Initializes the compression parameters of cctxParams according to
		// *  compression level. All other parameters are reset to their default values.
		// */
		//ZSTDLIB_API size_t ZSTD_CCtxParams_init(ZSTD_CCtx_params* cctxParams, int compressionLevel);

		///*! ZSTD_CCtxParams_init_advanced() :
		// *  Initializes the compression and frame parameters of cctxParams according to
		// *  params. All other parameters are reset to their default values.
		// */
		//ZSTDLIB_API size_t ZSTD_CCtxParams_init_advanced(ZSTD_CCtx_params* cctxParams, ZSTD_parameters params);


		///*! ZSTD_CCtxParam_setParameter() :
		// *  Similar to ZSTD_CCtx_setParameter.
		// *  Set one compression parameter, selected by enum ZSTD_cParameter.
		// *  Parameters must be applied to a ZSTD_CCtx using ZSTD_CCtx_setParametersUsingCCtxParams().
		// *  Note : when `value` is an enum, cast it to unsigned for proper type checking.
		// * @result : 0, or an error code (which can be tested with IsError()).
		// */
		//ZSTDLIB_API size_t ZSTD_CCtxParam_setParameter(ZSTD_CCtx_params* params, ZSTD_cParameter param, unsigned value);

		///*! ZSTD_CCtx_setParametersUsingCCtxParams() :
		// *  Apply a set of ZSTD_CCtx_params to the compression context.
		// *  This can be done even after compression is started,
		// *    if nbWorkers==0, this will have no impact until a new compression is started.
		// *    if nbWorkers>=1, new parameters will be picked up at next job,
		// *       with a few restrictions (windowLog, pledgedSrcSize, nbWorkers, jobSize, and overlapLog are not updated).
		// */
		//ZSTDLIB_API size_t ZSTD_CCtx_setParametersUsingCCtxParams(
		//        ZSTD_CCtx* cctx, readonly ZSTD_CCtx_params* params);


		///* ==================================== */
		///*===   Advanced decompression API   ===*/
		///* ==================================== */

		///* The following API works the same way as the advanced compression API :
		// * a context is created, parameters are pushed into it one by one,
		// * then the context can be used to decompress data using an interface similar to the straming API.
		// */

		///*! ZSTD_DCtx_loadDictionary() :
		// *  Create an internal DDict from dict buffer,
		// *  to be used to decompress next frames.
		// * @result : 0, or an error code (which can be tested with IsError()).
		// *  Special : Adding a null (or 0-size) dictionary invalidates any previous dictionary,
		// *            meaning "return to no-dictionary mode".
		// *  Note 1 : `dict` content will be copied internally.
		// *            Use ZSTD_DCtx_loadDictionary_byReference()
		// *            to reference dictionary content instead.
		// *            In which case, the dictionary buffer must outlive its users.
		// *  Note 2 : Loading a dictionary involves building tables,
		// *           which has a non-negligible impact on CPU usage and latency.
		// *  Note 3 : Use ZSTD_DCtx_loadDictionary_advanced() to select
		// *           how dictionary content will be interpreted and loaded.
		// */
		//ZSTDLIB_API size_t ZSTD_DCtx_loadDictionary(ZSTD_DCtx dctx, void* dict, size_t dictSize);
		//ZSTDLIB_API size_t ZSTD_DCtx_loadDictionary_byReference(ZSTD_DCtx dctx, void* dict, size_t dictSize);
		//ZSTDLIB_API size_t ZSTD_DCtx_loadDictionary_advanced(ZSTD_DCtx dctx, void* dict, size_t dictSize, ZSTD_dictLoadMethod_e dictLoadMethod, ZSTD_dictContentType_e dictContentType);


		///*! ZSTD_DCtx_refDDict() :
		// *  Reference a prepared dictionary, to be used to decompress next frames.
		// *  The dictionary remains active for decompression of future frames using same DCtx.
		// * @result : 0, or an error code (which can be tested with IsError()).
		// *  Note 1 : Currently, only one dictionary can be managed.
		// *           Referencing a new dictionary effectively "discards" any previous one.
		// *  Special : adding a null DDict means "return to no-dictionary mode".
		// *  Note 2 : DDict is just referenced, its lifetime must outlive its usage from DCtx.
		// */
		//ZSTDLIB_API size_t ZSTD_DCtx_refDDict(ZSTD_DCtx dctx, readonly ZSTD_DDict* ddict);


		///*! ZSTD_DCtx_refPrefix() :
		// *  Reference a prefix (single-usage dictionary) for next compression job.
		// *  Prefix is **only used once**. It must be explicitly referenced before each frame.
		// *  If there is a need to use same prefix multiple times, consider embedding it into a ZSTD_DDict instead.
		// * @result : 0, or an error code (which can be tested with IsError()).
		// *  Note 1 : Adding any prefix (including null) invalidates any previously set prefix or dictionary
		// *  Note 2 : Prefix buffer is referenced. It must outlive compression job.
		// *  Note 3 : By default, the prefix is treated as raw content (ZSTD_dm_rawContent).
		// *           Use ZSTD_CCtx_refPrefix_advanced() to alter dictMode.
		// *  Note 4 : Referencing a raw content prefix has almost no cpu nor memory cost.
		// */
		//ZSTDLIB_API size_t ZSTD_DCtx_refPrefix(ZSTD_DCtx dctx, void* prefix, size_t prefixSize);
		//ZSTDLIB_API size_t ZSTD_DCtx_refPrefix_advanced(ZSTD_DCtx dctx, void* prefix, size_t prefixSize, ZSTD_dictContentType_e dictContentType);


		///*! ZSTD_DCtx_setMaxWindowSize() :
		// *  Refuses allocating internal buffers for frames requiring a window size larger than provided limit.
		// *  This is useful to prevent a decoder context from reserving too much memory for itself (potential attack scenario).
		// *  This parameter is only useful in streaming mode, since no internal buffer is allocated in direct mode.
		// *  By default, a decompression context accepts all window sizes <= (1 << ZSTD_WINDOWLOG_MAX)
		// * @return : 0, or an error code (which can be tested using IsError()).
		// */
		//ZSTDLIB_API size_t ZSTD_DCtx_setMaxWindowSize(ZSTD_DCtx dctx, size_t maxWindowSize);


		///*! ZSTD_DCtx_setFormat() :
		// *  Instruct the decoder context about what kind of data to decode next.
		// *  This instruction is mandatory to decode data without a fully-formed header,
		// *  such ZSTD_f_zstd1_magicless for example.
		// * @return : 0, or an error code (which can be tested using IsError()).
		// */
		//ZSTDLIB_API size_t ZSTD_DCtx_setFormat(ZSTD_DCtx dctx, ZSTD_format_e format);


		///** ZSTD_getFrameHeader_advanced() :
		// *  same as GetFrameHeader(),
		// *  with added capability to select a format (like ZSTD_f_zstd1_magicless) */
		//ZSTDLIB_API size_t ZSTD_getFrameHeader_advanced(FrameHeader* zfhPtr,
		//                        void* src, size_t srcSize, ZSTD_format_e format);


		///*! ZSTD_decompress_generic() :
		// *  Behave the same as ZSTD_decompressStream.
		// *  Decompression parameters cannot be changed once decompression is started.
		// * @return : an error code, which can be tested using IsError()
		// *           if >0, a hint, nb of expected input bytes for next invocation.
		// *           `0` means : a frame has just been fully decoded and flushed.
		// */
		//ZSTDLIB_API size_t ZSTD_decompress_generic(ZSTD_DCtx dctx,
		//                                           ZSTD_outBuffer* output,
		//                                           ZSTD_inBuffer* input);


		///*! ZSTD_decompress_generic_simpleArgs() :
		// *  Same as ZSTD_decompress_generic(),
		// *  but using only integral types as arguments.
		// *  Argument list is larger than ZSTD_{in,out}Buffer,
		// *  but can be helpful for binders from dynamic languages
		// *  which have troubles handling structures containing memory pointers.
		// */
		//ZSTDLIB_API size_t ZSTD_decompress_generic_simpleArgs (
		//                            ZSTD_DCtx dctx,
		//                            void* dst, size_t dstCapacity, size_t* dstPos,
		//                      void* src, size_t srcSize, size_t* srcPos);


		///*! ZSTD_DCtx_reset() :
		// *  Return a DCtx to clean state.
		// *  If a decompression was ongoing, any internal data not yet flushed is cancelled.
		// *  All parameters are back to default values, including sticky ones.
		// *  Dictionary (if any) is dropped.
		// *  Parameters can be modified again after a reset.
		// */
		//ZSTDLIB_API void ZSTD_DCtx_reset(ZSTD_DCtx dctx);



		///* ============================ */
		///**       Block level API       */
		///* ============================ */

		///*!
		//    Block functions produce and decode raw zstd blocks, without frame metadata.
		//    Frame metadata cost is typically ~18 bytes, which can be non-negligible for very small blocks (< 100 bytes).
		//    User will have to take in charge required information to regenerate data, such as compressed and content sizes.

		//    A few rules to respect :
		//    - Compressing and decompressing require a context structure
		//      + Use CreateCCtx() and CreateDCtx()
		//    - It is necessary to init context before starting
		//      + compression : any ZSTD_compressBegin*() variant, including with dictionary
		//      + decompression : any ZSTD_decompressBegin*() variant, including with dictionary
		//      + copyCCtx() and copyDCtx() can be used too
		//    - Block size is limited, it must be <= GetBlockSize() <= ZSTD_BLOCKSIZE_MAX == 128 KB
		//      + If input is larger than a block size, it's necessary to split input data into multiple blocks
		//      + For inputs larger than a single block size, consider using the regular Compress() instead.
		//        Frame metadata is not that costly, and quickly becomes negligible as source size grows larger.
		//    - When a block is considered not compressible enough, CompressBlock() result will be zero.
		//      In which case, nothing is produced into `dst`.
		//      + User must test for such outcome and deal directly with uncompressed data
		//      + DecompressBlock() doesn't accept uncompressed data as input !!!
		//      + In case of multiple successive blocks, should some of them be uncompressed,
		//        decoder must be informed of their existence in order to follow proper history.
		//        Use InsertBlock() for such a case.
		//*/

		public const int ZSTD_BLOCKSIZELOG_MAX = 17;

		public const int ZSTD_BLOCKSIZE_MAX = (1 << ZSTD_BLOCKSIZELOG_MAX); /* define, for static allocation */

		///*=====   Raw zstd block functions  =====*/
		//ZSTDLIB_API size_t GetBlockSize(readonly ZSTD_CCtx* cctx);
		//ZSTDLIB_API size_t CompressBlock(ZSTD_CCtx* cctx, void* dst, size_t dstCapacity, void* src, size_t srcSize);
		//ZSTDLIB_API size_t DecompressBlock(ZSTD_DCtx dctx, void* dst, size_t dstCapacity, void* src, size_t srcSize);
		//ZSTDLIB_API size_t InsertBlock(ZSTD_DCtx dctx, void* blockStart, size_t blockSize);  /**< insert uncompressed block into `dctx` history. Useful for multi-blocks decompression. */


		//#endif   /* ZSTD_H_ZSTD_STATIC_LINKING_ONLY */

		//#if defined (__cplusplus)
		//}
		//#endif
	}
}
