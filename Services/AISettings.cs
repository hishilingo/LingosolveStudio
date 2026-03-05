using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace LingosolveStudio.Services
{
    [DataContract]
    public class AISettings
    {
        private static readonly string SettingsFilePath = Path.Combine(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
            "ai_settings.json");

        [DataMember(Name = "openrouter_api_key")]
        public string OpenRouterApiKey { get; set; } = string.Empty;

        [DataMember(Name = "model_name")]
        public string ModelName { get; set; } = "google/gemini-2.0-flash-001";

        [DataMember(Name = "auto_translation_enabled")]
        public bool AutoTranslationEnabled { get; set; } = false;

        [DataMember(Name = "target_language")]
        public string TargetLanguage { get; set; } = "English";

        [DataMember(Name = "save_output_to_file")]
        public bool SaveOutputToFile { get; set; } = false;

        public static readonly List<string> AvailableLanguages = new List<string>
        {
            "English", "German", "Spanish", "French", "Portuguese", "Italian",
            "Arabic", "Hindi", "Kannada", "Sanskrit", "Vietnamese", "Latin",
            "Hebrew", "Chinese", "Japanese", "Korean", "Russian", "Turkish"
        };

        public bool HasApiKey()
        {
            return !string.IsNullOrWhiteSpace(OpenRouterApiKey);
        }

        public void Save()
        {
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(AISettings));
                using (var stream = new FileStream(SettingsFilePath, FileMode.Create))
                {
                    serializer.WriteObject(stream, this);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving AI settings: {ex.Message}");
            }
        }

        public static AISettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(AISettings));
                    using (var stream = new FileStream(SettingsFilePath, FileMode.Open))
                    {
                        return (AISettings)serializer.ReadObject(stream);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading AI settings: {ex.Message}");
            }
            return new AISettings();
        }
    }

    public class TokenUsageStats
    {
        public int TotalInputTokens { get; set; }
        public int TotalOutputTokens { get; set; }
        public int PagesProcessed { get; set; }

        public void Reset()
        {
            TotalInputTokens = 0;
            TotalOutputTokens = 0;
            PagesProcessed = 0;
        }

        public void AddPageStats(int inputTokens, int outputTokens)
        {
            TotalInputTokens += inputTokens;
            TotalOutputTokens += outputTokens;
            PagesProcessed++;
        }

        public string GetTotalStats()
        {
            return $"Total Tokens - In: {TotalInputTokens:N0} | Out: {TotalOutputTokens:N0} | Pages: {PagesProcessed}";
        }
    }
}
