
using System.Collections.Generic;

namespace AISmart.Agent;

public static class LLMTypesConstant
{
    public const string AzureOpenAI = "azure_openai";
    public const string Bedrock = "bedrock";
    public const string GoogleGemini = "google_gemini";
    public const string DeepSeek = "deepseek";

    public static readonly List<string> AllSupportedLlmTypes = new List<string>
    {
        AzureOpenAI,
        Bedrock,
        GoogleGemini,
        DeepSeek,
    };
}