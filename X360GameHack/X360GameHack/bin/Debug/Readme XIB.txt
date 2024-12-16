=================================================================================
Xbox Image Browser v2.9 Build:0350 by Redline99

Browse and Extract ISO files

Added - Added folder preselection to Save As browse dialog
Added - Command line parsing to open an iso from explorer (Open With command)

NOTE:
Xbox Image Browser is intended to only work with iso files.  Reading directly
from a dvd disc is supported by the Image Browser embedded in Xbox Backup Creator.

=================================================================================
Xbox Image Browser v2.9 Build:0345 by Redline99

Browse and Extract ISO files

Added - XGD3 Support
Changed - Some minor clean ups

USAGE:

Selecting the "su20076000_00000000" will display the version number.
Selecting an "xex" file will display the region

Right clicking on top level file name will extract the entire ISO.
Right clicking on a folder will extract it and it's sub folders.
Right clicking on a single will will allow you to extract that file.
Right clicking on a single file will allow you to replace the file.
(The file size must be the same or smaller.)

This application is basically the same one embedded in Xbox Backup Creator.


NOTE:

If your getting an error in windows 7 (or other os) with mscomctl.ocx which 
is stopping backup creator from running then try this

Get the mscomctl.ocx whether zip/rar form or whatever, extract to( C:\WINDOWS\SYSTEM directory. ) LEFT CLICK ON "Command Prompt" and choose "RUN AS ADMINISTRATOR"
this is important, RUN AS ADMINISTRATOR ok got that good, then use "CD C:\Windows\system"
NOT \system32
Then "regsvr32 mscomctl.ocx"


Running XBC on 64-bit windows 7 its different again you need to put MSCOMCTL.OCX into C:\Windows\SysWOW64 folder.
Run command prompt as Administrator and ether type or copy and paste this in >>> Regsvr32 c:\windows\SysWOW64\MSCOMCTL.OCX if anyone needs it > Here.

http://www.majorgeeks.com/files/mscomctl.zip

