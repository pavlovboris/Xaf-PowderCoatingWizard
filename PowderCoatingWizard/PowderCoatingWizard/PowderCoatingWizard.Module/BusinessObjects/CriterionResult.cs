using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects
{
    /// <summary>
    /// The recorded result of a single AnalysisCriterion within an AnalysisRecord.
    /// Supports numeric, grade, pass/fail, text, and hours value types.
    /// Stores a snapshot of the applied threshold at the time of recording.
    /// </summary>
    [DefaultClassOptions]
    [DefaultProperty(nameof(DisplayName))]
    [NavigationItem("Quality Analysis")]
    public class CriterionResult : BaseObject
    {
        public CriterionResult(Session session) : base(session) { }

        AnalysisRecord analysisRecord;
        AnalysisCriterion criterion;
        double? numericValue;
        int? gradeValue;
        bool? passFail;
        string textValue;
        double? hoursValue;
        AnalysisCriterionThreshold appliedThreshold;
        ParameterStatus evaluatedStatus;
        string notes;

        [Association("AnalysisRecord-Results")]
        [ToolTip("The analysis record this result belongs to.")]
        public AnalysisRecord AnalysisRecord
        {
            get => analysisRecord;
            set => SetPropertyValue(nameof(AnalysisRecord), ref analysisRecord, value);
        }

        [ToolTip("The criterion (check) this result corresponds to.")]
        public AnalysisCriterion Criterion
        {
            get => criterion;
            set => SetPropertyValue(nameof(Criterion), ref criterion, value);
        }

        [ToolTip("Numeric measured value (for Numeric value type).")]
        public double? NumericValue
        {
            get => numericValue;
            set => SetPropertyValue(nameof(NumericValue), ref numericValue, value);
        }

        [ToolTip("Ordinal grade (for Grade value type, e.g. cross-cut Gt0–Gt5).")]
        public int? GradeValue
        {
            get => gradeValue;
            set => SetPropertyValue(nameof(GradeValue), ref gradeValue, value);
        }

        [ToolTip("Pass or fail result (for PassFail value type).")]
        public bool? PassFail
        {
            get => passFail;
            set => SetPropertyValue(nameof(PassFail), ref passFail, value);
        }

        [Size(300)]
        [ToolTip("Text observation (for Text value type, e.g. colour, surface description).")]
        public string TextValue
        {
            get => textValue;
            set => SetPropertyValue(nameof(TextValue), ref textValue, value);
        }

        [ToolTip("Duration in hours (for Hours value type, e.g. hours in salt spray before failure).")]
        public double? HoursValue
        {
            get => hoursValue;
            set => SetPropertyValue(nameof(HoursValue), ref hoursValue, value);
        }

        [ToolTip("Snapshot of the active threshold at the time this result was recorded.")]
        public AnalysisCriterionThreshold AppliedThreshold
        {
            get => appliedThreshold;
            set => SetPropertyValue(nameof(AppliedThreshold), ref appliedThreshold, value);
        }

        [ToolTip("Status evaluated against the applied threshold.")]
        public ParameterStatus EvaluatedStatus
        {
            get => evaluatedStatus;
            set => SetPropertyValue(nameof(EvaluatedStatus), ref evaluatedStatus, value);
        }

        [Size(300)]
        [ToolTip("Additional notes or observations for this criterion result.")]
        public string Notes
        {
            get => notes;
            set => SetPropertyValue(nameof(Notes), ref notes, value);
        }

        [PersistentAlias("Concat([Criterion.Name], ': ')")]
        public string DisplayName => $"{Criterion?.Name}: {FormattedValue}";

        /// <summary>
        /// Returns a display-ready string of the recorded value based on the criterion's ValueType.
        /// </summary>
        public string FormattedValue => Criterion?.ValueType switch
        {
            CriterionValueType.Numeric  => NumericValue.HasValue
                                            ? $"{NumericValue} {Criterion?.Unit?.Symbol}"
                                            : "–",
            CriterionValueType.Grade    => GradeValue.HasValue ? $"{GradeValue}" : "–",
            CriterionValueType.PassFail => PassFail.HasValue
                                            ? (PassFail.Value ? "PASS" : "FAIL")
                                            : "–",
            CriterionValueType.Hours    => HoursValue.HasValue ? $"{HoursValue} h" : "–",
            CriterionValueType.Text     => TextValue ?? "–",
            _                           => "–"
        };

        /// <summary>
        /// Captures the currently active threshold for this criterion and evaluates
        /// the recorded value against it, storing the result in <see cref="EvaluatedStatus"/>.
        ///
        /// Rules:
        ///   • Only captures <see cref="AppliedThreshold"/> when it is still null (new record).
        ///     Once set, the snapshot is never overwritten — it represents the threshold
        ///     that was in effect at the exact moment of recording and must remain immutable.
        ///   • Pass <c>force: true</c> to explicitly re-capture before the first save
        ///     (e.g. when correcting a value before committing).
        ///   • <see cref="EvaluatedStatus"/> is always re-evaluated against whatever
        ///     <see cref="AppliedThreshold"/> is current (captured or existing snapshot).
        /// </summary>
        public void CaptureThresholdAndEvaluate(bool force = false)
        {
            if (Criterion == null) return;

            // Protect the existing snapshot — do not overwrite once set
            if (force || AppliedThreshold == null)
            {
                AppliedThreshold = Session.Query<AnalysisCriterionThreshold>()
                    .FirstOrDefault(t => t.Criterion == Criterion
                                      && t.Status == ThresholdStatus.Active);
            }

            if (AppliedThreshold == null || Criterion.ValueType != CriterionValueType.Numeric)
            {
                EvaluatedStatus = PassFail.HasValue
                    ? (PassFail.Value ? ParameterStatus.OK : ParameterStatus.Alarm)
                    : ParameterStatus.OK;
                return;
            }

            EvaluatedStatus = NumericValue.HasValue
                ? Criterion.EvaluateStatus(NumericValue.Value)
                : ParameterStatus.OK;
        }
    }
}
