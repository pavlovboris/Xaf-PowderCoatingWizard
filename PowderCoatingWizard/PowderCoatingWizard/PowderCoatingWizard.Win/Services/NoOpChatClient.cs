using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace PowderCoatingWizard.Win.Services
{
    /// <summary>
    /// A no-op <see cref="IChatClient"/> used as a DI placeholder at startup,
    /// before the real provider is loaded from the database after login.
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
