# README for 1888 Glitch2m and DevGL images

## Only small block phats are supported!

Don't expect this to work on a BB jasper, or any flavour of slim. The 1888 kernel does not have a driver for the 256/512mb BB flash controller, and the slim GPU is much too different to work with similar patches.

## GPU Patches

1888 did not officially support Rhea and Zeus GPUs, so the patches for Zephyr/Falcon/Jasper have board specific patches for Rhea (Falcon and Zephyr) and Zeus (Jasper). As such, there are some special cases to worry about. Otherwise, you may get an E78 (unsupported GPU) or graphical artifacting.

- If the GPU has been retrofitted, you must use the board type that originally came with the GPU (i.e. use falcon if a Rhea has been installed, Jasper if a Zeus or Kronos has been installed)
- To create a good image for a zephyr A (has a Y1), rename patches_g2mzephyr_y1.bin to patches_g2mzephyr.bin
- For an elpis xenon, ensure you create a glitch2m image with the elpis checkbox selected

## Software Compatibility

- xexmenu runs great on 1888, early versions of FSD seem to work too. So long as the homebrew doesn't use any new kernel functions, it should work.
- Homebrew that requires HV access *must* use syscall 0. The 1888 hypervisor doesn't have expansion support or HvxSetState.
- Official 360 games that require up to and inluding kernel 4552 appear to work fine
- Original xbox games work well, the newest hacked emulator files appear to function correctly and games not originally supported on 1888 can be played
- A DashLaunch substitute based on BladesDL is being used here- it provides the following features. There is no plugin support or ini configuration file.
   - XBL blocking
   - Memory protection toggle for original xbox games
   - Retail/Dev xex key swapping
   - Reboot instead of freeze if the kernel hits a bugcheck
   - ping patch
   - patch update strings in xam to $$ystemUpdate

## Credits

- sk1080 for the GPU patches
- dotprofile for the mostly labelled 1888 kernel/HV
- InvoxiPlayGames for the usbdsec patch
- Byrom90 for the 6770 compatible BladesDL I used as a base
