Summary:
----------------------------------------------------
- X360GameHack 2025 is an open-source, all-in-one app designed to streamline the process of installing games on Xbox 360 RGH/JTAG consoles and devkits to make it easier for people who have never used an RGH/JTAG to pickup a console and install their personally backed up games, with added support for original Xbox ISO Optimizing, extracting, info grabbing, and xbe patches such as ram and cpu, etc. It also includes universal FTP Support and XBDM support for xbox 360. As well as built in gemini ai for any modding related questions users may have. It features heavily optimised code and advanced security with open source transparency for today's day in age.
- Perfect for RGH sellers to give to their customers with their new RGH.

Core Functionality:
----------------------------------------------------
- XexTool GUI: Drag-and-drop interface for encrypting or decrypting Xbox 360 XEX files, eliminating command-line complexities and path-related errors (e.g., spaces in file paths). Displays XexTool output directly in the app.
- Bulk XISO Tool: Automates extraction of Xbox 360 game ISOs into "JTAG rips" (folder-based game extracts) and simultaneously patches XEX files to unlock them for RGH, XDK, or badupdate exploit. Supports original Xbox games for extracting ISOs and applying RAM/CPU patches for enhanced systems. 
- Bulk clean ISO: Bulk stealth patch iso with abgx360 to be used with flashed or emulated drives.
- FTP2Xbox: File transfer support for original xbox, xbox 360, & PS4. 
- Integrated Tools: 
- God2Iso: Convert God (STFS) to iso.
- Iso2God: Convert Original xbox or xbox 360 iso to game on demand so they can played from the dashbboard menu.
- Xbox Image Browser: View all files and replace or extract a file in the iso.
- Xbox Backup Creator: Create backups of xbox 360 games with a special pc drive flashed with custom firmware and more.
- Launch.ini editor for easy plugin editing: Simplifies setting up plugins and editing launch.ini files, with options to transfer configurations to the console’s HDD, connected USB, or generate new files.
- One-Click Homebrew Installations: Supports installing essential applications like XexMenu, Aurora, Freestyle Dashboard, Dashlaunch, and potentially skins or other necessities directly to the console’s HDD or a connected USB.
- One-Click Homebrew Launching: Launch xexmenu, dashlaunch, aurora, fsd, games, etc over xbdm.
- Gemini AI Integration: Includes a free, older version of Gemini AI accessible via a Q&A tab, providing detailed answers to common modding questions.

X360GameHack Installation:
----------------------------------------------------
- Download the latest release from the GitHub Releases page.
- Extract the ZIP file to a directory of your choice.
- Run X360GameHack.exe (Windows only, 32-bit compatible).
- Ensure all dependencies (included in the X360GameHack folder) are present.

Console Setup:
----------------------------------------------------
- Set xbdm.xex as plugin 1 in the launch.ini canfiguraton ini file manually or using the editor.
- Send it to console or usb or copy it to the storage device where the launch.ini will reside. 

What is xbdm or xbdm.xex?
--
- Xbox Debug Manager or Xbox Debug Management is a plugin that was originally created by microsoft to be used with XDK kits. 
- It was reverse engineered some 10+ years ago and patched to work on regular jtag/rgh consoles.
- Its a tcp protical used by microsoft to allow xbox 360 neighborhood to connect to a xdk console from a pc using its ip address and includes a wide range of built in funtionality now avalible to rgh consoles.

How does DashLaunch plugin loading work? (launch.ini)
--
- Its a part of the consoles flash stoarge meaning it doesn't actually need a formal installation if your console is already an rgh and you also do not need to open the dashlaunch dl30.xex or to even use the dashlaunch gui at all if not desired.. You can simply create a launch.ini with this tool and send it to console or usb or even install and/or launch the dashlaunch gui automatically and set and save it with the gui.
- Remember that if a launch.ini is on your usb dashlaunch will use the launch.ini on the usb first as a fail safe to save your hdd from a bad xex plugin (such as a downed atealth server leading to the xbox 360 boot up logo hanging when attempting to use a old stealth server plugin..) So be sure to edit the correct launch.ini so the plugin will actually take effect when you boot/start up the console.
- Xbox 360 neighborhood is not required and will never be included you only need the local ip of your console from the stock dashboad menu in wifi or to discover it.. its usually something like 192.168.137.000 or 10.1.0.69 etc..

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
- Xorloser xextool, x360pkgtool, x360gamehack v6.3 creator
- XboxDevOrganization XISO
- GUI Developer: IcyModz420 (GitHub)
- Integrated Tools: Respect and credit to the original creators of god2iso, iso2god, Xbox Image Browser, Xbox Backup Creator, and others included in the X360GameHack folder.
- AI: Powered by an older, free version of Gemini AI.

Security Features:
----------------------------------------------------
- Anti-Debug Protection: Prevents attaching debuggers to the application.
- Anti-Path Traversal: Blocks attempts to hide malicious commands via the executable invoker class.
- Custom AES Encryption: Implemented via BouncyCastle for secure data handling.
- String Signature Checks: Ensures integrety of sensitve strings by checking against runtime.
- SHA3-256 Signature Checks: Ensures integrity of other exe, dll, etc using BouncyCastle.
- Proxy-Free Web Calls: Prevents interception by tools like Fiddler.
- Single Process Enforcement: Restricts running multiple instances of X360GameHack.
- Future Obfuscation: Planned obfuscation of specific classes and functions to deter tampering while keeping non-critical code accessible for open-source transparency. 
(Obfuscated code will remain viewable in tools like dnSpy (32-bit) but harder to rebuild and exploit in the field without expertise.)

Future-Proofing:
----------------------------------------------------
- Open Source: Hosted on GitHub, allowing community contributions and vulnerability reporting. Users can submit bug reports or patches via the GitHub Issues page.
- Community-Driven Development: Feedback is welcomed to add new features or improve functionality.

Virus scans:
----------------------------------------------------
- While searching google for X360GameHack related questions and info I came across this scan of the entire github repository and have decided to include it:
https://www.urlquery.net/report/383df792-a00b-40cf-90de-2de7a56bb1ed 
(It appears to have around 7 false positives on applications other than X360GameHack.exe its self.)
- For users that wish to verify the source of these additional applications the hash of all files has been provided in that link.
- To verify they are the same you can download the same exact versions of the 7 applications such as abgx360 installer from Hadzz and compare the hash and see that it is the same and that not a single byte of any of those has been changed. 
- Due to this application being primarally open source this means that it is possable to easilly alter the code of the app and rebuild it then proceed to share it.. Therefore it is recommended to only download this software from this offical github page and I must ask all users that they DO NOT repost just the ".zip" of "X360GameHack" or share the files outside of the github link in any way.

Legal Disclaimer
----------------------------------------------------
- This software is provided "as is" without warranty of any kind. Use it at your own risk, and ensure compliance with local laws regarding console modding. The developer does not claim ownership of third-party tools included in the package.
- X360GameHack is a free, independent, open source, software project and is not affiliated with, endorsed by, or sponsored by Microsoft Corporation or any other entity. All trademarks, copyrighted materials, and intellectual property, including but not limited to Xbox 360 game files, protected code within games including assets, and encryption keys, are the property of their respective owners. This tool is intended for lawful use only, such as extracting files from legally owned game ISOs for personal backup or archival purposes, in compliance with applicable copyright and intellectual property laws.
- By using this software users understand that it is illegal in the US, UK, Canada, and alike to download, extract, backup, play, and/or have a copy of a XiSO you do not physically or digitally (with limitations) own and that the creator of this software cannot be held responsible for the unintended use of this software.

Contact
----------------------------------------------------
- GitHub: IcyModz420/X360GameHack2025
- YouTube: IcyModz420v2
- TheTechGame IcyModzXeX
- Console Crunch: IcyModz420
- se7insins AnonSec
