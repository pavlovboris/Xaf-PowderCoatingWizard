using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects
{
    /// <summary>
    /// Immutable snapshot of one <see cref="ParameterThreshold"/> captured at the moment
    /// a <see cref="ParameterMeasurement"/> was recorded.
    ///
    /// Values are COPIED from the live threshold so that future edits to the threshold
    /// do not alter the historical audit record.
    ///
    /// One <see cref="ParameterMeasurement"/> will have one snapshot per active threshold
    /// that was in effect at measurement time (e.g. WarningLow + WarningHigh + AlarmLow
    /// can all be captured simultaneously).
    /// </summary>
    [DefaultProperty(nameof(DisplayName))]
    [NavigationItem("Measurements")]
    public class MeasurementThresholdSnapshot : BaseObject
    {
        public MeasurementThresholdSnapshot(Session session) : base(session) { }

        ParameterMeasurement measurement;
        Guid   originalThresholdOid;
        ThresholdType thresholdType;
        ComparisonDirection direction;
        double thresholdValue;
        ParameterStatus triggerStatus;
        string action;
        int    version;
        bool   wasBreached;

        // ── Owner ────────────────────────────────────────────────────────────

        [Association("Measurement-ThresholdSnapshots")]
        [ToolTip("The measurement this snapshot belongs to.")]
        public ParameterMeasurement Measurement
        {
            get => measurement;
            set => SetPropertyValue(nameof(Measurement), ref measurement, value);
        }

        // ── Copied threshold values (audit-safe) ─────────────────────────────

        [ToolTip("OID of the original ParameterThreshold object (for traceability only).")]
        public Guid OriginalThresholdOid
        {
            get => originalThresholdOid;
            set => SetPropertyValue(nameof(OriginalThresholdOid), ref originalThresholdOid, value);
        }

        [ToolTip("Type of threshold captured (WarningLow, AlarmHigh, etc.).")]
        public ThresholdType ThresholdType
        {
            get => thresholdType;
            set => SetPropertyValue(nameof(ThresholdType), ref thresholdType, value);
        }

        [ToolTip("Comparison direction at capture time.")]
        public ComparisonDirection Direction
        {
            get => direction;
            set => SetPropertyValue(nameof(Direction), ref direction, value);
        }

        [ToolTip("Threshold limit value at capture time.")]
        public double ThresholdValue
        {
            get => thresholdValue;
            set => SetPropertyValue(nameof(ThresholdValue), ref thresholdValue, value);
        }

        [ToolTip("Status that was triggered if this threshold was breached.")]
        public ParameterStatus TriggerStatus
        {
            get => triggerStatus;
            set => SetPropertyValue(nameof(TriggerStatus), ref triggerStatus, value);
        }

        [Size(500)]
        [ToolTip("Recommended action text at capture time.")]
        public string Action
        {
            get => action;
            set => SetPropertyValue(nameof(Action), ref action, value);
        }

        [ToolTip("Version number of the threshold at capture time.")]
        public int Version
        {
            get => version;
            set => SetPropertyValue(nameof(Version), ref version, value);
        }

        // ── Evaluation result ────────────────────────────────────────────────

        [ToolTip("Whether the measured value actually breached this threshold.")]
        public bool WasBreached
        {
            get => wasBreached;
            set => SetPropertyValue(nameof(WasBreached), ref wasBreached, value);
        }

        // ── Display ──────────────────────────────────────────────────────────

        [PersistentAlias("Concat([ThresholdType], Concat(' ', [Direction]))")]
        public string DisplayName =>
            $"{ThresholdType} {Direction} {ThresholdValue}{(WasBreached ? " ⚠ BREACHED" : "")}";

        // ── Factory ──────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a new snapshot from a live <paramref name="threshold"/> and immediately
        /// evaluates whether <paramref name="measuredValue"/> breaches it.
        /// </summary>
        public static MeasurementThresholdSnapshot Capture(
            Session session,
            ParameterMeasurement owner,
            ParameterThreshold threshold,
            double measuredValue)
        {
            var snap = new MeasurementThresholdSnapshot(session)
            {
                Measurement          = owner,
                OriginalThresholdOid = threshold.Oid,
                ThresholdType        = threshold.ThresholdType,
                Direction            = threshold.Direction,
                ThresholdValue       = threshold.Value,
                TriggerStatus        = threshold.TriggerStatus,
                Action               = threshold.Action ?? string.Empty,
                Version              = threshold.Version,
            };

            // Evaluate immediately so the result is frozen in the snapshot
            snap.WasBreached = threshold.Direction switch
            {
                ComparisonDirection.Below => measuredValue < threshold.Value,
                ComparisonDirection.Above => measuredValue > threshold.Value,
                _                         => false
            };

            return snap;
        }
    }
}
