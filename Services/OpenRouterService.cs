using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace LingosolveStudio.Services
{
    /// <summary>
    /// Service for OCR and translation via OpenRouter.ai (OpenAI-compatible chat completions API).
    /// Supports vision models for image OCR and text-only for translation.
    /// </summary>
    public class OpenRouterService
    {
        private readonly string apiKey;
        private readonly string model;
        private readonly HttpClient httpClient;
        private const string API_URL = "https://openrouter.ai/api/v1/chat/completions";

        public TokenUsageStats TokenStats { get; private set; }
        private string previousPageContext = string.Empty;

        public OpenRouterService(string apiKey, string model)
        {
            this.apiKey = apiKey;
            this.model = model;
            this.httpClient = new HttpClient();
            this.httpClient.Timeout = TimeSpan.FromMinutes(5);
            this.httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            this.TokenStats = new TokenUsageStats();
        }

        public void ResetPageContext()
        {
            previousPageContext = string.Empty;
        }

        public void ResetTokenStats()
        {
            TokenStats.Reset();
        }

        /// <summary>
        /// Performs OCR on an image using a vision-capable model via OpenRouter.
        /// </summary>
        public async Task<OCRResult> ProcessImageAsync(Image image, string translateTo = null)
        {
            try
            {
                string base64Image = ImageToBase64(image);
                string prompt = BuildOCRPrompt(translateTo);

                string requestBody = BuildVisionRequest(prompt, base64Image);
                string responseJson = await SendRequestAsync(requestBody);

                return ParseResponse(responseJson, translateTo);
            }
            catch (Exception ex)
            {
                return new OCRResult { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// Performs OCR on a PDF page image (byte array).
        /// </summary>
        public async Task<OCRResult> ProcessPdfPageAsync(byte[] imageData, string translateTo = null)
        {
            try
            {
                byte[] optimizedData = OptimizeImageData(imageData);
                string base64Image = Convert.ToBase64String(optimizedData);
                string prompt = BuildOCRPrompt(translateTo);

                string requestBody = BuildVisionRequest(prompt, base64Image);
                string responseJson = await SendRequestAsync(requestBody);

                return ParseResponse(responseJson, translateTo);
            }
            catch (Exception ex)
            {
                return new OCRResult { Success = false, Error = ex.Message };
            }
        }

        /// <summary>
        /// Translates text without OCR (text-only, no image).
        /// </summary>
        public async Task<OCRResult> TranslateTextAsync(string text, string targetLanguage, string sourceLanguage = null)
        {
            try
            {
                string sourceLang = !string.IsNullOrEmpty(sourceLanguage) ? sourceLanguage : "the source language";
                string prompt = $"Act as an expert academic translator. Translate the following text from {sourceLang} to {targetLanguage}.\n\n" +
                    "Translation Directives:\n" +
                    "1. Maintain the original structure and formatting.\n" +
                    "2. Handle philosophical terms (like Brahman, Atman, Jiva, Maya, Moksha) appropriately - transliterate and keep them.\n" +
                    "3. Do not output introductory filler. Just the translation.\n" +
                    "4. Preserve verse numbers and section markers.\n\n" +
                    "TEXT TO TRANSLATE:\n" + text + "\n\n" +
                    "Output ONLY the translation, nothing else.";

                string requestBody = BuildTextRequest(prompt);
                string responseJson = await SendRequestAsync(requestBody);

                return ParseTranslationResponse(responseJson);
            }
            catch (Exception ex)
            {
                return new OCRResult { Success = false, Error = ex.Message };
            }
        }

        private string BuildOCRPrompt(string translateTo)
        {
            string ocrInstructions = "You are a precise OCR system. Your task is to extract ONLY the text that is actually visible in this image.\n\n" +
                "CRITICAL INSTRUCTIONS:\n" +
                "1. Read ONLY what you can clearly see in the image\n" +
                "2. Do NOT repeat the same phrase over and over\n" +
                "3. Do NOT make up content or hallucinate text\n" +
                "4. If you see Sanskrit/Devanagari/Kannada text, transcribe it exactly as shown\n" +
                "5. If you see English text, transcribe it exactly as shown\n" +
                "6. Preserve line breaks, spacing, and paragraph structure\n" +
                "7. If text is unclear, use [unclear] instead of guessing\n" +
                "8. If a page appears blank or mostly blank, respond with [Blank page]\n" +
                "9. Do NOT add your own interpretations or explanations\n" +
                "10. Stop if you find yourself repeating the same words";

            if (!string.IsNullOrEmpty(translateTo) && translateTo != "None")
            {
                string contextNote = string.Empty;
                if (!string.IsNullOrEmpty(previousPageContext))
                {
                    contextNote = $"\n\nCONTEXT FROM PREVIOUS PAGE (for continuity - do not include this in output):\n\"{previousPageContext}\"\n\n" +
                                 "If the current page starts mid-sentence, complete the thought naturally in the translation.";
                }

                string translationDirectives = $"\n\nAct as an expert academic translator. Translate the extracted text to {translateTo}.\n\n" +
                    "Translation Directives:\n" +
                    "1. Maintain the original numbering and structure.\n" +
                    "2. Handle philosophical terms appropriately - transliterate and keep them.\n" +
                    "3. Do not output introductory filler. Just the translation.\n" +
                    "4. Preserve verse numbers and section markers.\n" +
                    "5. If text ends mid-sentence, translate what is visible." +
                    contextNote;

                return ocrInstructions + translationDirectives +
                    "\n\nOutput ONLY in this exact format:\n" +
                    "=== ORIGINAL TEXT ===\n[the extracted text]\n\n" +
                    $"=== TRANSLATION ({translateTo}) ===\n[the translation]";
            }

            return ocrInstructions + "\n\nOutput ONLY the extracted text, nothing else.";
        }

        /// <summary>
        /// Build OpenAI-compatible vision request with base64 image.
        /// </summary>
        private string BuildVisionRequest(string prompt, string base64Image)
        {
            // OpenAI vision format: content is an array with text and image_url parts
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"model\":\"" + EscapeJson(model) + "\",");
            sb.Append("\"messages\":[{");
            sb.Append("\"role\":\"user\",");
            sb.Append("\"content\":[");
            sb.Append("{\"type\":\"text\",\"text\":\"" + EscapeJson(prompt) + "\"},");
            sb.Append("{\"type\":\"image_url\",\"image_url\":{");
            sb.Append("\"url\":\"data:image/jpeg;base64," + base64Image + "\"");
            sb.Append("}}");
            sb.Append("]");
            sb.Append("}],");
            sb.Append("\"max_tokens\":8192,");
            sb.Append("\"temperature\":0.1");
            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// Build text-only request for translation.
        /// </summary>
        private string BuildTextRequest(string prompt)
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"model\":\"" + EscapeJson(model) + "\",");
            sb.Append("\"messages\":[{");
            sb.Append("\"role\":\"user\",");
            sb.Append("\"content\":\"" + EscapeJson(prompt) + "\"");
            sb.Append("}],");
            sb.Append("\"max_tokens\":8192,");
            sb.Append("\"temperature\":0.1");
            sb.Append("}");
            return sb.ToString();
        }

        private async Task<string> SendRequestAsync(string requestBody)
        {
            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(API_URL, content);

            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"OpenRouter API error: {response.StatusCode} - {errorBody}");
            }

            return await response.Content.ReadAsStringAsync();
        }

        private OCRResult ParseResponse(string responseJson, string translateTo)
        {
            var result = new OCRResult { Success = true };

            try
            {
                string extractedText = ExtractContentFromResponse(responseJson);
                if (string.IsNullOrEmpty(extractedText))
                {
                    result.Success = false;
                    result.Error = "No text content in response";
                    return result;
                }

                if (!string.IsNullOrEmpty(translateTo) && translateTo != "None")
                {
                    int origStart = extractedText.IndexOf("=== ORIGINAL TEXT ===", StringComparison.OrdinalIgnoreCase);
                    int transStart = extractedText.IndexOf("=== TRANSLATION", StringComparison.OrdinalIgnoreCase);

                    if (origStart >= 0 && transStart > origStart)
                    {
                        result.OriginalText = extractedText.Substring(origStart + 21, transStart - origStart - 21).Trim();
                        result.TranslatedText = extractedText.Substring(transStart).Trim();
                        int newlinePos = result.TranslatedText.IndexOf('\n');
                        if (newlinePos > 0)
                        {
                            result.TranslatedText = result.TranslatedText.Substring(newlinePos + 1).Trim();
                        }
                    }
                    else if (origStart >= 0)
                    {
                        result.OriginalText = extractedText.Substring(origStart + 21).Trim();
                    }
                    else
                    {
                        result.OriginalText = extractedText;
                    }
                }
                else
                {
                    result.OriginalText = extractedText;
                }

                // Parse token usage from response
                ParseTokenUsage(responseJson, result);
                TokenStats.AddPageStats(result.InputTokens, result.OutputTokens);

                // Save context for next page
                if (!string.IsNullOrEmpty(result.OriginalText) && result.OriginalText.Length > 50)
                {
                    int contextLength = Math.Min(200, result.OriginalText.Length);
                    previousPageContext = result.OriginalText.Substring(result.OriginalText.Length - contextLength).Trim();
                }
                else
                {
                    previousPageContext = string.Empty;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = $"Error parsing response: {ex.Message}";
            }

            return result;
        }

        private OCRResult ParseTranslationResponse(string responseJson)
        {
            var result = new OCRResult { Success = true };

            try
            {
                string extractedText = ExtractContentFromResponse(responseJson);
                result.TranslatedText = extractedText;
                ParseTokenUsage(responseJson, result);
                TokenStats.AddPageStats(result.InputTokens, result.OutputTokens);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = $"Error parsing response: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Extracts the text content from an OpenAI-compatible chat completion response.
        /// Looks for choices[0].message.content
        /// </summary>
        private string ExtractContentFromResponse(string json)
        {
            // Find "content" inside "message" inside "choices"
            int choicesIdx = json.IndexOf("\"choices\"");
            if (choicesIdx < 0) return null;

            int messageIdx = json.IndexOf("\"message\"", choicesIdx);
            if (messageIdx < 0) return null;

            int contentIdx = json.IndexOf("\"content\"", messageIdx);
            if (contentIdx < 0) return null;

            int colonIdx = json.IndexOf(":", contentIdx + 9);
            if (colonIdx < 0) return null;

            // Skip whitespace
            int i = colonIdx + 1;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\n' || json[i] == '\r' || json[i] == '\t'))
                i++;

            if (i >= json.Length) return null;

            // Handle null content
            if (json.Substring(i, Math.Min(4, json.Length - i)) == "null")
                return null;

            if (json[i] != '"') return null;

            // Extract string value
            i++; // skip opening quote
            var sb = new StringBuilder();
            while (i < json.Length)
            {
                if (json[i] == '\\' && i + 1 < json.Length)
                {
                    char next = json[i + 1];
                    switch (next)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        default: sb.Append(next); break;
                    }
                    i += 2;
                }
                else if (json[i] == '"')
                {
                    break;
                }
                else
                {
                    sb.Append(json[i]);
                    i++;
                }
            }

            return sb.ToString();
        }

        private void ParseTokenUsage(string json, OCRResult result)
        {
            // Look for "usage" object with prompt_tokens and completion_tokens (OpenAI format)
            int usageIdx = json.IndexOf("\"usage\"");
            if (usageIdx < 0) return;

            int promptIdx = json.IndexOf("\"prompt_tokens\"", usageIdx);
            if (promptIdx >= 0)
            {
                int num = ParseIntAfterColon(json, promptIdx + 15);
                result.InputTokens = num;
            }

            int completionIdx = json.IndexOf("\"completion_tokens\"", usageIdx);
            if (completionIdx >= 0)
            {
                int num = ParseIntAfterColon(json, completionIdx + 19);
                result.OutputTokens = num;
            }
        }

        private int ParseIntAfterColon(string json, int startIdx)
        {
            int colonIdx = json.IndexOf(":", startIdx);
            if (colonIdx < 0) return 0;

            int i = colonIdx + 1;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t'))
                i++;

            var sb = new StringBuilder();
            while (i < json.Length && char.IsDigit(json[i]))
            {
                sb.Append(json[i]);
                i++;
            }

            int.TryParse(sb.ToString(), out int val);
            return val;
        }

        private string ImageToBase64(Image image)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                var jpegEncoder = GetEncoder(ImageFormat.Jpeg);
                var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
                image.Save(ms, jpegEncoder, encoderParams);
                return Convert.ToBase64String(ms.ToArray());
            }
        }

        private byte[] OptimizeImageData(byte[] imageData)
        {
            using (var ms = new MemoryStream(imageData))
            using (var image = Image.FromStream(ms))
            {
                int maxDimension = 3500;
                int width = image.Width;
                int height = image.Height;

                if (width > maxDimension || height > maxDimension)
                {
                    float scale = Math.Min((float)maxDimension / width, (float)maxDimension / height);
                    width = (int)(width * scale);
                    height = (int)(height * scale);

                    using (var resized = new Bitmap(width, height))
                    using (var graphics = Graphics.FromImage(resized))
                    {
                        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(image, 0, 0, width, height);

                        using (var outMs = new MemoryStream())
                        {
                            var jpegEncoder = GetEncoder(ImageFormat.Jpeg);
                            var encoderParams = new EncoderParameters(1);
                            encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
                            resized.Save(outMs, jpegEncoder, encoderParams);
                            return outMs.ToArray();
                        }
                    }
                }
                else
                {
                    using (var outMs = new MemoryStream())
                    {
                        var jpegEncoder = GetEncoder(ImageFormat.Jpeg);
                        var encoderParams = new EncoderParameters(1);
                        encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
                        image.Save(outMs, jpegEncoder, encoderParams);
                        return outMs.ToArray();
                    }
                }
            }
        }

        private ImageCodecInfo GetEncoder(ImageFormat format)
        {
            var codecs = ImageCodecInfo.GetImageDecoders();
            foreach (var codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                    return codec;
            }
            return null;
        }

        private string EscapeJson(string str)
        {
            if (string.IsNullOrEmpty(str)) return str;
            return str
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }

    /// <summary>
    /// Result from OCR/translation processing.
    /// </summary>
    public class OCRResult
    {
        public bool Success { get; set; }
        public string OriginalText { get; set; }
        public string TranslatedText { get; set; }
        public string Error { get; set; }
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
    }
}
