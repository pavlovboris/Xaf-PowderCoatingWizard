using PowderCoatingWizard.Module.BusinessObjects;
using PowderCoatingWizard.Module.BusinessObjects.AI;
using System.ComponentModel;
using System.Text;

namespace PowderCoatingWizard.Module.Services.AI
{
    /// <summary>
    /// Provides the assistant with the XAF application context captured before the custom chat window was opened.
    /// </summary>
    public sealed class CurrentContextToolService
    {
        private readonly LineStage? _stage;
        private readonly AIAgent? _agent;
        private readonly SchemaDiscoveryService _schema;
        private readonly bool _databaseToolsEnabled;
        private readonly bool _ragAvailable;
        private readonly CurrentXafContextSnapshot _contextSnapshot;

        public CurrentContextToolService(
            LineStage? stage,
            AIAgent? agent,
            SchemaDiscoveryService schema,
            bool databaseToolsEnabled,
            bool ragAvailable,
            CurrentXafContextSnapshot? contextSnapshot = null)
        {
            _stage = stage;
            _agent = agent;
            _schema = schema;
            _databaseToolsEnabled = databaseToolsEnabled;
            _ragAvailable = ragAvailable;
            _contextSnapshot = contextSnapshot ?? CurrentXafContextSnapshot.Empty;
        }

        [Description("Returns the current XAF application context captured before the custom AI chat window was opened. Use when the user says this, current, selected, here, or refers to the active stage/screen. This tool never uses the chat window itself as the application context.")]
        public string GetCurrentContext()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Current XAF application context for internal assistant reasoning.");
            sb.AppendLine("Context source: captured from the originating XAF view before the custom AI chat window was opened.");
            sb.AppendLine("Do not treat the AI chat window as the selected business object or active application view.");
            sb.AppendLine();

            AppendViewSnapshot(sb);

            if (_stage != null)
            {
                sb.AppendLine("Active stage:");
                sb.AppendLine($"- Entity: {nameof(LineStage)}");
                sb.AppendLine($"- Display: {Safe(_stage.DisplayName)}");
                sb.AppendLine($"- Name: {Safe(_stage.Name)}");
                sb.AppendLine($"- Position: {_stage.Position}");
                sb.AppendLine($"- Function: {_stage.StageFunction}");
                sb.AppendLine($"- Chemistry: {_stage.ChemistryType}");
                sb.AppendLine($"- Active: {_stage.IsActive}");
                if (_stage.Line != null)
                    sb.AppendLine($"- Production line: {Safe(_stage.Line.ToString())}");
                if (!string.IsNullOrWhiteSpace(_stage.Description))
                    sb.AppendLine($"- Description: {Safe(_stage.Description)}");
            }
            else
            {
                sb.AppendLine("Active stage: none captured from the originating XAF view.");
            }

            sb.AppendLine();
            if (_agent != null)
            {
                sb.AppendLine("Active agent:");
                sb.AppendLine($"- Name: {Safe(_agent.Name)}");
                if (!string.IsNullOrWhiteSpace(_agent.Description))
                    sb.AppendLine($"- Description: {Safe(_agent.Description)}");
                sb.AppendLine($"- Enabled tools: {FormatAgentTools(_agent)}");
                sb.AppendLine($"- Enabled skills: {FormatAgentSkills(_agent)}");
            }
            else
            {
                sb.AppendLine("Active agent: default assistant profile.");
            }

            sb.AppendLine();
            sb.AppendLine("Available context capabilities:");
            sb.AppendLine($"- Database tools enabled: {_databaseToolsEnabled}");
            sb.AppendLine($"- Retrieval/search available: {_ragAvailable}");
            sb.AppendLine($"- AI-queryable entity count: {_schema.Schema.Entities.Count}");

            return sb.ToString();
        }

        private void AppendViewSnapshot(StringBuilder sb)
        {
            if (string.IsNullOrWhiteSpace(_contextSnapshot.ViewId) &&
                _contextSnapshot.CurrentObject == null &&
                _contextSnapshot.SelectedObjectCount == 0)
            {
                sb.AppendLine("Originating XAF view: no view snapshot was captured.");
                sb.AppendLine();
                return;
            }

            sb.AppendLine("Originating XAF view:");
            if (!string.IsNullOrWhiteSpace(_contextSnapshot.ViewId))
                sb.AppendLine($"- View id: {Safe(_contextSnapshot.ViewId)}");
            if (!string.IsNullOrWhiteSpace(_contextSnapshot.ViewType))
                sb.AppendLine($"- View type: {Safe(_contextSnapshot.ViewType)}");
            if (!string.IsNullOrWhiteSpace(_contextSnapshot.ObjectTypeName))
                sb.AppendLine($"- Object type: {Safe(_contextSnapshot.ObjectTypeName)}");
            if (_contextSnapshot.IsListView)
                sb.AppendLine("- Mode: ListView");
            if (_contextSnapshot.IsDetailView)
                sb.AppendLine("- Mode: DetailView");

            if (_contextSnapshot.CurrentObject != null)
            {
                sb.AppendLine("Current object:");
                AppendObjectSnapshot(sb, _contextSnapshot.CurrentObject, "  ");
            }
            else
            {
                sb.AppendLine("Current object: none captured.");
            }

            sb.AppendLine($"Selected objects: {_contextSnapshot.SelectedObjectCount}");
            if (_contextSnapshot.SelectedObjects.Count > 0)
            {
                foreach (var selected in _contextSnapshot.SelectedObjects)
                    AppendObjectSnapshot(sb, selected, "  - ");

                if (_contextSnapshot.SelectedObjectCount > _contextSnapshot.SelectedObjects.Count)
                    sb.AppendLine($"  - Additional selected objects omitted: {_contextSnapshot.SelectedObjectCount - _contextSnapshot.SelectedObjects.Count}");
            }

            sb.AppendLine();
        }

        private static void AppendObjectSnapshot(StringBuilder sb, CurrentXafObjectSnapshot snapshot, string prefix)
        {
            var keyText = string.IsNullOrWhiteSpace(snapshot.Key) ? "no key captured" : snapshot.Key;
            sb.AppendLine($"{prefix}{Safe(snapshot.EntityName)} key={Safe(keyText)} display={Safe(snapshot.DisplayText)}");
        }

        private static string FormatAgentTools(AIAgent agent)
        {
            var values = agent.EnabledTools.Select(t => t.ToolName.ToString()).ToList();
            return values.Count == 0 ? "All tools" : string.Join(", ", values);
        }

        private static string FormatAgentSkills(AIAgent agent)
        {
            var values = agent.EnabledSkills.Select(s => s.SkillName.ToString()).ToList();
            return values.Count == 0 ? "All skills" : string.Join(", ", values);
        }

        private static string Safe(string? value)
        {
            var text = value?.Replace("\r", " ").Replace("\n", " ").Trim() ?? string.Empty;
            return text.Length <= 300 ? text : text[..300] + "...";
        }
    }
}
