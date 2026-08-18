##################################
# standard patch format:
##################################
.long address
.long size # number of u32 in patches
<patch data equaling above size>


##################################
#jtag:
##################################
#patches_<consolemodel>.bin

# all patches are placed into NAND for loading
# by the freeboot core (after exploit)

#format:
<1bl patches>
0xFFFFFFFF
<CB patches>
0xFFFFFFFF
<CD patches>
0xFFFFFFFF
<kernel/hv patches>
0xFFFFFFFF

##################################
#glitch:
##################################
#patches_fat.bin or patches_trinity.bin

# CB_B and CD patches are applied directly to the bootloader data
# when the image is created, and only kernel/hv patches are placed
# into NAND for loading by the CD patches

#format:
<CB_B patches> # note in the included patch set there is no data here
0xFFFFFFFF
<CD patches>
0xFFFFFFFF
<kernel/hv patches>
0xFFFFFFFF


##################################
#external/optional/addon patches:
##################################

# these patches are tacked onto the end of kernel/hv patches by the builder
# and are requested to be added on via the image builder's -a name command
# for example, "-a nomu" on the command line will attempt to load and append
# the file "nomu.bin" from the /bin directory of the firmware you are building
# up to 16 individual files can be specified

#format:
# use standard patch format, do NOT end with 0xFFFFFFFF, just the patch data is required
# the builder will take care of reloctating the final 0xFFFFFFFF and will append
# a u32 after that to the in flash patch set with the total number of bytes patched 
# in via options for on-xbox updaters like dash launch to make use of



