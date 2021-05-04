/*
 * Copyright (c) 2016-present, Yann Collet, Facebook, Inc.
 * All rights reserved.
 *
 * This source code is licensed under both the BSD-style license (found in the
 * LICENSE file in the root directory of this source tree) and the GPLv2 (found
 * in the COPYING file in the root directory of this source tree).
 * You may select, at your option, one of the above-listed licenses.
 */
using BYTE = System.Byte;
using size_t = System.UInt32;
using U16 = System.UInt16;
using U32 = System.UInt32;
using U64 = System.UInt64;

namespace EPAM.Deltix.ZStd
{
	internal static unsafe class ZStdErrors
	{
		//#ifndef ZSTD_ERRORS_H_398273423
		//#define ZSTD_ERRORS_H_398273423

		//#if defined (__cplusplus)
		//extern "C" {
		//#endif

		///*===== dependency =====*/
		//#include <stddef.h>   /* size_t */


		///* =====   ZSTDERRORLIB_API : control library symbols visibility   ===== */
		//#ifndef ZSTDERRORLIB_VISIBILITY
		//#  if defined(__GNUC__) && (__GNUC__ >= 4)
		//#    define ZSTDERRORLIB_VISIBILITY __attribute__ ((visibility ("default")))
		//#  else
		//#    define ZSTDERRORLIB_VISIBILITY
		//#  endif
		//#endif
		//#if defined(ZSTD_DLL_EXPORT) && (ZSTD_DLL_EXPORT==1)
		//#  define ZSTDERRORLIB_API __declspec(dllexport) ZSTDERRORLIB_VISIBILITY
		//#elif defined(ZSTD_DLL_IMPORT) && (ZSTD_DLL_IMPORT==1)
		//#  define ZSTDERRORLIB_API __declspec(dllimport) ZSTDERRORLIB_VISIBILITY /* It isn't required but allows to generate better code, saving a function pointer load from the IAT and an indirect jump.*/
		//#else
		//#  define ZSTDERRORLIB_API ZSTDERRORLIB_VISIBILITY
		//#endif

		///*-*********************************************
		// *  Error codes list
		// *-*********************************************
		// *  Error codes _values_ are pinned down since v1.3.1 only.
		// *  Therefore, don't rely on values if you may link to any version < v1.3.1.
		// *
		// *  Only values < 100 are considered stable.
		// *
		// *  note 1 : this API shall be used with static linking only.
		// *           dynamic linking is not yet officially supported.
		// *  note 2 : Prefer relying on the enum than on its value whenever possible
		// *           This is the only supported way to use the error list < v1.3.1
		// *  note 3 : IsError() is always correct, whatever the library version.
		// **********************************************/
		public enum Error : uint
		{
			no_error = 0,
			GENERIC = 1,
			prefix_unknown = 10,
			version_unsupported = 12,
			frameParameter_unsupported = 14,
			frameParameter_windowTooLarge = 16,
			corruption_detected = 20,
			checksum_wrong = 22,
			dictionary_corrupted = 30,
			dictionary_wrong = 32,
			dictionaryCreation_failed = 34,
			parameter_unsupported = 40,
			parameter_outOfBound = 42,
			tableLog_tooLarge = 44,
			maxSymbolValue_tooLarge = 46,
			maxSymbolValue_tooSmall = 48,
			stage_wrong = 60,
			init_missing = 62,
			memory_allocation = 64,
			workSpace_tooSmall = 66,
			dstSize_tooSmall = 70,
			srcSize_wrong = 72,

			/* following error codes are __NOT STABLE__, they can be removed or changed in future versions */
			frameIndex_tooLarge = 100,
			seekableIO = 102,
			maxCode = 120 /* never EVER use this value directly, it can change in future versions! Use IsError() instead */
		}

		public static size_t ERROR(Error code)
		{
			return (size_t)(-(size_t)code);
		}

		public static bool IsError(size_t code)
		{
			return code > ERROR(Error.maxCode);
		}

		///*! GetErrorCode() :
		//    convert a `size_t` function result into a `ZSTD_ErrorCode` enum type,
		//    which can be used to compare with enum list published above */
		//ZSTDERRORLIB_API ZSTD_ErrorCode GetErrorCode(size_t functionResult);
		//ZSTDERRORLIB_API readonly char* GetErrorString(ZSTD_ErrorCode code);   /**< Same as ZSTD_getErrorName, but using a `ZSTD_ErrorCode` enum argument */


		//#if defined (__cplusplus)
		//}
		//#endif

		//#endif /* ZSTD_ERRORS_H_398273423 */
	}
}
