using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace StickyNotes__
{
    public static class AiHelper
    {
        private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
        private static readonly HttpClient StatusClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        public static string GetOllamaUrl()
        {
            try
            {
                if (System.IO.File.Exists(AppConfig.SettingsPath))
                {
                    string json = System.IO.File.ReadAllText(AppConfig.SettingsPath);
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        if (doc.RootElement.TryGetProperty("OllamaUrl", out JsonElement urlEl))
                        {
                            string url = urlEl.GetString() ?? "";
                            if (!string.IsNullOrEmpty(url)) return url.TrimEnd('/');
                        }
                    }
                }
            }
            catch {}
            return "http://localhost:11434";
        }

        public static string GetOllamaModelSetting()
        {
            try
            {
                if (System.IO.File.Exists(AppConfig.SettingsPath))
                {
                    string json = System.IO.File.ReadAllText(AppConfig.SettingsPath);
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        if (doc.RootElement.TryGetProperty("OllamaModel", out JsonElement modelEl))
                        {
                            string model = modelEl.GetString() ?? "";
                            if (!string.IsNullOrEmpty(model)) return model.Trim();
                        }
                    }
                }
            }
            catch {}
            return "";
        }

        public static async Task<bool> IsOllamaRunningAsync()
        {
            try
            {
                var response = await StatusClient.GetAsync($"{GetOllamaUrl()}/api/tags");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<string> GetDefaultModelAsync()
        {
            string modelSetting = GetOllamaModelSetting();
            if (!string.IsNullOrEmpty(modelSetting))
                return modelSetting;

            try
            {
                var response = await HttpClient.GetAsync($"{GetOllamaUrl()}/api/tags");
                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    using (JsonDocument doc = JsonDocument.Parse(content))
                    {
                        var root = doc.RootElement;
                        if (root.TryGetProperty("models", out JsonElement modelsEl) && modelsEl.GetArrayLength() > 0)
                        {
                            return modelsEl[0].GetProperty("name").GetString() ?? "llama3.2:1b";
                        }
                    }
                }
            }
            catch {}
            return "llama3.2:1b";
        }

        public static async Task<string> GenerateTextAsync(string prompt)
        {
            if (!await IsOllamaRunningAsync())
            {
                return "";
            }

            string model = await GetDefaultModelAsync();
            var payload = new
            {
                model = model,
                prompt = prompt,
                stream = false,
                // Smaller/faster models (e.g. llama3.2:1b) have a modest default context window,
                // which can silently truncate long prompts (many notes) or cut the response off
                // mid-JSON. Raising both caps here gives structured-output callers (Auto Organize,
                // auto-tagging) enough headroom to actually finish.
                options = new { num_predict = 4096, num_ctx = 8192 }
            };

            string jsonPayload = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            try
            {
                var response = await HttpClient.PostAsync($"{GetOllamaUrl()}/api/generate", content);
                if (response.IsSuccessStatusCode)
                {
                    string respString = await response.Content.ReadAsStringAsync();
                    using (JsonDocument doc = JsonDocument.Parse(respString))
                    {
                        if (doc.RootElement.TryGetProperty("response", out JsonElement respEl))
                        {
                            return respEl.GetString()?.Trim() ?? "";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ollama generation error: " + ex.Message);
            }
            return "";
        }

        public static async Task<List<string>> AutoTagTextAsync(string text)
        {
            if (string.IsNullOrEmpty(text))
                return new List<string>();

            string prompt = $"Analyze the following text and generate 3 to 5 simple, lowercase tags representing the topics. Return ONLY a plain JSON string array of tags, for example: [\"database\", \"sql\", \"fix\"]. Do not write any explanations, markdown blocks, or other text. If the text has no clear topics, return an empty array.\n\nText: {text}";
            
            string response = await GenerateTextAsync(prompt);
            if (string.IsNullOrEmpty(response))
                return new List<string>();

            try
            {
                // Clean up markdown markers if any (like ```json ... ```)
                string cleaned = response;
                if (cleaned.Contains("[") && cleaned.Contains("]"))
                {
                    int start = cleaned.IndexOf("[");
                    int end = cleaned.LastIndexOf("]") + 1;
                    cleaned = cleaned.Substring(start, end - start);
                }

                var tags = JsonSerializer.Deserialize<List<string>>(cleaned);
                if (tags != null)
                {
                    return tags;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error parsing AI tags: " + ex.Message + " | Response was: " + response);
            }
            return new List<string>();
        }
    }
}
