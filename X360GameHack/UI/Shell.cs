using System;
using System.Drawing;
using System.Windows.Forms;
using X360GameHack.Core.Services;

namespace X360GameHack.UI
{
    /// <summary>What a rebuilt main window restores: page, size and position, log drawer.</summary>
    public sealed class ShellState
    {
        public Type Page;
        public Rectangle Bounds;
        public FormWindowState WindowState;
        public int LogHeight;
    }

    /// <summary>
    /// Owns the main window for the lifetime of the app. Changing the theme rebuilds the window in place
    /// (same page, size, working file and console connection) so every control picks up the new colors.
    /// </summary>
    public sealed class Shell : ApplicationContext
    {
        public static Shell Current { get; private set; }

        public Shell()
        {
            Current = this;
            Theme.Use(ThemePalette.Active());
            MainForm = new MainForm(null);
            MainForm.Show();
        }

        /// <summary>Applies <paramref name="palette"/> by rebuilding the window. Refused while a task is running.</summary>
        public bool ApplyTheme(ThemePalette palette)
        {
            if (Status.IsBusy)
            {
                Dlg.Info("Theme", "Wait for the running task to finish (see the status bar), then apply the theme.");
                return false;
            }
            var old = (MainForm)MainForm;
            var state = old.CaptureState();
            Theme.Use(palette);
            old.Rebuilding = true;
            var fresh = new MainForm(state);
            MainForm = fresh;          // the context now follows the new window; closing the old one does not exit
            fresh.Show();
            old.Close();
            old.Dispose();
            fresh.Activate();
            Log.Success("Theme applied: " + palette.Name);
            return true;
        }
    }
}
