# Pinned local C++ toolchain

`manifest.json` pins the exact compiler archive, SHA-256, testlib revision, and source-file checksums used by Polygon AI Builder.

- Compiler: WinLibs GCC/MinGW-w64 x86_64 UCRT POSIX SEH. The downloaded distribution contains its upstream runtime and license notices under `mingw64/share/licenses`.
- testlib and checker sources: Mike Mirzayanov's official testlib repository, pinned to revision `1e4e8a24c79c6bad3becbdb5a332ffc352b7d5dd`; its MIT license is stored at `testlib/LICENSE`.

Run `scripts/acquire-toolchain.ps1` to download and verify the untracked compiler bundle. Run `scripts/verify-toolchain.ps1` for checksum, compiler-version, and GNU C++17 smoke checks.
