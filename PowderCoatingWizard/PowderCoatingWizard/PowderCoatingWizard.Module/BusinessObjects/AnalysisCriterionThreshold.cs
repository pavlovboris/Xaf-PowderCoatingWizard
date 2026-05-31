using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects
{
    /// <summary>
    /// Defines a pass/warning/fail limit for an AnalysisCriterion.
    /// Mirrors the ParameterThreshold versioning pattern so limit changes
    /// are fully auditable and traceable over time.
    /// </summary>
    [DefaultClassOptions]
    [DefaultProperty(nameof(DisplayName))]
    //[NavigationItem("Analysis Configuration")]
    [NavigationItem(false)]

    public class AnalysisCriterionThreshold : BaseObject
    {
        public AnalysisCriterionThreshold(Session session) : base(session) { }

        AnalysisCriterion criterion;
        ThresholdType thresholdType;
        ComparisonDirection direction;
        double value;
        ParameterStatus triggerStatus;
        string action;
        ThresholdStatus status;
        DateTime validFrom;
        DateTime? validTo;
        int version;
        string changeNote;

        [Association("Criterion-Thresholds")]
        [ToolTip("The analysis criterion this threshold applies to.")]
        public AnalysisCriterion Criterion
        {
            get => criterion;
            set => SetPropertyValue(nameof(Criterion), ref criterion, value);
        }

        [ToolTip("Type of threshold (Warning, Alarm, Target, Min, Max).")]
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

        [ToolTip("Numeric limit value for this threshold.")]
        public double Value
        {
            get => value;
            set => SetPropertyValue(nameof(Value), ref this.value, value);
        }

        [ToolTip("Status triggered when this threshold is breached.")]
        public ParameterStatus TriggerStatus
        {
            get => triggerStatus;
            set => SetPropertyValue(nameof(TriggerStatus), ref triggerStatus, value);
        }

        [Size(500)]
        [ToolTip("Recommended corrective action when this threshold is triggered.")]
        public string Action
        {
            get => action;
            set => SetPropertyValue(nameof(Action), ref action, value);
        }

        [ToolTip("Lifecycle status of this threshold version: Active, Draft, or Archived.")]
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
        [ToolTip("Note explaining why this version was created (e.g. updated after Qualicoat audit).")]
        public string ChangeNote
        {
            get => changeNote;
            set => SetPropertyValue(nameof(ChangeNote), ref changeNote, value);
        }

        [PersistentAlias("Concat(Concat([Criterion.Name], ' – '), [ThresholdType])")]
        public string DisplayName =>
            $"{Criterion?.Name} – {ThresholdType} ({Direction} {Value} {Criterion?.Unit?.Symbol})";

        /// <summary>
        /// Returns the triggered status if this threshold is active and the measured value breaches it.
        /// Returns OK otherwise.
        /// </summary>
        public ParameterStatus EvaluateStatus(double measuredValue)
        {
            if (Status != ThresholdStatus.Active)
                return ParameterStatus.OK;

            bool breached = Direction == ComparisonDirection.Above
                ? measuredValue > Value
                : measuredValue < Value;

            return breached ? TriggerStatus : ParameterStatus.OK;
        }
    }
}
