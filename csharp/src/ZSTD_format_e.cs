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

namespace EPAM.Deltix.ZStd
{
#if ZSTDPUBLIC
	public
#else
	internal
#endif
	enum ZSTD_format_e : uint
	{
		/* Opened question : should we have a format ZSTD_f_auto ?
		 * Today, it would mean exactly the same as ZSTD_f_zstd1.
		 * But, in the future, should several formats become supported,
		 * on the compression side, it would mean "default format".
		 * On the decompression side, it would mean "automatic format detection",
		 * so that ZSTD_f_zstd1 would mean "accept *only* zstd frames".
		 * Since meaning is a little different, another option could be to define different enums for compression and decompression.
		 * This question could be kept for later, when there are actually multiple formats to support,
		 * but there is also the question of pinning enum values, and pinning value `0` is especially important */
		ZSTD_f_zstd1 = 0,        /* zstd frame format, specified in zstd_compression_format.md (default) */
		ZSTD_f_zstd1_magicless,  /* Variant of zstd frame format, without initial 4-bytes magic number.
		                              * Useful to save 4 bytes per generated frame.
		                              * Decoder cannot recognise automatically this format, requiring instructions. */
	}
}
