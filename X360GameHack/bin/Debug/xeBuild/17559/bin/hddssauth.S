#============================================================================
# This is an alternative hard drive authentication patch for 17559 FreeBoot
#
# Patches SataDiskAuthenticateDevice to do full authentication for hard disks
# with a security sector. This allows drives formatted by FATXPlorer with
# a security sector and "unusable space" to be used on FreeBoot machines, in
# addition to retail hard drives. With this patch, the microsoft logo is
# shown for hard drives with a security sector, just like on a retail 360.
#
# The error handler is patched to allow hard drives without a security sector
# to still function, just like the old-school freeboot drive auth patch.
#============================================================================
.include "macros.S"

#============================================================================
# Restore the two instructions that were patched by FreeBoot
#============================================================================
KMAKEPATCH 0x8015D9D8
0:
   mflr %r12
   KMAKEBRANCHL 0x8010CBCC
9:

#============================================================================
# Rather than calling VdDisplayFatalError to display an E69 (hehehehe),
# jump to the error handler in SataDiskAuthenticateDevice
#============================================================================
KMAKEPATCH 0x8015DA18
0:
   KMAKEBRANCH 0x8015DB08
9:

#============================================================================
# Patch error handler to set r3 to 1 indicating "auth success" if we hit
# any of the error paths in SataDiskAuthenticateDevice
#============================================================================
KMAKEPATCH 0x8015DB08
0:
   li %r3, 1
9:
