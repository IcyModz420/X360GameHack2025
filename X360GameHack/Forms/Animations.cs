using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace X360GameHack
{
    class Animations
    {
        public async void StartButtonAnimation(Button button, int changeSpeed)
        {
            await Task.Run(async () =>
            {
                string[] textSequence = { "Donate", "Donat", "Dona", "Don", "Do", "D", "Do", "Don", "Dona", "Donat", "Donate", "", "Donate", "", "Donate" };
                int[] sleepDurations = { changeSpeed, changeSpeed, changeSpeed, changeSpeed, changeSpeed, changeSpeed, changeSpeed, changeSpeed, changeSpeed, changeSpeed, changeSpeed, 1000, 1000, 1000, 5000 };

                while (true) // Infinite loop
                {
                    for (int i = 0; i < textSequence.Length; i++)
                    {
                        // Update UI on the UI thread
                        if (button.InvokeRequired)
                        {
                            button.Invoke(new Action(() => button.Text = textSequence[i]));
                        }
                        else
                        {
                            button.Text = textSequence[i];
                        }

                        await Task.Delay(sleepDurations[i]);
                    }
                }
            });
        }

        public async void StartBuyRGH(Button button, int changeSpeed)
        {
            await Task.Run(async () =>
            {
                string[] textSequence = { "Buy RGH", "    RGH", "Buy    ", "    RGH", "Buy    ", "Buy RGH", "Buy RGH", "    RGH", "Buy    ", "    RGH", "Buy    ", "Buy RGH" };
                int[] sleepDurations = { changeSpeed, changeSpeed, changeSpeed, changeSpeed, changeSpeed, changeSpeed, changeSpeed, changeSpeed, changeSpeed, changeSpeed, changeSpeed, 1000, 1000, 1000, 5000 };

                while (true) // Infinite loop
                {
                    for (int i = 0; i < textSequence.Length; i++)
                    {
                        // Update UI on the UI thread
                        if (button.InvokeRequired)
                        {
                            button.Invoke(new Action(() => button.Text = textSequence[i]));
                        }
                        else
                        {
                            button.Text = textSequence[i];
                        }

                        await Task.Delay(sleepDurations[i]);
                    }
                }
            });
        }






    }
}
