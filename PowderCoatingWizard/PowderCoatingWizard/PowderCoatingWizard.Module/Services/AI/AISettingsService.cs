using Azure.AI.OpenAI;
using DevExpress.ExpressApp;
using Microsoft.Extensions.AI;
using OpenAI;
using PowderCoatingWizard.Module.BusinessObjects.AI;

namespace PowderCoatingWizard.Module.Services.AI
{
    /// <summary>
    /// Loads the active <see cref="AIProviderSettings"/> from the database and
    /// builds the corresponding <see cref="IChatClient"/> instance.
    /// </summary>
    public class AISettingsService
    {
        private readonly INonSecuredObjectSpaceFactory _objectSpaceFactory;

        public AISettingsService(INonSecuredObjectSpaceFactory objectSpaceFactory)
        {
            _objectSpaceFactory = objectSpaceFactory;
        }

        /// <summary>Loads the first enabled AIProviderSettings record.</summary>
        public AISettingsDto? LoadSettings()
        {
            using var os = _objectSpaceFactory.CreateNonSecuredObjectSpace(typeof(AIProviderSettings));
            var settings = os.FirstOrDefault<AIProviderSettings>(s => s.IsEnabled);
            if (settings == null) return null;

            // Copy values out before ObjectSpace is disposed.
            return new AISettingsDto
            {
                DisplayName     = settings.DisplayName,
                ProviderType    = settings.ProviderType,
                Endpoint        = settings.Endpoint,
                ApiKeyEncrypted = settings.ApiKeyEncrypted,
                ModelId         = settings.ModelId,
                EmbeddingModelId = settings.EmbeddingModelId,
                IsEnabled       = settings.IsEnabled,
                OpenAIVectorStoreEnabled = settings.OpenAIVectorStoreEnabled,
                OpenAIVectorStoreId = settings.OpenAIVectorStoreId,
                OpenAIVectorStoreMaxResults = settings.OpenAIVectorStoreMaxResults
            };
        }

        /// <summary>Builds an <see cref="IChatClient"/> from the supplied settings.</summary>
        public static IChatClient? BuildChatClient(AISettingsDto? settings)
        {
            if (settings == null || !settings.IsEnabled) return null;

            string apiKey = AICredentialProtector.Decrypt(settings.ApiKeyEncrypted ?? string.Empty);
            string modelId = settings.ModelId ?? "gpt-4o-mini";

            return settings.ProviderType switch
            {
                AIProviderType.AzureOpenAI => BuildAzureOpenAI(settings.Endpoint ?? string.Empty, apiKey, modelId),
                AIProviderType.OpenAI => BuildOpenAI(apiKey, modelId),
                AIProviderType.Ollama => BuildOllama(settings.Endpoint ?? string.Empty, modelId),
                _ => null
            };
        }

        // ── Provider builders ────────────────────────────────────────────────

        private static IChatClient BuildAzureOpenAI(string endpoint, string apiKey, string modelId)
        {
            var client = new AzureOpenAIClient(
                new Uri(endpoint),
                new System.ClientModel.ApiKeyCredential(apiKey));
            return client.GetChatClient(modelId).AsIChatClient();
        }

        private static IChatClient BuildOpenAI(string apiKey, string modelId)
        {
            var client = new OpenAIClient(
                new System.ClientModel.ApiKeyCredential(apiKey));
            return client.GetChatClient(modelId).AsIChatClient();
        }

        private static IChatClient BuildOllama(string endpoint, string modelId)
        {
            // OllamaSharp 5.x: OllamaApiClient implements IChatClient directly.
            var uri = new Uri(string.IsNullOrWhiteSpace(endpoint) ? "http://localhost:11434" : endpoint);
            var ollama = new OllamaSharp.OllamaApiClient(uri) { SelectedModel = modelId };
            return (IChatClient)ollama;
        }

        // ── Embedding generator ──────────────────────────────────────────────

        /// <summary>
        /// Builds an <see cref="IEmbeddingGenerator{String,Embedding}"/> from the supplied settings.
        /// Returns null when the provider/model does not support embeddings (e.g. Ollama without a
        /// dedicated embedding model) or when settings are absent.
        /// </summary>
        public static IEmbeddingGenerator<string, Embedding<float>>? BuildEmbeddingGenerator(AISettingsDto? settings)
        {
            if (settings == null || !settings.IsEnabled) return null;

            string apiKey = AICredentialProtector.Decrypt(settings.ApiKeyEncrypted ?? string.Empty);
            // Use a dedicated embedding model when provided, otherwise fall back to a sensible default.
            string embModel = settings.EmbeddingModelId
                              ?? (settings.ProviderType == AIProviderType.AzureOpenAI
                                  ? "text-embedding-3-small"
                                  : "text-embedding-3-small");

            try
            {
                return settings.ProviderType switch
                {
                    AIProviderType.AzureOpenAI => new AzureOpenAIClient(
                        new Uri(settings.Endpoint ?? string.Empty),
                        new System.ClientModel.ApiKeyCredential(apiKey))
                        .GetEmbeddingClient(embModel)
                        .AsIEmbeddingGenerator(),
                    AIProviderType.OpenAI => new OpenAIClient(
                        new System.ClientModel.ApiKeyCredential(apiKey))
                        .GetEmbeddingClient(embModel)
                        .AsIEmbeddingGenerator(),
                    _ => null   // Ollama: user must configure separately if needed
                };
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>Plain data container used to transport AI settings out of a disposed ObjectSpace.</summary>
    public sealed class AISettingsDto
    {
        public string? DisplayName      { get; init; }
        public AIProviderType ProviderType { get; init; }
        public string? Endpoint         { get; init; }
        public string? ApiKeyEncrypted  { get; init; }
        public string? ModelId          { get; init; }
        /// <summary>Optional dedicated embedding model ID (e.g. "text-embedding-3-small").</summary>
        public string? EmbeddingModelId { get; init; }
        public bool    IsEnabled        { get; init; }
        public bool OpenAIVectorStoreEnabled { get; init; }
        public string? OpenAIVectorStoreId { get; init; }
        public int OpenAIVectorStoreMaxResults { get; init; }
    }
}
