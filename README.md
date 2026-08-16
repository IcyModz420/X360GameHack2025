X360GameHack 2025-2026

Huge Update just released stay tuned here more coming soon: 

https://www.youtube.com/@IcyModz420

I should probably mention XBDM has been postponed due to my router being WPA3 & 5G at the moment and the 360 only supports WPA2 & 2.4g bands which you would have to login and set.

Summary:
----------------------------------------------------
- X360GameHack 2026 is an open-source, all-in-one app designed to streamline the process of installing games on Xbox 360 RGH/JTAG consoles, Bad Update, Bad Avatar (Referred to inside X360GameHack as "exploited consoles"), and devkits to make it easier for people who have never used an exploited console to pickup a console and install their personally backed up games from any form ISO or STFS.
- Features support for original Xbox ISO and XBE files. 
- Features heavily optimized code, advanced security, and open source transparency for today's day in age.
- Perfect for RGH sellers to give to their customers with their new RGH.
- Please only report bugs under the tabs marked as production ready listed below.. X360GameHack is still in production in 2026-2027 as a side hobby. Sorry I'm a professional painter and body man too I get tired. I also have other projects.

XEX Tool GUI: (Production Complete)
----------------------------------------------------
<img width="1952" height="1664" alt="Screenshot_1" src="https://github.com/user-attachments/assets/f3248da5-5da5-43d6-a834-ce00a7213277" />

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
<img width="1956" height="1662" alt="Screenshot_2" src="https://github.com/user-attachments/assets/afa21b35-e330-4abe-9a5b-040d8aacc23d" />


- Easy Unlock DLC buttons for RGH, Bad Update, Bad Avatar, and Devkit.
- Patch Package format to be RGH, Bad Update, Bad Avatar, or Devkit.
- Patch Packages to appear purchased on modded consoles.
- Patch package to remove all original paid licenses/purchase traces embedded in the file.
- Print extended info about package in X360GameHack and with full output.

XISO Tools: Fully Production Complete)
----------------------------------------------------
<img width="1948" height="1659" alt="Screenshot_3" src="https://github.com/user-attachments/assets/cad3b122-2073-44aa-b767-c072525072dc" />

- Bulk Extract single disc ISO and automatically patch the xexs if desired.
- 2 Disc game install support (Install the package files from disc one to USB.) (COMING SOON)
- Third party Xbox Image browser by redline and Xbox backup creator built in.
- Third party ABGX360 one click options built in for use with flashed xboxes.
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
<img width="1944" height="1677" alt="Screenshot_7" src="https://github.com/user-attachments/assets/9afcd9e2-bee3-4168-9076-12fe5a66a0b0" />

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
<img width="1944" height="1666" alt="Screenshot_11" src="https://github.com/user-attachments/assets/61698c89-3701-4f43-82c3-4efd185c21dd" />

- Detect correct folder and install GOD/XBLA/TU/PKG/Save to USB.
- ini editors like launch.ini jrpc.ini etc. (COMING SOON.)
- FTP2Xbox File transfer support for original xbox, xbox 360, PS3 & PS4.

FTP Client: (Not fully Production Complete)
----------------------------------------------------
<img width="1949" height="1672" alt="Screenshot_5" src="https://github.com/user-attachments/assets/c9d6869d-0cbb-4f40-a087-43dcb172800e" />

- Transfer Files to and from original xbox and xbox 360.
- FTP2Xell is a web browser that connects to the IP of the console running xell 
(Note you will need an internet connection to connect to xell over ftp and access the remote web page.)

Built In Q&A: (Not fully Production Complete)
----------------------------------------------------
<img width="1953" height="1673" alt="Screenshot_17" src="https://github.com/user-attachments/assets/12c0f471-12bf-47d6-8462-56b7c9325ab0" />

- Answer all game installing and X360GameHack questions.

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
- (Yes you can do both at the same time.)
- Choose the output folder and click "Extract and patch" or similar to create a JTAG rip, optionally backup original executables, then specially patch the XEX/XBE files also.

Bulk Stealth Patch xbox 360 ISO with abgx360:
- Drag all desired xbox 360 ISO into the listbox.
- Click Patch All XISO With abgx360.

FTP2Xbox:
- Go to FTP2Xbox tab and type in your console local ip and the port the server is open on with correct username and password then click save.
- Proceed to use ftp options in the app like checkboxes.
- Connect and transfer files.

Security Features:
----------------------------------------------------
- Anti-Admin Protection: There is no need to run this tool as admin so if you do it will auto kill the process.
- Anti-Debug Protection: Prevents attaching debuggers to the application on a timer and manually so any attempt likely fails.
- Anti Command Injection: Blocks attempts to hide malicious commands (Ex. cmd, powershell, & escaping) in the Active Filepath and other paths sent to executable classes.
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

Windows Defender Virus Hit "This program executes commands from an attacker." Explanation:
----------------------------------------------------
- It has come to my attention that windows defender will no longer let me build X360GameHack without adding an exclusion in the program for it.. It claims that X360GameHack "Executes commands from an attacker.".. technically it does execute commands in user land command line apps. But not only is that *** backwards but it actually protects you from "Privilege Escalation" and "OS Command Injection" unlike mpgui and known rival programs. This project is fully open source and if you want to check anything before you build and run it it is very easy to do and I challenge anyone who can find something malicious (Ex, OS Command Injection, Privilege Escalation) to report the reproducible issue here for us to see with discord username for a $50 curtesy reward in this free program.
"If a virus already has user mode privileges it doesn't need to hack/change my program it needs privilege escalation."

Future-Proofing:
----------------------------------------------------
- Open Source: Hosted on GitHub, allowing community contributions and vulnerability reporting. Users can submit bug reports or patches via the GitHub Issues page.
- Community-Driven Development: Feedback is welcomed to add new features or improve functionality.

Legal Disclaimer:
----------------------------------------------------
- This software is provided "as is" without warranty of any kind. Use it at your own risk, and ensure compliance with local laws regarding console modding and backup practices. 
- The developer does not claim ownership of third-party tools included in the package and has intentionally not included any licensed/paid programs.
- X360GameHack is a free, independent, open source, software project.
- It is not affiliated with, endorsed by, approved by, or sponsored by Microsoft Corporation or any other entity. All trademarks, copyrighted materials, and intellectual property, including but not limited to Xbox 360 game files, assets, DLC, ISO, title update files, and encryption keys, are the property of their respective owners and will never be included. This tool is intended for lawful use only, such as extracting files from legally owned game ISOs for personal offline modding, personal backup, archival purposes, and/or installing personally backed up games of which the user of X360GameHack is intended to physically own. 
- With best intentions to be in compliance with applicable copyright and intellectual property laws.
- By using this software users understand that it is illegal in the US, UK, Canada, and alike to download, extract, backup, play, edit/patch, and/or download/have a copy of a Original xbox, Xbox 360, or any other game consoles XISO you do not physically or digitally (with limitations) own and that the creator of this software cannot be held responsible for the unintended use of this software.

Rebuilding X360GameHack from source:
----------------------------------------------------
- I usually use the most current up to date version of visual studio.
- I currently build X360GameHack on dot net v4.8.1 so you'll need to have that package. 
(https://dotnet.microsoft.com/en-us/download/dotnet-framework/thank-you/net481-developer-pack-offline-installer)
- As far as I'm aware you shouldn't need to add any additional packages manually or anything to build it.
(Please fork this repository if you make an edit instead of a new application and please give credit a lot of time was spent here to give it away/back to the scene..)

Credits:
----------------------------------------------------
- Xorloser: XEXTOOL, X360PKGTool
- XboxDevOrganization: XISO
- Integrated Tools: Respect and credit to the original creators of ISO2GOD, Xbox Image Browser, Xbox Backup Creator, and others included in the X360GameHack folder.
- GUI Developer: IcyModz420.

X360GameHack Developer Contact:
----------------------------------------------------
- Discord: IcyModz420#3071
- YouTube: IcyModz420
- Console Crunch: IcyModz420
- se7insins: AnonSec
- RIP TheTechGame: IcyModzXeX
