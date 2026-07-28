X360GameHack 2025-2026

Huge Update just released stay tuned here more coming soon: 

https://www.youtube.com/@IcyModz420

Summary:
----------------------------------------------------
- X360GameHack 2026 is an open-source, all-in-one app designed to streamline the process of installing games on Xbox 360 RGH/JTAG consoles, Bad Update, Bad Avatar exploited consoles and devkits to make it easier for people who have never used an exploited console to pickup a console and install their personally backed up games in any form ISO or STFS.
- It has support for original Xbox ISO and XBE files. 
- Features heavily optimized code, advanced security, and open source transparency for today's day in age.
- Perfect for RGH sellers to give to their customers with their new RGH.
- Please only report bugs under the tabs marked as production ready listed below.. X360GameHack is still in production in 2026-2027 as a side hobby. Sorry I'm a professional painter and body man too I get tired. I also have other projects.

XEX Tool GUI: (Production Complete)
----------------------------------------------------
<img width="1947" height="1674" alt="xex patches" src="https://github.com/user-attachments/assets/bbf56f07-3557-43eb-892e-abcca69798e1" />

- Drag-and-drop interface for encrypting or decrypting Xbox 360 XEX files, eliminating command-line complexities and path-related errors (e.g., spaces in file paths).
- Displays XexTool output directly in the app.
- Previous support for using batch files instead of capturing process output.
- Quickly Encrypt, Decrypt, Compress and Uncompress XEX, EXE, or DLL files.
- Patch devkit builds of games to work on RGH consoles.
- Patch games to work on RGH consoles or Devkit consoles.
- Patch games to work on the Bad Update and Bad Avatar hypervisor exploit chains out of the box.
- Change Title ID and Media ID of xbox 360 XEX files.
- Print extended info about the XEX file.

X360PKGTool GUI: (Production Complete)
----------------------------------------------------
<img width="1937" height="1673" alt="Screenshot_3" src="https://github.com/user-attachments/assets/0d1d666d-1f87-4c93-8fb6-ba31ccf6e3e5" />

- Easy Unlock DLC buttons for RGH, Bad Update, Bad Avatar, and Devkit.
- Patch Package format to be RGH, Bad Update, Bad Avatar, or Devkit.
- Patch Packages to appear purchased on modded consoles.
- Patch package to remove all original paid licenses/purchase traces embedded in the file.
- Print extended info about package in X360GameHack and with full output.

XISO Tools: (Not fully Production Complete)
----------------------------------------------------
- Bulk XISO Tool extract a list of OG xbox and xbox 360 games one at a time and patch all the needed files in as few clicks as possible. (COMING BACK SOON)
- Extract single disc ISO and automatically patch the xexs if desired. (COMING SOON)
- 2 Disc game install support (Install the package files from disc one to USB.) (COMING SOON)
- Third party Xbox Image browser by redline and Xbox backup creator built in.
- Third party ABGX360 one click options built in.
- Create original xbox ISO from folder.
- Optimize original xbox ISO to run smoother on the older consoles with newer blank CDs.

Save Patches: (Production Complete)
----------------------------------------------------
<img width="1952" height="1678" alt="Screenshot_4" src="https://github.com/user-attachments/assets/6dab11fb-7521-48d0-a673-d996e4cbb21e" />

- Convert Xbox 360 save game container to devkit or rgh.
(You still need to change the ids first. Save. Convert. Then rehash and resign at the end for it to work.)
(You still need the save and default.xex of the game you are playing to have matching media ID for the game itself to be able to detect the save.)
- Print extended info about the save file.

XBE Patches: (Not fully Production Complete)
----------------------------------------------------
- Grab and print all info from the XBE Header. (Coming soon)
- Grab Title Update and game name from XBE. (Coming soon)
- Change Title ID in XBE file (Coming soon)
- Set the ram speed of the XBE to 64MB or 128MB.
- Set CPU speed of the XBE to a value from 733 to 1480. (or higher because why not if someone wants to try?)

ISO2GOD: (Fully Production Complete)
----------------------------------------------------
- Third party ISO2GOD for ease of use. Works on OG Xbox and Xbox 360 games.
- Convert xbox 360 and og xbox ISO files to GOD/STFS so you can install and run it on the dashboard with limited modding support.

X360SetupTool: (Not fully Production Complete)
----------------------------------------------------
- Detect correct folder and install GOD/XBLA/TU/PKG/Save to USB.
- ini editors like launch.ini jrpc.ini etc. (COMING SOON.)
- FTP2Xbox File transfer support for original xbox, xbox 360, PS3 & PS4.

X360GameHack Installation:
----------------------------------------------------
- Download the latest release from the GitHub Releases page.
- Extract the ZIP file to a directory of your choice.
- Run X360GameHack.exe (Windows only, 32-bit compatible).
- Ensure all dependencies (included in the X360GameHack folder) are present.

Console Setup:
----------------------------------------------------
- Set xbdm.xex as plugin 1 in the launch.ini configuration ini file manually or using the editor.
- Send it to console or usb or copy it to the storage device where the launch.ini will reside. 
- Xbox 360 neighborhood is not required and will never be included you only need the local ip of your console from the stock dashboard menu in wifi or to discover it.. its usually something like 192.168.137.000 or 10.1.0.69 etc..

What is xbdm or xbdm.xex?
----------------------------------------------------
- Xbox Debug Manager or Xbox Debug Management is a plugin that was originally created by microsoft to be used with XDK kits. 
- It was reverse engineered some 10+ years ago and patched to work on regular jtag/rgh consoles.
- Its a TCP protocol used by microsoft to allow xbox 360 neighborhood to connect to a xdk console from a pc using its ip address and includes a wide range of built in functionality now available to rgh consoles.

How does Dash Launch plugin loading work? (launch.ini)
----------------------------------------------------
- Dash Launch is a part of the consoles flash storage meaning it doesn't actually need a formal installation by you if your console is already an rgh.
- You do not need to download and open the Dash Launch dl30.xex application on your console or to even use the Dash Launch gui at all if not desired.
- You can simply create a launch.ini with this tool and send it to console or USB then reboot for it to take effect.
- Or you can install and/or launch the Dash Launch gui automatically on the console with X360GameHack and set and save it with the gui.
- Remember that if a launch.ini is on your usb Dash Launch will use the launch.ini on the usb first as a fail safe to save your hdd from a bad xex plugin (such as a downed stealth server leading to the xbox 360 boot up logo getting stuck/hanging when attempting to use a old stealth server plugin..) So be sure to edit the correct launch.ini so the plugin will actually take effect when you boot/start up the console.

Usage:
----------------------------------------------------
XEX Patching:
- Drag or open an XEX file in the app.
- Click the appropriate button to encrypt/decrypt or patch the file.
- View XexTool output in the app’s interface.

XBE Patching:
- Drag or open an XEX file in the app.
- Click the appropriate button to patch the file.
- View output in the app’s interface.

Bulk XISO Extraction:
- Drag all desired Xbox 360 and/or original Xbox game ISO in the list box.
- (Yes you can do both at the same time)
- Choose the output folder and click "Extract and patch" or similar to create a JTAG rip, optionally backup original executables, then specially patch the XEX/XBE files also.

Bulk Stealth Patch xbox 360 ISO with abgx360:
- Drag all desired xbox 360 ISO into the listbox
- Click Patch All XISO With abgx360

FTP2Xbox:
- Go to FTP2Xbox tab and type in your console local ip and the port the server is open on with correct username and password then click save.
- Proceed to use ftp options in the app like checkboxes.
- Connect and transfer files.

Contributing:
----------------------------------------------------
X360GameHack 2025 is open source and welcomes contributions. To contribute:
- Fork the repository.
- Create a new branch for your feature or bug fix.
- Submit a pull request with detailed descriptions of changes.
- Report bugs or suggest features via GitHub Issues.

Credits:
----------------------------------------------------
- Xorloser xextool, x360pkgtool
- XboxDevOrganization XISO
- Integrated Tools: Respect and credit to the original creators of god2iso, iso2god, Xbox Image Browser, Xbox Backup Creator, and others included in the X360GameHack folder.
- GUI Developer: IcyModz420.

Security Features:
----------------------------------------------------
- Anti-Admin Protection: There is no need to run this tool as admin so if you do it will auto kill the process.
- Anti-Debug Protection: Prevents attaching debuggers to the application.
- Anti-Path Traversal: Blocks attempts to hide malicious commands in the filepath used in the executable invoker class.
- Custom AES Encryption: Implemented via BouncyCastle for secure data handling.
- String Signature Checks: Ensures integrity of sensitive strings by checking against runtime.
- SHA3-256 Signature Checks: Ensures integrity of all exe, dll, etc using BouncyCastle.
- Proxy-Free Web Calls: Prevents interception by tools like Fiddler.
- Single Process Enforcement: Restricts running multiple instances of X360GameHack.
- Future Obfuscation: Planned obfuscation of specific classes and functions to deter tampering while keeping non-critical code accessible for open-source transparency. 
(Obfuscated code will remain viewable in tools like dnSpy (32-bit) but harder to rebuild and exploit in the field without expertise.)

Anti-Admin Explanation:
----------------------------------------------------
- If X360GameHack needs admin permission it WILL ALWAYS ASK via message box in the X360GameHack window before popping a windows UAC to spawn a separate admin CMD to do what it needs. 
- Please read the command the uac is trying to use via show more in the uac box it will show you the path and the cmd command it wants to use be for you click yes.
- You are is secure hands with X360GameHack but it cannot ever hurt to be too safe.

Future-Proofing:
----------------------------------------------------
- Open Source: Hosted on GitHub, allowing community contributions and vulnerability reporting. Users can submit bug reports or patches via the GitHub Issues page.
- Community-Driven Development: Feedback is welcomed to add new features or improve functionality.

Legal Disclaimer:
----------------------------------------------------
- This software is provided "as is" without warranty of any kind. Use it at your own risk, and ensure compliance with local laws regarding console modding and backup practices. 
- The developer does not claim ownership of third-party tools included in the package and has intentionally not included any licensed/paid programs.
- X360GameHack is a free, independent, open source, software project.
- It is not affiliated with, endorsed by, approved by, or sponsored by Microsoft Corporation or any other entity. All trademarks, copyrighted materials, and intellectual property, including but not limited to Xbox 360 game files, assets, DLC, ISO, title update files, and encryption keys, are the property of their respective owners. This tool is intended for lawful use only, such as extracting files from legally owned game ISOs for personal backup or archival purposes, or installing personally backed up games of which the user of X360GameHack is intended to physically own. 
- With best intentions to be in compliance with applicable copyright and intellectual property laws.
- By using this software users understand that it is illegal in the US, UK, Canada, and alike to download, extract, backup, play, and/or download/have a copy of a XiSO you do not physically or digitally (with limitations) own and that the creator of this software cannot be held responsible for the unintended use of this software.

Contact:
----------------------------------------------------
- GitHub: IcyModz420/X360GameHack2025
- YouTube: IcyModz420
- Console Crunch: IcyModz420
- se7insins AnonSec
