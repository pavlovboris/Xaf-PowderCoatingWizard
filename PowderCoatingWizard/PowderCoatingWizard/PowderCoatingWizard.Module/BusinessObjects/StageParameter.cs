using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using PowderCoatingWizard.Module.Attributes;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects
{
    /// <summary>
    /// Links a BathParameter (with its thresholds) to a specific LineStage,
    /// defining which parameters are controlled and monitored at that stage.
    /// </summary>
    [DefaultClassOptions]
    [DefaultProperty(nameof(DisplayName))]
    [NavigationItem("Line Configuration")]
    [AIQueryable("Links a bath parameter to a specific line stage; defines which chemistry parameters are monitored.")]
    public class StageParameter : BaseObject
    {
        public StageParameter(Session session) : base(session) { }

        LineStage stage;
        BathParameter parameter;
        bool isRequired;
        int controlFrequencyHours;
        string notes;

        [Association("Stage-Parameters")]
        [ToolTip("The line stage this parameter belongs to.")]
        public LineStage Stage
        {
            get => stage;
            set => SetPropertyValue(nameof(Stage), ref stage, value);
        }

        [ToolTip("The bath parameter to be controlled at this stage.")]
        public BathParameter Parameter
        {
            get => parameter;
            set => SetPropertyValue(nameof(Parameter), ref parameter, value);
        }

        [ToolTip("Whether measurement of this parameter is mandatory (e.g. required by Qualicoat).")]
        public bool IsRequired
        {
            get => isRequired;
            set => SetPropertyValue(nameof(IsRequired), ref isRequired, value);
        }

        [ToolTip("How often this parameter must be measured, in hours (0 = every batch).")]
        public int ControlFrequencyHours
        {
            get => controlFrequencyHours;
            set => SetPropertyValue(nameof(ControlFrequencyHours), ref controlFrequencyHours, value);
        }

        [Size(300)]
        [ToolTip("Additional notes or instructions for measuring this parameter.")]
        public string Notes
        {
            get => notes;
            set => SetPropertyValue(nameof(Notes), ref notes, value);
        }

        [PersistentAlias("Concat([Stage.Name], Concat(' / ', [Parameter.Name]))")]
        public string DisplayName => $"{Stage?.Name} / {Parameter?.Name}";
    }
}
