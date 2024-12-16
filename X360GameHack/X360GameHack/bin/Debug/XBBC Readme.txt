=================================================================================
Xbox Backup Creator v2.8 Build:0275 by Redline99

Added - Added support for BenQ VAD6038 0800 firmware
	(Note: This has had limited testing, be cautious)

=================================================================================
Xbox Backup Creator v2.7 Build:0265 by Redline99

Added - Added support for Kreon's SH-D163B firmware
Added - Added support for Kreon's SH-D162D firmware
Added - Verify Disc button on the Drive Tools Tab
Added - Added option to do a Verify Disc after a Burn
	(Note: Used ImgBurns build in Verify when selected)

=================================================================================
Xbox Backup Creator v2.6 Build:0260 by Redline99

Changed - Stealth Check Report
Changed - SplitVid Implementation for better compatibility
Changed - Removed Eject/Load button from Drive Tools

Added - More information from SS to log
Added - Stealth Check for Backup media
Added - Stealth Check Before Burn process for Nero
Added - Stealth Check Before Burn process for ImgBurn
Added - Stealth Check to Validate Media ID XEX-SS-DMI
Added - SplitVid format as a public option, Default Enabled
Added - Region Check button in Drive Tools
Added - Region Check button in Image Tools
Added - Better sector caching to generic dvd reader
Added - Check and on the fly fixing for backup media info in PFI
Added - Check to validate Video size versus PFI when injecting 
        into an ISO and in Stealth Check

Fixed - XDVDFS Parser to read on 0x10 sector alignments 

=================================================================================
Xbox Backup Creator v2.5 Build:0XXX by Redline99

Changed - Stealth Check Report
Changed - SplitVid Implementation for better compatibility
Changed - Removed Eject/Load button from Drive Tools

Added - More information from SS to log
Added - Stealth Check for Backup media
Added - Stealth Check Before Burn process for Nero
Added - Stealth Check Before Burn process for ImgBurn
Added - Stealth Check to Validate Media ID XEX-SS-DMI
Added - Media ID Check before injecting SS or DMI into an ISO Image
Added - SplitVid format as a public option, Default Enabled
Added - Region Check button in Drive Tools
Added - Region Check button in Image Tools
Added - Better sector caching to generic dvd reader
Added - Check and on the fly fixing for backup media info in PFI
Added - Check to validate Video size versus PFI when injecting 
        into an ISO and in Stealth Check

Fixed - XDVDFS Parser to read on 0x10 sector alignments 
Fixed - Issue with reading Backups on a SHD162C somtimes

Known Issues:
The read tab is slower to enable the controls
Return Value from ImgBurn not working properly
USB Drives in ASPI mode are not recognized correctly
And a few others from previous releases with some more new ones sprinkled in
to keep me up late.

Notes:
Check your settings, this version will reset all settings back to their defaults

On the Image Tools Tab if you use the "Image Browser" it will first check if
there is a dvd in the current selected drive. If there is a dvd in the drive then 
that is what will be displayed.  You can use the menu in the Image Brower to 
close that and open an iso file.

=================================================================================
Xbox Backup Creator v2.4 Build:0225 by Redline99

Added - Image Browser/Extractor
        Right mouse click in Tree/List for extraction options
        Use File menu to load an iso image from HDD
Added - Region Checking xbe/xex
Added - Support to burn image in ImgBurn (Currently doesn't get correct exit code?)
Added - Support to burn image with Nero (COM component)
Added - Support to detect identity changing between Hitachi and Samsung
        I've been too lazy to hook my Hitachi back up with a modified firmware
        so this has not been fully tested by myself. :)
Added - Support to Extract/Inject Video
Added - Support to read PFI, DMI, SS and whole image off of a backup
Added - Support for iso's that dont have PFI, DMI (will substitute standard values)

Changed - SPTI Timeout values, hopefully fixes more than creates problems :)
Changed - Drive Open method for burner to Exclusive Access
Changed - Changed verbiage on ISO Tools tab from Merge to Inject
Changed - Relaxed PFI check to allow for oddballs like Star Trek Legacy
Changed - Timeout value when setting layerbreak from 10 seconds to 120
Changed - Method to detect current visible partition, a little slower now though
Changed - Removed SGD-605B until I have the time to properly support it
Changed - Removed Save Firmware button, there are better tools for this
Changed - Removed Get DriveKey button, there are better tools for this

Fixed - Not being able to quit if the drive space was low and user selected cancel
Fixed - Initial lock state for Kreons drives
Fixed - File Handle not being closed after FindFirstFile
Fixed - Issue with non-standard video or game partition sizes (Star Trek Legacy)

=================================================================================
Xbox Backup Creator v2.3 Build:0185 by Redline99

Added - Support for SH-D163A using Kreon's firmware

Changed - Get Security Sector precedure to retry upto 5 times on failure
Changed - Stealth Check to include Security Sector
          It also will do an additional check for Xbox One 0x0200 - 0x02CF

=================================================================================
Xbox Backup Creator v2.2 Build:0180 by Redline99

Fixed - Some drive/media combinations didn't report Available bytes correctly

Added - Option to Perform the OPC or not, Default = True
Added - Option to Perform the Track Reservation or not, Default = True
Added - Fix for Invalid Xbox One Security Sector (Kreon pre .81 and Hitachi F900)
Added - Support for Maximus-Garyopa Hitachi-LG Xtreme v2.2 Stealth 8in1 (limited)
Added - Support for Frog Aspi v0.29.4, http://www.frogaspi.org/index.html
Added - Support for TS-H943A Xtreme v4.0 Game Partition Unlock command

Changed - Set Write Speed, Hopefully more compatible
Changed - New method of monitoring the tray status
Changed - GetMediaStatus routine, to allow for more waiting after closing a disk
Changed - Increased the File IO read timeout, 
	  this puts more responsibility on the burners buffer-underrun tech

Please start using the Frog Aspi v0.29.4 in ASPI mode. If it works for everyone
then I will probabally set that as default next time.

=================================================================================
Xbox Backup Creator v2.1 Build:0175 by Redline99

Removed - SDG-605B from being able to create a Complete Backup Image (Will bring back and fix later)
Removed - Most of the "Wait For Ever" CDB code options. This was causing what looked like a App hang.
Removed - Speed Boost check box. It is now controlled by the drop down box in the Drive Specific settings

Changed - Log File will always get the Detail Log Level unless logging is turned off
Changed - Reduced executable file size 
Changed - Improved installer
Changed - Read Me file to reflect common questions and current status.

Fixed - ToolTip text for the Merge/Extract on the Image Tools Tab
Fixed - Burner didn't write at selected speed
Fixed - Error on Windows 2000 related to "uxtheme.dll"
Fixed - DeviceIoControl Error when extracting Security Sector with SH-D162C (Experimental; not proven)

Added - More detailed logging of operations and results
Added - Hopefully some better writer compatibility (I can't really test)
Added - New logo by jhyphinwill, Thanks looks nice and it got me away from the Microsoft logo :)
Added - New Read error reporting that will tell you the file name of the data it can't read
Added - Check of the OS before using theme dll (uxtheme.dll)
Added - Additional Inquiry data (Nice for Kreon’s SH-D162C, shows version info)
Added - New dialog when a read error occurs (Allows Retry, Fill with zeros, Cancel)
Added - Should a file buffer error occur during writing, it will retry 10 times before failing
Added - Code to prevent multiple instances of the application
Added - Warning if the drive is already unlocked when doing a Complete Backup
Added - Better overall retry strategy when Initializing/Writing/Finalizing)
Added - A detailed Security Sector log
Added - Check of image file size before writing
Added - Optimum Power Calibration before writing
Added - Log file will now reset if larger than a 512KB
Added - New method for reading Xbox One disks. This should greatly improve the process. See Note *
Added - Feature to save the XDVDFS structure to a comma delimited text file (mostly for research)
Added - XDVDFS File system entry details (if a read error is in the file system area)
Added - Feature that if user selects to fill read errors with zeros, but a 
	read error occurs in file system data they will get prompted again as this 
	could be a media defect error in game data and shouldn't just be filled with zeros.

-------------------------------------------------------------------------------------------
* This uses a technique I called the "Sector Mapper" It uses the XDVDFS to read only the 
  data referenced by the file system.  In theory this should do two things:
  	1. Speed up the process by not trying to read the Security Place Holders
  	2. Speed up the process by only reading the data, not the entire disk.
  Please be aware that although this has been tested and is working the concept has
  not proven itself on a large scale.  It currently does not work on Xbox 360 disks.
-------------------------------------------------------------------------------------------
  
Xbox Backup Creator v2.0 Build:0100 by Redline99
	Initial Release

-------------------------------------------------------------------------------------------
-------------------------------------------------------------------------------------------
Hardware Requirements:
	Processor: Something within the last 3 years should be fine. I use a P4 3GHz with 1GB Ram
	Ram:       The application currently used a 20MB write buffer, shouldn’t be a 
		   problem on current machines.
	Drives:	   A DVD+R Double Layer burner that supports a Buffer Underrun Technology.
		   PC DVD Samsung SH-D162C with Kreons modified firmware (PC DVD Drive)**
		   Xbox 360 Samsung TS-H943A with Xtreme compatible firmware, v1, v2 or v3.
		   Xbox 360 Hitachi GDR-3120L with Xtreme compatible firmware with OPA v2.1***

OS Requirements:
	Windows NT platform with NTFS file system (because file sizes are 4+ GB)
	Windows XP SP2 is preferred, Vista has not been tested.
	If there is enough interest I may support Win9x by way of creating file sets 
	within FAT32 limits. An ASPI driver will also be required. (ASPI already supported)
	
Application Use:
	It should be pretty straight forward. Read your disk then write it to a blank DVD+R DL.
	Since this application is not multi-threaded, please do not disturb it while writing.
	
	After inserting a disk it may take a few seconds for it to be recognized. If it does
	not populate and enable the buttons after more than 30 seconds press 
	CTRL-R to force	a refresh.
	
	Remember, you must use one of the supported drives for READING Xbox disks.  The drive
	will ALSO need the modified firmware. :)  A lot of people were confused about this. 
	You cannot just pop the Xbox disk into your burner to make a backup.
	
	This app doesn't support making a backup of a backup. Find another way, its easy. 
	
	If your burner doesn't automatically set the book type to DVD-ROM for DVD+R DL then
	you must set the booktype before writing using the appropriate bitsetting app for 
	your burner.  Some of the newer Xbox drive firmwares get past this requirement.
		Samsung - Use Xtreme v3.1 or later
		Hitachi - Use the MediaCheck patch by Geremia
	
Known Issues or Errors:
	If your disk isn't recongized with in 20 seconds, press CTRL-R to force a refresh
	
	May have display issues with some locale settings, not really tested.
	
	Writing doesn't support all burners, I hope that it has improved though!
	
	Session Fixation Errors are usually due to media and/or burner firmware.
	
	Optimum Power Calibration failures are usually due to low quality media.

	Invalid Write Address failures are usually due to low quality media.

	You may get a DeviceIoControl error with a Sense error of "0/00/00", I don't have a good 
	answer but, some people have reported success by reinstalling updated Sata drivers. Also
	another method that may work is to use the Nero ASPI driver instead of SPTI.
	
	During the write initialization you may encounter "Power On, or Bus Device Reset occurred"
	I have not been able to figure out the root of this or the proper method of recovery.
	If you happen to know, please fill me in on the secret. :)
	
Supported Backup Images:
	Xtreme style disk images version 1-3. wxRipper support may be added at a later date.
	The application will only build version 3 style images. I don't see the need to add 
	the complication of the version 1,2 types. This is because the data is outside 
	the readable LBA range to the Xbox and the older firmwares will just not use the extra 
	DMI and PFI sectors. The Security Sector is in the same position across versions. 
	
Xbox Versions:
	Xbox 360: Xbox 360 disks and Xbox One disks!
	Xbox One: Not supported, previous version was making the ISO incorrectly
	          and I haven't had time to fix yet. Hopefully next major release.
	
Support:
	I will do my best to keep current and add requested features, feel free to contact 
	me with ideas, compatibility issues or bug reports.  I ask of you to please not use 
	the forums to talk about your adventures in pirating.  Specifically the forums at
	www.xboxhacker.net and www.xbox-scene.com.  My personal belief is that making a
	backup of property you purchased is within your rights.  Of course not everyone shares
	this opinion and you should use common sense and do not distribute or download backup
	disk images.  You can view Microsoft's statement on piracy at the link below.
			http://www.microsoft.com/piracy/default.mspx

Installation:
	This application requires the Visual Basic 6 sp6 runtimes, included if you need them
	These is also a dependency on MSCOMCTL.OCX, included in the msi setup package.
	Hopefully you can just run the exe! Use the Setup Package if you need it.
	If you are upgrading from a previous version, you should be able to just copy
	the executable and run it.
	If you decide to run the installer, uninstall the previous through Add/Remove Programs.
	
-------------------------------------------------------------------------------------------
Some shortcut keys:
	CTRL-TAB = Switch to next tab
	CTRL-L   = Show/Hide Log
	CTRL-C   = Cancel current operation
	CTRL-R   = Refresh Disk Information
	
-------------------------------------------------------------------------------------------
   * Provided you do it my way and your hardware is compatible! :)

  ** Pre Version .80 of Kreon's Samsung SH-D162C doesn't completely support the Security Sector 
     extraction, but the application will detect the capability when it is available.
     By the way... Good job Kreon! 
     
     Also note that .80 has a bug that prevents the SS from working on a Xbox One. You can manually
     fix it or better yet use Kreon's .81 version.
     
 *** Currently, the Hitachi GRD-3120L is not capable of viewing the game partition without 
     a hardcoded firmware patch. Hopefully someone will create proper support for unlocking.
