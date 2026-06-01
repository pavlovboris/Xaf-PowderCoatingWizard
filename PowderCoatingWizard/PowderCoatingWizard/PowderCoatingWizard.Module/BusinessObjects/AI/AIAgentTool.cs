using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects.AI
{
    /// <summary>
    /// Represents a single AI tool enabled for an <see cref="AIAgent"/>.
    /// The collection of these objects on the agent is rendered by XAF's built-in
    /// TokenObjectsPropertyEditor — no custom editor required.
    /// </summary>
    [DefaultClassOptions]
    [NavigationItem(false)]
    [DefaultProperty(nameof(DisplayName))]
    public class AIAgentTool : BaseObject
    {
        public AIAgentTool(Session session) : base(session) { }

        AIAgent _agent;
        AgentTool _toolName;

        [Association("AIAgent-EnabledTools")]
        [Browsable(false)]
        public AIAgent Agent
        {
            get => _agent;
            set => SetPropertyValue(nameof(Agent), ref _agent, value);
        }

        /// <summary>The tool this entry represents.</summary>
        public AgentTool ToolName
        {
            get => _toolName;
            set => SetPropertyValue(nameof(ToolName), ref _toolName, value);
        }

        /// <summary>Human-readable label shown in the token selector.</summary>
        [PersistentAlias(nameof(ToolName))]
        public string DisplayName => ToolName switch
        {
            AgentTool.BathData       => "Live Bath Data",
            AgentTool.Trend          => "Measurement Trend",
            AgentTool.ThresholdAlert => "Threshold Alerts",
            AgentTool.DbQuery        => "Database Query",
            _                        => ToolName.ToString()
        };
    }
}
