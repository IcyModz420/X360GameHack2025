using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using X360GameHack.Core.Other;

namespace X360GameHack.Core.Xbox_360
{
    internal class STFSInstaller
    {
        public static async void InstallSTFSUSB(string ContainerLocation)
        {
            try
            {
                if (Properties.Settings.Default.SelectedUSBLetter != "")
                {
                    var pkg = new Xbox360PkgParser();
                    pkg.GetPkgInfo(ContainerLocation);
                    string ContainerName = Path.GetFileName(ContainerLocation);// get folder name of pkg only
                    if (!Directory.Exists(Properties.Settings.Default.SelectedUSBLetter + "Content\\" + pkg.InstallDir)) //check for just install folder directory
                    {                    
                         Directory.CreateDirectory(Properties.Settings.Default.SelectedUSBLetter + "Content\\" + pkg.InstallDir);// create path if needed
                         X360GameHack.CurrentInstance.UpdateListboxForOutput("X360GameHack: Filepath " + "Content\\" + pkg.InstallDir + " created on USB volume " + Properties.Settings.Default.SelectedUSBLetter);
                    }
                    File.Copy(ContainerLocation, Properties.Settings.Default.SelectedUSBLetter + "Content\\" + pkg.InstallDir + "\\" + ContainerName, true); //overwrite if exists
                    if (File.Exists(Properties.Settings.Default.SelectedUSBLetter + "Content\\" + pkg.InstallDir + "\\" + ContainerName)) //check if copy was successful
                    {
                        X360GameHack.CurrentInstance.UpdateListboxForOutput("X360GameHack: File " + ContainerName + " successfully copied to " + Properties.Settings.Default.SelectedUSBLetter + "Content\\" + pkg.InstallDir + "!");
                        return;
                    }
                    else
                    {
                        X360GameHack.CurrentInstance.UpdateListboxForOutput("X360GameHack: ERROR! File " + ContainerName + " could not overwritten/copied to " + Properties.Settings.Default.SelectedUSBLetter + "Content\\" + pkg.InstallDir + "! Is another program using it..?");
                        return;
                    }
                }
                else
                {
                    MessageBox.Show("Drive letter not selected and set in settings tab!", "You must select a USB to use this!");
                    return;
                }
            }
            catch
            {
                MessageBox.Show("Attempt to copy files failed!");
                return;
            }
        }
    }
}