using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects
{
    /// <summary>
    /// Defines a threshold/limit for a given parameter.
    /// A single parameter can have multiple thresholds (Warning Low, Warning High, Alarm Low, Alarm High, Target, etc.)
    /// </summary>
    [DefaultClassOptions]
    [DefaultProperty(nameof(DisplayName))]
    //[NavigationItem("Bath Configuration")]
    [NavigationItem(false)]

    public class ParameterThreshold : BaseObject
    {
        public ParameterThreshold(Session session) : base(session) { }

        BathParameter parameter;
        LineStage stage;
        ThresholdType thresholdType;
        ComparisonDirection direction;
        double value;
        ParameterStatus triggerStatus;
        string action;
        bool isActive;
        ThresholdStatus status;
        DateTime validFrom;
        DateTime? validTo;
        int version;
        string changeNote;

        [Association("Parameter-Thresholds")]
        [ToolTip("The parameter this threshold applies to.")]
        public BathParameter Parameter
        {
            get => parameter;
            set => SetPropertyValue(nameof(Parameter), ref parameter, value);
        }

        [ToolTip("The specific bath/tank stage this threshold applies to. Leave empty to apply to all stages.")]
        public LineStage Stage
        {
            get => stage;
            set => SetPropertyValue(nameof(Stage), ref stage, value);
        }

        [ToolTip("Type of threshold (Warning, Alarm, Target).")]
        public ThresholdType ThresholdType
        {
            get => thresholdType;
            set => SetPropertyValue(nameof(ThresholdType), ref thresholdType, value);
        }

        [ToolTip("Comparison direction — below or above the threshold value.")]
        public ComparisonDirection Direction
        {
            get => direction;
            set => SetPropertyValue(nameof(Direction), ref direction, value);
        }

        [ToolTip("Numeric value of the threshold.")]
        public double Value
        {
            get => value;
            set => SetPropertyValue(nameof(Value), ref this.value, value);
        }

        [ToolTip("Status triggered when the threshold is breached.")]
        public ParameterStatus TriggerStatus
        {
            get => triggerStatus;
            set => SetPropertyValue(nameof(TriggerStatus), ref triggerStatus, value);
        }

        [Size(500)]
        [ToolTip("Recommended action when this threshold is triggered.")]
        public string Action
        {
            get => action;
            set => SetPropertyValue(nameof(Action), ref action, value);
        }

        [ToolTip("Whether this threshold is active.")]
        public bool IsActive
        {
            get => isActive;
            set => SetPropertyValue(nameof(IsActive), ref isActive, value);
        }

        [ToolTip("Threshold status: Active, Archived, or Draft.")]
        public ThresholdStatus Status
        {
            get => status;
            set => SetPropertyValue(nameof(Status), ref status, value);
        }

        [ToolTip("Date from which this version of the threshold is effective.")]
        public DateTime ValidFrom
        {
            get => validFrom;
            set => SetPropertyValue(nameof(ValidFrom), ref validFrom, value);
        }

        [ToolTip("Expiry/archive date. Leave empty if the threshold is still active.")]
        public DateTime? ValidTo
        {
            get => validTo;
            set => SetPropertyValue(nameof(ValidTo), ref validTo, value);
        }

        [ToolTip("Version number — incremented on each change.")]
        public int Version
        {
            get => version;
            set => SetPropertyValue(nameof(Version), ref version, value);
        }

        [Size(500)]
        [ToolTip("Note explaining why this version was created (e.g. correction after audit).")]
        public string ChangeNote
        {
            get => changeNote;
            set => SetPropertyValue(nameof(ChangeNote), ref changeNote, value);
        }

        [PersistentAlias("Concat(Concat([Parameter.Name], ' – '), [ThresholdType])")]
        public string DisplayName =>
            Stage != null
                ? $"{Parameter?.Name} [{Stage.Name}] – {ThresholdType} ({Direction} {Value} {Parameter?.Unit?.Symbol})"
                : $"{Parameter?.Name} – {ThresholdType} ({Direction} {Value} {Parameter?.Unit?.Symbol})";

        /// <summary>
        /// Activates this threshold and archives all other Active thresholds
        /// for the same Parameter + Stage + ThresholdType combination.
        /// Ensures only one active threshold per parameter/stage/type at any time.
        /// </summary>
        public void Activate()
        {
            var siblings = Session.Query<ParameterThreshold>()
                .Where(t => t.Oid != Oid
                         && t.Parameter == Parameter
                         && t.Stage == Stage
                         && t.ThresholdType == ThresholdType
                         && t.Status == ThresholdStatus.Active);

            foreach (var sibling in siblings)
            {
                sibling.Status   = ThresholdStatus.Archived;
                sibling.IsActive = false;
                sibling.ValidTo  = DateTime.Today;
                sibling.Save();
            }

            Status    = ThresholdStatus.Active;
            IsActive  = true;
            ValidFrom = ValidFrom == default ? DateTime.Today : ValidFrom;
            ValidTo   = null;
            Save();
        }

        /// <summary>
        /// Evaluates a measured value against this threshold.
        /// Returns TriggerStatus if the condition is breached, otherwise OK.
        /// </summary>
        public ParameterStatus EvaluateStatus(double measuredValue)
        {
            if (!isActive || status != ThresholdStatus.Active) return ParameterStatus.OK;

            bool breached = direction switch
            {
                ComparisonDirection.Below => measuredValue < value,
                ComparisonDirection.Above => measuredValue > value,
                _ => false
            };

            return breached ? triggerStatus : ParameterStatus.OK;
        }
    }

    public enum ThresholdType
    {
        [Description("Target value — informational only")]
        Target,
        [Description("Warning — value is below the lower recommended limit")]
        WarningLow,
        [Description("Warning — value is above the upper recommended limit")]
        WarningHigh,
        [Description("Alarm — value is below the critical lower limit")]
        AlarmLow,
        [Description("Alarm — value is above the critical upper limit")]
        AlarmHigh
    }

    public enum ComparisonDirection
    {
        [Description("Breach when measured value is below the threshold")]
        Below,
        [Description("Breach when measured value is above the threshold")]
        Above
    }

    public enum ThresholdStatus
    {
        [Description("Draft — not used for evaluation yet")]
        Draft,
        [Description("Active — used for evaluation")]
        Active,
        [Description("Archived — kept for history, no longer used")]
        Archived
    }
}
