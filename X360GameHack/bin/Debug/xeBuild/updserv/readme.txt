Update Server for Xebuild Client
================================

The main purpose of this xex is to simply run the built in user dll that provides
a server that xebuild communicates with. It can easily run at console boot without
any controller intervention by simply placing default.xex on the root of a FAT formatted
memory stick, and plugging the memory stick into the console before powering it on.

The built in server dll is also included in dash launch version 3.08 or greater installer GUI.

v3
- added command so client can retreive raw bootloaders (everything from 0 to first patch slot)

v2
- fix in server dll for corona 16M (thanks Danny Lane!)
- additional debug info on screen if server dll can't identify the flash chip
