using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace PowderCoatingWizard.Win.Services
{
    /// <summary>
    /// Diagnostic wrapper around any <see cref="IChatClient"/>.
    /// Catches every exception thrown during chat calls and shows a MessageBox
    /// with the full error detail so it is visible even when the caller
    /// (e.g. AIChatControl's Blazor WebView) swallows the exception.
    /// Remove or replace with a no-op pass-through once the issue is identified.
    /// </summary>
    internal sealed class DiagnosticChatClient : IChatClient
    {
        private readonly IChatClient _inner;

        public DiagnosticChatClient(IChatClient inner)
        {
            _inner = inner;
        }

        public ChatClientMetadata Metadata => new ChatClientMetadata();

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await _inner.GetResponseAsync(chatMessages, options, cancellationToken);
            }
            catch (Exception ex)
            {
                ShowError(nameof(GetResponseAsync), ex);
                throw;
            }
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            IAsyncEnumerator<ChatResponseUpdate> enumerator;
            try
            {
                enumerator = _inner.GetStreamingResponseAsync(chatMessages, options, cancellationToken)
                                   .GetAsyncEnumerator(cancellationToken);
            }
            catch (Exception ex)
            {
                ShowError(nameof(GetStreamingResponseAsync) + " (init)", ex);
                throw;
            }

            while (true)
            {
                ChatResponseUpdate update;
                try
                {
                    if (!await enumerator.MoveNextAsync()) break;
                    update = enumerator.Current;
                }
                catch (Exception ex)
                {
                    ShowError(nameof(GetStreamingResponseAsync) + " (stream)", ex);
                    throw;
                }
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? key = null)
            => _inner.GetService(serviceType, key);

        public void Dispose() => _inner.Dispose();

        // ── helpers ───────────────────────────────────────────────────────────

        private static void ShowError(string method, Exception ex)
        {
            DevExpress.Persistent.Base.Tracing.Tracer.LogError(ex);

            string msg = BuildMessage(method, ex);

            // Show on UI thread — the call may arrive from a background continuation.
            if (System.Windows.Forms.Application.OpenForms.Count > 0)
            {
                var form = System.Windows.Forms.Application.OpenForms[0];
                if (form.InvokeRequired)
                    form.BeginInvoke(() => ShowBox(msg));
                else
                    ShowBox(msg);
            }
        }

        private static void ShowBox(string msg) =>
            DevExpress.XtraEditors.XtraMessageBox.Show(
                msg,
                "AI Chat Error",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Error);

        private static string BuildMessage(string method, Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Method : {method}");
            sb.AppendLine();

            var current = ex;
            int depth = 0;
            while (current != null && depth < 5)
            {
                sb.AppendLine($"[{current.GetType().Name}]");
                sb.AppendLine(current.Message);
                if (current.InnerException != null)
                    sb.AppendLine("--- Inner ---");
                current = current.InnerException;
                depth++;
            }
            return sb.ToString();
        }
    }
}
