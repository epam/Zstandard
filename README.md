# Deltix ZStandard library for Java/C#

This library contains implementations of [ZStandard](https://github.com/facebook/zstd) - Fast real-time compression algorithm for JVM and .NET platforms written completely in Java & C#.

Based on [Aircompressor](https://github.com/airlift/aircompressor) & the original C++ implementation.

Uses parts of [New Generation Entropy library](https://github.com/Cyan4973/FiniteStateEntropy)
# Requirements
### Java
This library requires a Java 1.8+ virtual machine containing the `sun.misc.Unsafe` interface running on a little endian platform.

Uses Gradle build tool.
### C#
This library requires .NET platform that supports netstandard1.1

Uses Cake build tool, which may also require Mono on Linux systems.
# License
This library is released under Apache 2.0 license. See ([license.txt](license.txt))
