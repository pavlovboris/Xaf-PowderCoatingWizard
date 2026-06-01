using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects.AI
{
    /// <summary>
    /// Represents a single workflow skill enabled for an <see cref="AIAgent"/>.
    /// Skills are runtime configuration; the enum gives stable routing semantics.
    /// </summary>
    [DefaultClassOptions]
    [NavigationItem(false)]
    [DefaultProperty(nameof(DisplayName))]
    public class AIAgentSkill : BaseObject
    {
        public AIAgentSkill(Session session) : base(session) { }

        AIAgent _agent;
        AgentSkill _skillName;

        [Association("AIAgent-EnabledSkills")]
        [Browsable(false)]
        public AIAgent Agent
        {
            get => _agent;
            set => SetPropertyValue(nameof(Agent), ref _agent, value);
        }

        public AgentSkill SkillName
        {
            get => _skillName;
            set => SetPropertyValue(nameof(SkillName), ref _skillName, value);
        }

        [PersistentAlias(nameof(SkillName))]
        public string DisplayName => SkillName switch
        {
            AgentSkill.GeneralAnswer => "General Answer",
            AgentSkill.CoatingDefectInvestigation => "Coating Defect Investigation",
            AgentSkill.ChemicalBathAnalysis => "Chemical Bath Analysis",
            AgentSkill.ProcessTrendAnalysis => "Process Trend Analysis",
            AgentSkill.DocumentCompliance => "Document Compliance",
            AgentSkill.CaseStudyMatching => "Case Study Matching",
            _ => SkillName.ToString()
        };
    }
}
