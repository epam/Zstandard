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
	unsafe class XXH64_state_s : IDisposable
	{
		public XXH64_state_s()
		{
			mem64 = (ulong*)ZStdDecompress.ZSTD_entropyDTables_t.InitPtr(new ulong[4], ref mem64Handle);
			reserved = (uint*)ZStdDecompress.ZSTD_entropyDTables_t.InitPtr(new uint[2], ref reservedHandle);
		}

		public void Dispose()
		{
			ZStdDecompress.ZSTD_entropyDTables_t.SafeFree(ref mem64Handle);
			ZStdDecompress.ZSTD_entropyDTables_t.SafeFree(ref reservedHandle);
		}

		public void FillZero()
		{
			total_len = 0;
			v1 = 0;
			v2 = 0;
			v3 = 0;
			v4 = 0;
			for (int i = 0; i < 4; ++i)
				mem64[i] = 0;
			memsize = 0;
			for (int i = 0; i < 2; ++i)
				reserved[i] = 0;
		}

		public ulong total_len;
		public ulong v1;
		public ulong v2;
		public ulong v3;
		public ulong v4;
		public ulong* mem64;   /* buffer defined as U64 for alignment */
		public uint memsize;
		public uint* reserved;          /* never read nor write, will be removed in a future version */

		private GCHandle mem64Handle;
		private GCHandle reservedHandle;
	}
}
