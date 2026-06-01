using Microsoft.Extensions.AI;

namespace PowderCoatingWizard.Win.Services
{
    public sealed class ToolIndicatorChatClient : DelegatingChatClient
    {
        public ToolIndicatorChatClient(IChatClient inner, Action<string> onToolStart, Action onToolEnd)
            : base(inner)
        {
            var fic = inner.GetService<FunctionInvokingChatClient>();
            if (fic != null)
            {
                AILogger.LogEvent("TOOL:INIT", $"FunctionInvokingChatClient found — hooking FunctionInvoker");
                var original = fic.FunctionInvoker;
                fic.FunctionInvoker = async (ctx, ct) =>
                {
                    AILogger.LogEvent("TOOL", $"Invoking tool: {ctx.Function.Name}  args={System.Text.Json.JsonSerializer.Serialize(ctx.Arguments)}");
                    onToolStart(ctx.Function.Name);
                    try
                    {
                        var result = original != null ? await original(ctx, ct) : await ctx.Function.InvokeAsync(ctx.Arguments, ct);
                        AILogger.LogEvent("TOOL", $"Tool {ctx.Function.Name} completed");
                        return result;
                    }
                    catch (Exception ex)
                    {
                        AILogger.LogError("TOOL", ex);
                        throw;
                    }
                    finally { onToolEnd(); }
                };
            }
            else
            {
                AILogger.LogEvent("TOOL:INIT", $"WARNING: FunctionInvokingChatClient NOT found via GetService — tool indicator will not fire. Inner type: {inner.GetType().FullName}");
            }
        }
    }
}
