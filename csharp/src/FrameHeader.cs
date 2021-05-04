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

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace EPAM.Deltix.ZStd
{
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
#if ZSTDPUBLIC
	public
#else
	internal
#endif
	struct FrameHeader
	{
		public void Reset()
		{
			frameContentSize = 0;
			windowSize = 0;
			blockSizeMax = 0;
			frameType = 0;
			headerSize = 0;
			dictID = 0;
			checksumFlag = 0;
		}

		public ulong frameContentSize; /* if == ZSTD_CONTENTSIZE_UNKNOWN, it means this field is not available. 0 means "empty" */
		public ulong windowSize;       /* can be very large, up to <= frameContentSize */
		public uint blockSizeMax;
		public ZSTD_frameType_e frameType;          /* if == ZSTD_skippableFrame, frameContentSize is the size of skippable content */
		public uint headerSize;
		public uint dictID;
		public uint checksumFlag;
	}
}
