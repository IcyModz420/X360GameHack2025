If you have any problems installing the J-R Programmer drivers, this is how to fix.

The J-R Programmer uses an updated driver set that is also used by the Xecuter NAND-X but the old drivers do not work so you need to manually delete a few files and re-install with this new driver set (both the NAND-X and J-R Programmer will also work together on the same computer after you have installed the new drivers)

Open dm.jpg to see what correctly installed drivers look like in device manager

If you have a problem with one of the drivers (usually a yellow exclamation mark next to the device name) simply right click and uninstall (make sure you also tick the box that sayes "delete the driver software for this device), then manually delete the following files (some may not exist but just make sure they are all removed). We have included a bat file that will delete the files for you, del_old.bat - simply double click on it.

These files to be removed can be viewed in the image driver_files.jpg

C:\Windows\System32\drivers\PTLIBUSB0.SYS
C:\Windows\System32\PTLIBUSB0.DLL
C:\Windows\SYSWOW64\PTLIBUSB0.DLL

Now unplug the USB cable and replug (make sure the LED is green - the switch has to be to the right away from the reset button) and reinstall the new driver set. You will get a pop up notice saying 'Windows can't verify the publisher of this driver software" - simply select "Install this driver software anyway"

This happens if you had older versions of NAND-X drivers previously installed

To reset the J-R Programmer make sure the switch is to the right and simply push the reset button, this will make the driver re-detect. If some reason the LED stays on RED then simply unplug the USB cable and replug - you should get a green LED. If the LED always stays on RED then it is in bootloader mode and needs programming with the latest J-R Programmer Firmware (JRP_FIRMWARE_104.hex). You can do this by using the X360USB PRO Update Tool (http://forums.360mods.net/downloads.php?do=file&id=14)

For help and support please use the Xecuter support forums www.team-xecuter.com/forums