see filelist.ini for the list of files found/to place here

required for each build type that will be supported by the files:
_retail.ini  : for retail images
_glitch.ini  : for gg images
_glitch2.ini : for gg images using dual CB
_jtag.ini    : for jtag images
_devkit.ini  : for devkit images
_jtag_bigflash.ini : alternate name used with -i bigflash option on command line

optionally, the system update container (su20076000_00000000) can be provided for flash files
and backed by files automatically extracted from nanddump.bin or found in /common folder
instead of putting all files in this folder

note, with bls the following is true (before calculating CRC for an ini):
- the file is truncated to the u32 size found at offset 0xC
- CB/CB_A/CB_B 0x0 fill: @0x10 for 0x30 bytes
- CD 0x0 fill: @0x10 for 0x10
- CE 0x0 fill: @0x10 for 0x10
- CF 0x0 fill: @0x20 for 0x210
- CG 0x0 fill: @0x10 for 0x10

