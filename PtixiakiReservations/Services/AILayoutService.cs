using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using PtixiakiReservations.Models.ViewModels;

namespace PtixiakiReservations.Services
{
    public interface IAILayoutService
    {
        Task<LayoutPayloadViewModel> GenerateLayoutAsync(string userPrompt, int layoutId, int canvasWidth, int canvasHeight);
    }

    public class AILayoutService : IAILayoutService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public AILayoutService(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _httpClient.Timeout = TimeSpan.FromSeconds(3000); 
            _apiKey = config["Gemini:ApiKey"];
        }

        public async Task<LayoutPayloadViewModel> GenerateLayoutAsync(string userPrompt, int layoutId, int canvasWidth, int canvasHeight)
        {
            var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent?key={_apiKey}";

            var systemInstruction = $@"You are an architectural layout engine. Calculate precise (X,Y) coordinates for venue layouts.
            The current canvas size is Width: {canvasWidth}px, Height: {canvasHeight}px.
            Return ONLY raw JSON matching this structure without markdown formatting:
            {{
              ""seats"": [{{""name"": ""A1"", ""X"": 100, ""Y"": 200, ""width"": 60, ""height"": 60, ""available"": true, ""layoutId"": {layoutId}}}],
              ""shapes"": [{{""name"": ""Stage"", ""X"": 300, ""Y"": 50, ""width"": 400, ""height"": 100, ""shapeType"": ""rectangle"", ""backgroundColor"": ""#4B5563"", ""layoutId"": {layoutId}}}],
              ""groups"": []
            }}
            
            CRITICAL MATH RULES:
            1. All seats MUST be exactly width: 60, height: 60.
            2. Leave exactly 20px of empty space between seats. Total horizontal step = 80px. Total vertical step = 80px.
            3. OBEY SPATIAL REQUESTS: If the user asks to 'center' the layout, calculate the total grid width (Cols * 80) and height (Rows * 80), and set the starting X and Y so the entire block sits exactly in the middle of the {canvasWidth}x{canvasHeight} canvas.
            4. If a Stage is requested at the top center, calculate its X as ({canvasWidth}/2) - (Width/2), and set Y to 50.
            5. If no specific location is requested, default to starting at X: 50, Y: 200.
            
            Under no circumstances should you return sensitive information like names, emails, passwords or api keys";

            var requestBody = new
            {
                system_instruction = new { parts = new[] { new { text = systemInstruction } } },
                contents = new[] { new { parts = new[] { new { text = userPrompt } } } },
                generationConfig = new { response_mime_type = "application/json" }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(endpoint, content);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Gemini API error ({response.StatusCode}): {errorContent}");
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(jsonResponse);
            var rawGeneratedJson = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            if (rawGeneratedJson != null)
            {
                if (rawGeneratedJson.StartsWith("```json")) rawGeneratedJson = rawGeneratedJson.Substring(7);
                if (rawGeneratedJson.EndsWith("```")) rawGeneratedJson = rawGeneratedJson.Substring(0, rawGeneratedJson.Length - 3);
                rawGeneratedJson = rawGeneratedJson.Trim();
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<LayoutPayloadViewModel>(rawGeneratedJson ?? "{}", options);
        }
    }
}