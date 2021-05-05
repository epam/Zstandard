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
using System.Security.Cryptography;

namespace Zstandard.Tests
{
	class XorShift128Plus
	{
		private UInt64[] s = new UInt64[2];

		public XorShift128Plus()
		{
			var cryptoResult = new byte[s.Length * sizeof(UInt64)];
			RandomNumberGenerator.Create().GetBytes(cryptoResult);

			for (int i = 0; i < s.Length; ++i)
				s[i] = BitConverter.ToUInt64(cryptoResult, i * sizeof(UInt64));
		}

		public XorShift128Plus(UInt64 seedLo, UInt64 seedHi)
		{
			s[0] = seedLo;
			s[1] = seedHi;
		}

		public void Seed(UInt64 seedLo, UInt64 seedHi)
		{
			s[0] = seedLo;
			s[1] = seedHi;
		}

		public UInt64 Next()
		{
			var x = s[0];
			var y = s[1];
			s[0] = y;
			x ^= x << 23; // a
			s[1] = x ^ y ^ (x >> 17) ^ (y >> 26); // b, c
			return s[1] + y;
		}
	}
}
