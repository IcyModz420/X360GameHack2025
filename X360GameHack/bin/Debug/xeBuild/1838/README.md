## How to use

Build and flash it like any other XDKBuild image. You'll need to be on the latest release of JRunner. Don't expect any homebrew to work- 1838 is XEX1, *99.999999%* of the homebrew that exists is XEX2

## What's supported?

The 1838 kernel has the following restrictions:

- Console: Phat only for now.
- GPU: Fixed Y1 is the only GPU that will work 100% glitch free. Rhea and Zeus are functional with the additional patches, but the EDRAM may occasionally fail to train and it will look like your GPU is failing. Don't panic (unless you've got a defective GPU ofc), reboot the system and it should be OK.
- NAND: Devkit or Zero fuse supports both 16mb and 64mb. Due to xeBuild limitations, only 16mb is supported for glitch2m.

> [!IMPORTANT]
> Systems that use 16mb NAND chips must put the "1838-fs" folder on the root of the hard drive to be able to boot to the real dashboard. The system will boot to xshell, and dash.xex has been replaced with a minimal launcher that can then load the real dash.xex (or another application) from a USB flash drive.
>
> In addition, kernel 1838 has zero support for retail formatted hard drives. It MUST be dev formatted. Xbox 360 neighborhood does work, so if you've got a dev formatted hard drive you can boot the system and copy the files over. Alternatively, you'll need to use a FATX injector program.

## What's this about a dash.xex substitute?

1838 is a bit of a weird situation- it doesn't support retail formatted hard drives, and xshell doesn't support loading anything from a flash drive. On 64mb machines you've got access to the dash and can format a drive, however on a 16mb machine there simply isn't enough room for the "real" dash.xex. That means you're effectively locked out of actually *doing anything* with the system unless you format a hard drive outside of the 360.

This distribution of 1838 includes an xexmenu-style file browser under __nandfiles/dash.xex__ that can browse FAT32 formatted USB devices (in addition to the hard drive or flashfs) and launch xex or exe format applications. It can also launch XeLL from NAND (xell-gggggg.bin is stored in the flashfs)

Does this count as the world's first XEX1 homebrew?

## How do i install the dash.xex substitute?

It is included in the flashfs of 16mb images by default, and can be added to the __DEVKIT__ folder of a hard drive so that 64mb machines (and 16mb machines with a hard drive) can access the same loader menu.

## Preparing the hard drive with the zfBuild `hdd.tools.py`

> [!CAUTION]
> The following steps will erase and format the hard drive! Make sure a backup of all important data is taken!

As of the writing of this guide, you will need a linux environment with python3 installed. I used a virtual machine of linux mint and attached the drive in question with USB passthrough. WSL will NOT work as it does not have low level access to the drive. Native windows might work if you use the raw device path (`\\.\PhysicalDriveN` etc.) but is untested. This guide was tested with a 20gb retail hard drive. Larger sizes will likely work fine.

1. Back up the hard drive data!!!!
2. Determine the physical path of the attached hard drive. This can be done on linux with `sudo lshw -class disk`. You should see a `disk` with the appropriate `size` in the output. The `logical name`, such as `/dev/sdb` is what you will use in future commands.
3. Back up the security sector: `sudo python3 hdd.tools.py --drive <physical drive> --backup-ss <path to ss backup>`
   * Example: `sudo python3 hdd.tools.py --drive /dev/sdb --backup-ss ~/Desktop/hddss.bin`
4. Create the new partition table: `sudo python3 hdd.tools.py --drive <physical drive> -dg -c`
5. Format the drive: `sudo python3 hdd.tools.py --drive /dev/sdb --format-all`
6. For 16mb consoles, inject the 1838-fs folder: `sudo python3 hdd.tools.py --drive /dev/sdb --insert <path to 1838-fs folder>`
   * Example: `sudo python3 hdd.tools.py --drive /dev/sdb --insert ~/Desktop/1838-fs`
7. Optional: inject any folders containing tools with syntax similar to step 6. However, Xbox 360 neighborhood is fully supported in 1838 so you can alternatively boot the system and copy files over the network.

## Troubleshooting

If 1838 refuses to boot, or will boot but has weird behaviour, build an image with settings reset by selecting nomobile under `Options` > `Advanced XeBuild Options` > `nomobile`. It has been reported that using a retail NAND dump with family settings enabled to build an 1838 image will cause unexpected behaviour.

If 1838 boots, but you encounter GPU artifacting or freezing, reboot the machine. Y1 GPUs are officially supported by 1838, but Rhea and later may occasionally fail to train the edram and display artifacting similar to a failing GPU. Driver changes in later versions of the kernel resolve this.  

## Credits

- sk1080 for help getting this going and for the hard drive script
- Scar for help getting this working on Rhea and Zeus
- XDKBuild VFuse patches and flag fixer: xvistaman2005
- 360hub Discord Server - https://discord.gg/z9r3HMUxp7
- RGLoader Discord Server - https://discord.gg/jTDT4rAh56

... and anyone else i may have forgotten