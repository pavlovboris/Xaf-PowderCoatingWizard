using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using PowderCoatingWizard.Module.Attributes;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects
{
    /// <summary>
    /// A single measured value for one BathParameter within a MeasurementSession.
    /// Supports both numeric values (pH, temperature, concentration...)
    /// and text/qualitative results (visual check, colour test...).
    /// </summary>
    [DefaultClassOptions]
    [DefaultProperty(nameof(DisplayName))]
    [NavigationItem("Measurements")]
    [AIQueryable("A single chemistry measurement (numeric or text) taken during a measurement session.")]
    public class ParameterMeasurement : BaseObject
    {
        public ParameterMeasurement(Session session) : base(session) { }

        MeasurementSession measurementSession;
        LineStage stage;
        BathParameter parameter;
        ParameterStatus evaluatedStatus;
        double? numericValue;
        string textValue;
        PredefinedParameterValue selectedValue;

        [Association("Session-Measurements")]
        [ToolTip("The session this measurement belongs to.")]
        public MeasurementSession MeasurementSession
        {
            get => measurementSession;
            set => SetPropertyValue(nameof(MeasurementSession), ref measurementSession, value);
        }

        [ToolTip("The bath/tank stage this reading was taken from.")]
        public LineStage Stage
        {
            get => stage;
            set
            {
                if (SetPropertyValue(nameof(Stage), ref stage, value) && !IsLoading)
                {
                    OnChanged(nameof(AvailableParameters));
                    // Clear Parameter when it no longer belongs to the newly selected Stage
                    if (Parameter != null && (stage == null ||
                        !stage.Parameters.Any(sp => sp.Parameter?.Oid == Parameter.Oid)))
                        Parameter = null;
                }
            }
        }

        [ToolTip("The bath parameter to be measured.")]
        [DataSourceProperty(nameof(AvailableParameters),nameof(Stage))]
        public BathParameter Parameter
        {
            get => parameter;
            set => SetPropertyValue(nameof(Parameter), ref parameter, value);
        }

        [ToolTip("Numeric measured value (for quantitative parameters).")]
        public double? NumericValue
        {
            get => numericValue;
            set
            {
                if (SetPropertyValue(nameof(NumericValue), ref numericValue, value) && !IsLoading)
                    CaptureThreshold(force: true);
            }
        }

        [Size(300)]
        [ToolTip("Text result for qualitative/visual checks.")]
        public string TextValue
        {
            get => textValue;
            set => SetPropertyValue(nameof(TextValue), ref textValue, value);
        }

        [ToolTip("Selected option for parameters with a fixed pick-list (ValueType = Predefined).")]
        [DataSourceProperty(nameof(AvailablePredefinedValues))]
        public PredefinedParameterValue SelectedValue
        {
            get => selectedValue;
            set
            {
                if (SetPropertyValue(nameof(SelectedValue), ref selectedValue, value) && !IsLoading)
                    EvaluatedStatus = selectedValue?.Status ?? ParameterStatus.OK;
            }
        }

        /// <summary>Available predefined options for the currently selected parameter.</summary>
        public IList<PredefinedParameterValue> AvailablePredefinedValues
        {
            get
            {
                if (parameter == null) return [];
                return parameter.PredefinedValues
                    .OrderBy(v => v.SortOrder)
                    .ThenBy(v => v.Name)
                    .ToList();
            }
        }

        /// <summary>
        /// The worst status determined by evaluating ALL active thresholds at capture time.
        /// Persisted so that reporting never needs to re-run threshold logic on historical data.
        /// </summary>
        [ToolTip("Overall evaluated status based on all threshold snapshots captured at measurement time.")]
        public ParameterStatus EvaluatedStatus
        {
            get => evaluatedStatus;
            set => SetPropertyValue(nameof(EvaluatedStatus), ref evaluatedStatus, value);
        }

        /// <summary>
        /// Immutable snapshots of every active threshold that was in effect at measurement time.
        /// One snapshot per threshold — all are captured together so the full picture is preserved.
        /// </summary>
        [Aggregated, Association("Measurement-ThresholdSnapshots")]
        [ToolTip("Frozen copies of all thresholds active at the time of this measurement.")]
        public XPCollection<MeasurementThresholdSnapshot> ThresholdSnapshots
            => GetCollection<MeasurementThresholdSnapshot>(nameof(ThresholdSnapshots));

        /// <summary>
        /// Transient list of BathParameters available for the currently selected Stage.
        /// Populated from <see cref="LineStage.Parameters"/> (StageParameter links).
        /// Used by the <see cref="DataSourceProperty"/> attribute on <see cref="Parameter"/>.
        /// </summary>
        ///
        
        public IList<BathParameter> AvailableParameters
        {
            get
            {
                if (stage == null) return [];
                return stage.Parameters
                    .Select(sp => sp.Parameter)
                    .Where(p => p != null && p.IsActive)
                    .ToList();
            }
        }

        /// <summary>
        /// Captures immutable snapshots of ALL currently active thresholds for this
        /// parameter + stage combination, evaluates each one against <see cref="NumericValue"/>,
        /// and stores the final status in <see cref="EvaluatedStatus"/>.<br/>
        /// The combination logic follows <see cref="BathParameter.ThresholdEvaluationMode"/>:
        /// <list type="bullet">
        ///   <item><b>Any</b> — one breach at a level is enough to trigger that level (default).</item>
        ///   <item><b>All</b> — ALL thresholds of the same level must be breached to trigger it.</item>
        /// </list>
        /// Called automatically when <see cref="NumericValue"/> is set, so direct XAF detail-view
        /// editing is captured without any extra wiring.
        /// </summary>
        public void CaptureThreshold(bool force = false)
        {
            if (Parameter == null || !NumericValue.HasValue) return;
            if (!force && ThresholdSnapshots.Count > 0) return;

            if (force)
            {
                foreach (var old in ThresholdSnapshots.ToList())
                    old.Delete();
            }

            var measured = NumericValue.Value;

            // Stage-specific thresholds win over global ones per ThresholdType
            var stageSpecific = Session.Query<ParameterThreshold>()
                .Where(t => t.Parameter == Parameter
                         && t.Stage     == Stage
                         && t.Status    == ThresholdStatus.Active
                         && t.IsActive)
                .ToList();

            var coveredTypes = new HashSet<ThresholdType>(stageSpecific.Select(t => t.ThresholdType));

            var global = Session.Query<ParameterThreshold>()
                .Where(t => t.Parameter == Parameter
                         && t.Stage     == null
                         && t.Status    == ThresholdStatus.Active
                         && t.IsActive)
                .ToList()
                .Where(t => !coveredTypes.Contains(t.ThresholdType));

            var allActive = stageSpecific.Concat(global).ToList();

            // Capture snapshot for every threshold
            var snaps = allActive
                .Select(t => MeasurementThresholdSnapshot.Capture(Session, this, t, measured))
                .ToList();

            // Determine worst status respecting the evaluation mode
            var mode = Parameter.ThresholdEvaluationMode;
            var worstStatus = ParameterStatus.OK;

            foreach (ParameterStatus level in new[] { ParameterStatus.Alarm, ParameterStatus.Warning })
            {
                var atLevel = snaps.Where(s => s.TriggerStatus == level).ToList();
                if (atLevel.Count == 0) continue;

                bool triggered = mode == ThresholdEvaluationMode.All
                    ? atLevel.All(s => s.WasBreached)          // ALL must be breached
                    : atLevel.Any(s => s.WasBreached);         // ANY breach is enough

                if (triggered)
                {
                    worstStatus = level;   // levels are checked Alarm first, so first hit wins
                    break;
                }
            }

            EvaluatedStatus = worstStatus;
        }

        [PersistentAlias("Concat(Concat([Stage.Name], ' / '), [Parameter.Name])")]
        public string DisplayName =>
            NumericValue.HasValue
                ? $"{Parameter?.Name}: {NumericValue} {Parameter?.Unit?.Symbol}"
                : SelectedValue != null
                    ? $"{Parameter?.Name}: {SelectedValue.Name}"
                    : $"{Parameter?.Name}: {TextValue}";
    }
}
