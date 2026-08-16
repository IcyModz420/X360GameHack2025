using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using X360GameHack.Security;
using System.Windows.Forms;

namespace X360GameHack.Core.Other
{
    public class GeminiClient
    {
        public static string WhyWouldYouTho { get; set; }
        public static string LatestResponce { get; private set; } = "";

        private static readonly HttpClient _httpClient = new HttpClient();

        public static async Task AskGemini(string question, bool TrainForMessageBoxOutput)
        {
            Pastebin pb = new Pastebin();
            CastleAES DaCastle = new CastleAES();
            if (TrainForMessageBoxOutput)
            {
                question = question + " that was the question from the user this is a message from the system your implimented in: Your responce will be outputed in the bottom part of a c# message box so format the responce to the question to fit in a message box dont mention code, c#, etc, only format the responce to fit in the bottom part of a c# message box so that when i do show your responce in my program it will fit and not overhang or cutoff any text when i show the responce your going to send to the question in the bottom part of a c# message box. You will not show a title so do not worry about that in the responce. Your responce is going into the bottom part of a message box only and dont mention this any of this extra info to the user only answer their question with this additional formatting in the responce. Keep it to 10-15 sentences max. Even if they ask in another launguage.";
            }
            if (string.IsNullOrWhiteSpace(pb.GetYYY()))
            {
                throw new InvalidOperationException("API key could not be grabbed.");
            }
            try
            {
                var requestBody = new
                {
                    contents = new[]
                    {
                    new { parts = new[] { new { text = question } } }
                }
                };
                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
               // Messagebox.Show(DaCastle.DecryptString(pb.GetYYY(), pb.GetZZZ());
                var request = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={DaCastle.DecryptString(pb.GetYYY(), pb.GetZZZ())}")
                {
                    Content = content
                };
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
                var responseJson = await response.Content.ReadAsStringAsync();
                var responseObject = JsonConvert.DeserializeObject<GeminiResponse>(responseJson);
                LatestResponce = responseObject?.Candidates[0]?.Content?.Parts[0]?.Text ?? "No answer received.";
            }
            catch (Exception ex)
            {
                LatestResponce = $"Error: {ex.Message}";
            }
        }
    }

    public class GeminiResponse
    {
        [JsonProperty("candidates")]
        public Candidate[] Candidates { get; set; }
    }

    public class Candidate
    {
        [JsonProperty("content")]
        public Content Content { get; set; }
    }

    public class Content
    {
        [JsonProperty("parts")]
        public Part[] Parts { get; set; }
    }

    public class Part
    {
        [JsonProperty("text")]
        public string Text { get; set; }
    }
}
