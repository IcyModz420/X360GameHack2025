using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using X360GameHack.Core.Net;
using X360GameHack.Core.Services;

namespace X360GameHack.UI.Pages
{
    /// <summary>
    /// The things plain XBDM cannot do, through the JRPC2 plugin: pop-up notifications with any of the console's
    /// pictures, the temperature sensors and the ring-of-light LEDs. Nothing else in the program uses JRPC2.
    /// </summary>
    public sealed class JrpcPage : PageBase
    {
        public override string Title => "Notify & Sensors";
        public override string Subtitle => "Pop-up notifications, temperatures and the ring of light: the things plain XBDM cannot do. They need the JRPC2 or XRPC plugin.";
        public override string PageGlyph => Glyph.Send;

        private readonly XTextBox _text = new XTextBox("Text to show on the console, e.g. Hello from X360GameHack");
        private readonly ListView _icons = ListStyle.Create(("No.", 56), ("Picture", 420));
        private readonly XTextBox _iconFilter = new XTextBox("Filter pictures - for example achievement, party, download", false, true);
        private readonly KvGrid _temps = new KvGrid();
        private readonly XCheck _watch = new XCheck("Read every 5 seconds", false, "Keeps reading the sensors while this page is open.");
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 5000 };
        private readonly XCombo[] _led = new XCombo[4];
        private CancellationTokenSource _tour;
        private bool _reading;

        public JrpcPage()
        {
            Body.Add(new ConnectBar());
            Note(Body, "XBDM has no command for these, so they go through a plugin: JRPC2.xex if it is loaded, otherwise XRPC.xex (notifications only). Add a plugin with the INI Editor. Everything else in this program works over plain XBDM.", Theme.TextDim);

            var cols = AddColumns(2, 440);

            // ---------------------------------------------------------------- notification
            var notify = AddCard("Notification", "Shows a pop-up on the console, with the picture you pick. " + Jrpc.Icons.Count + " pictures.", Glyph.Send, cols);
            Field(notify, "Text", _text);
            Field(notify, "Picture", _iconFilter);
            _icons.Height = Theme.S(300);
            _icons.MultiSelect = false;
            notify.Add(_icons);
            Buttons(notify,
                Btn("Send", Glyph.Send, SendNotify, BtnKind.Primary, "Show this text with the selected picture"),
                Btn("Show every picture", Glyph.Play, TourIcons, tip: "Sends one notification per picture, each saying its number and name, a few seconds apart"),
                Btn("Stop", Glyph.Cancel, () => _tour?.Cancel()));
            _iconFilter.TextChanged += (s, e) => FillIcons();
            _icons.DoubleClick += (s, e) => SendNotify();
            _text.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SendNotify(); } };
            FillIcons();

            // ---------------------------------------------------------------- temperatures
            var right = new VStack { AutoHeight = true, Spacing = Body.Spacing, Padding = new Padding(0) };
            cols.Controls.Add(right);
            var temps = AddCard("Temperatures", "The console's own sensors, in degrees Celsius.", Glyph.Info, right);
            temps.Add(_temps);
            Buttons(temps, Btn("Read", Glyph.Refresh, () => ReadTemps(true), BtnKind.Primary));
            temps.Add(_watch);
            _watch.CheckedChanged += (s, e) => { if (_watch.Checked) { ReadTemps(true); } else _timer.Stop(); };
            _timer.Tick += (s, e) => { if (Visible && ConsoleService.IsConnected) ReadTemps(false); else { _timer.Stop(); _watch.Checked = false; } };
            foreach (JrpcSensor s in Enum.GetValues(typeof(JrpcSensor))) _temps.Set(SensorName(s), "-");

            // ---------------------------------------------------------------- ring of light
            var leds = AddCard("Ring of light", "The four lights around the power button.", Glyph.Power, right);
            string[] where = { "Top left", "Top right", "Bottom left", "Bottom right" };
            for (int i = 0; i < 4; i++)
            {
                _led[i] = new XCombo { Width = Theme.S(160) };
                foreach (JrpcLed v in Enum.GetValues(typeof(JrpcLed))) _led[i].Items.Add(v);
                _led[i].SelectedItem = JrpcLed.Green;
                Field(leds, where[i], _led[i]).FillChild = null;
            }
            Buttons(leds,
                Btn("Apply", Glyph.Check, () => SetLeds(null), BtnKind.Primary),
                Btn("All green", null, () => SetLeds(JrpcLed.Green)),
                Btn("All red", null, () => SetLeds(JrpcLed.Red)),
                Btn("All orange", null, () => SetLeds(JrpcLed.Orange)),
                Btn("All off", null, () => SetLeds(JrpcLed.Off)));
            Buttons(leds,
                Btn("Spin", Glyph.Refresh, () => Animate(SpinFrames), tip: "One green light going round"),
                Btn("Alternate", Glyph.Play, () => Animate(AlternateFrames), tip: "Opposite corners swapping"),
                Btn("Flash", Glyph.Flash, () => Animate(FlashFrames), tip: "All four on and off"),
                Btn("Stop animation", Glyph.Cancel, StopAnimation));
            _anim.Tick += (s, e) => AnimationTick();
        }

        // ---------------------------------------------------------------- ring-of-light animations
        // Each frame is top-left, top-right, bottom-left, bottom-right. One frame is sent per tick.
        private static readonly JrpcLed[][] SpinFrames =
        {
            new[] { JrpcLed.Green, JrpcLed.Off, JrpcLed.Off, JrpcLed.Off }, new[] { JrpcLed.Off, JrpcLed.Green, JrpcLed.Off, JrpcLed.Off },
            new[] { JrpcLed.Off, JrpcLed.Off, JrpcLed.Off, JrpcLed.Green }, new[] { JrpcLed.Off, JrpcLed.Off, JrpcLed.Green, JrpcLed.Off },
        };
        private static readonly JrpcLed[][] AlternateFrames =
        {
            new[] { JrpcLed.Green, JrpcLed.Off, JrpcLed.Off, JrpcLed.Green }, new[] { JrpcLed.Off, JrpcLed.Green, JrpcLed.Green, JrpcLed.Off },
        };
        private static readonly JrpcLed[][] FlashFrames =
        {
            new[] { JrpcLed.Green, JrpcLed.Green, JrpcLed.Green, JrpcLed.Green }, new[] { JrpcLed.Off, JrpcLed.Off, JrpcLed.Off, JrpcLed.Off },
        };
        private readonly System.Windows.Forms.Timer _anim = new System.Windows.Forms.Timer { Interval = 450 };
        private JrpcLed[][] _frames;
        private int _frame;
        private bool _sending;

        private void Animate(JrpcLed[][] frames)
        {
            if (!Ready()) return;
            _frames = frames; _frame = 0;
            _anim.Start();
            Log.Info("Ring-of-light animation started. Press Stop animation to end it.");
        }

        private void StopAnimation()
        {
            if (!_anim.Enabled) return;
            _anim.Stop(); _frames = null;
            Log.Info("Ring-of-light animation stopped.");
        }

        private async void AnimationTick()
        {
            if (_sending || _frames == null) return;
            if (!Visible || !ConsoleService.IsConnected) { _anim.Stop(); _frames = null; return; }
            var f = _frames[_frame++ % _frames.Length];
            _sending = true;
            try { await ConsoleService.RunAsync(c => ConsoleExtras.SetLeds(c, f[0], f[1], f[2], f[3])); }
            catch (Exception ex) { _anim.Stop(); _frames = null; Log.Error("Animation stopped: " + ex.Message); }
            finally { _sending = false; }
        }

        private static string SensorName(JrpcSensor s) => s == JrpcSensor.Cpu ? "Processor (CPU)" : s == JrpcSensor.Gpu ? "Graphics (GPU)" : s == JrpcSensor.Memory ? "Memory (EDRAM)" : "Motherboard";

        private void FillIcons()
        {
            string q = _iconFilter.Text.Trim();
            int keep = _icons.SelectedItems.Count > 0 ? ((ConsoleNotifyIcon)_icons.SelectedItems[0].Tag).Id : 0;
            _icons.BeginUpdate();
            _icons.Items.Clear();
            foreach (var icon in Jrpc.Icons.Where(i => q.Length == 0 || i.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || i.Id.ToString() == q))
            {
                var li = new ListViewItem(icon.Id.ToString()) { Tag = icon };
                li.SubItems.Add(icon.Name);
                _icons.Items.Add(li);
                if (icon.Id == keep) li.Selected = true;
            }
            if (_icons.SelectedItems.Count == 0 && _icons.Items.Count > 0) _icons.Items[0].Selected = true;
            _icons.EndUpdate();
        }

        private bool Ready()
        {
            if (!ConsoleService.IsConnected) { Dlg.Info("Not connected", "Connect to the console first (the bar at the top of this page)."); return false; }
            return true;
        }

        private void SendNotify()
        {
            string text = _text.Text.Trim();
            string problem = Jrpc.NotifyTextProblem(text);
            if (problem != null) { Dlg.Warn("Notification", problem); return; }
            if (_icons.SelectedItems.Count == 0) { Dlg.Info("Notification", "Pick a picture in the list."); return; }
            var icon = (ConsoleNotifyIcon)_icons.SelectedItems[0].Tag;
            if (!Ready()) return;
            Run(null, "Sending notification", async () =>
            {
                await ConsoleService.RunAsync(c => ConsoleExtras.Notify(c, text, icon.Id));
                Log.Success("Notification sent through " + ConsoleExtras.Name(ConsoleExtras.LastRoute) + ": \"" + text + "\" with picture " + icon.Id + " (" + icon.Name + ").");
            });
        }

        private void TourIcons()
        {
            if (_tour != null) return;
            if (!Ready()) return;
            _tour = new CancellationTokenSource();
            var ct = _tour.Token;
            var list = _icons.Items.Cast<ListViewItem>().Select(i => (ConsoleNotifyIcon)i.Tag).ToList();
            Run(null, "Showing every picture", async () =>
            {
                try
                {
                    using (Status.Begin("Showing " + list.Count + " pictures on the console", _tour))
                    {
                        int n = 0;
                        foreach (var icon in list)
                        {
                            ct.ThrowIfCancellationRequested();
                            await ConsoleService.RunAsync(c => ConsoleExtras.Notify(c, "Picture " + icon.Id + ": " + icon.Name, icon.Id), ct);
                            n++;
                            await Task.Delay(3500, ct);
                        }
                        Log.Success("Showed " + n + " picture(s).");
                    }
                }
                catch (OperationCanceledException) { Log.Warn("Stopped."); }
                finally { _tour.Dispose(); _tour = null; }
            });
        }

        private void ReadTemps(bool fromButton)
        {
            if (_reading) return;
            if (fromButton && !Ready()) { _watch.Checked = false; return; }
            _reading = true;
            Run(null, "Reading temperatures", async () =>
            {
                try
                {
                    var values = await ConsoleService.RunAsync(c => Enum.GetValues(typeof(JrpcSensor)).Cast<JrpcSensor>().ToDictionary(s => s, s => ConsoleExtras.Temperature(c, s)));
                    foreach (var v in values) _temps.Set(SensorName(v.Key), v.Value + " °C");
                    if (_watch.Checked) _timer.Start();
                    if (fromButton) Log.Info("Temperatures: " + string.Join(", ", values.Select(v => SensorName(v.Key) + " " + v.Value + " °C")));
                }
                catch { _timer.Stop(); _watch.Checked = false; throw; }
                finally { _reading = false; }
            });
        }

        private void SetLeds(JrpcLed? all)
        {
            if (all.HasValue) foreach (var b in _led) b.SelectedItem = all.Value;
            var v = _led.Select(b => (JrpcLed)b.SelectedItem).ToArray();
            if (!Ready()) return;
            Run(null, "Setting the ring of light", async () =>
            {
                await ConsoleService.RunAsync(c => ConsoleExtras.SetLeds(c, v[0], v[1], v[2], v[3]));
                Log.Success("Ring of light set: " + string.Join(", ", v.Select(x => x.ToString())) + ".");
            });
        }

        public override void OnDeactivated() { _timer.Stop(); _watch.Checked = false; _anim.Stop(); _frames = null; }
    }
}
