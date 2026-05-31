using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace PowderCoatingWizard.Blazor.Server.Services
{
    /// <summary>
    /// A no-op <see cref="IChatClient"/> returned when no AI provider is configured.
    /// Prevents DI resolution failures; the chat UI shows a configuration hint instead.
    /// </summary>
    internal sealed class NoOpChatClient : IChatClient
    {
        private const string NotConfiguredMessage =
            "⚠️ AI provider is not configured. Please open AI → AI Provider Settings.";

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var msg = new ChatMessage(ChatRole.Assistant, NotConfiguredMessage);
            return Task.FromResult(new ChatResponse([msg]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, NotConfiguredMessage);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? key = null) => null;

        public void Dispose() { }
    }
}
