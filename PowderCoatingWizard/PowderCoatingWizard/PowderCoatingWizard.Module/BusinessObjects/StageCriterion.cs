using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects
{
    /// <summary>
    /// A named evaluation column attached to a <see cref="LineStage"/>.
    /// Appears as one extra column in the Bath Stage Sheet grid.
    ///
    /// Evaluation is a priority-ordered IF / ELSE-IF / ELSE chain:
    ///   • Each <see cref="StageCriterionBranch"/> is one IF-clause (sorted by SortOrder).
    ///   • The first branch whose conditions are satisfied wins.
    ///   • If no branch matches, <see cref="DefaultStatus"/> / <see cref="DefaultMessage"/> are returned.
    /// </summary>
    [DefaultClassOptions]
    [DefaultProperty(nameof(Name))]
    //[NavigationItem("Line Configuration")]
    [NavigationItem(false)]

    public class StageCriterion : BaseObject
    {
        public StageCriterion(Session session) : base(session) { }

        LineStage stage;
        string name;
        int sortOrder;
        ParameterStatus defaultStatus;
        string defaultMessage;

        [Association("Stage-Criteria")]
        [ToolTip("The bath/tank stage this criterion belongs to.")]
        public LineStage Stage
        {
            get => stage;
            set => SetPropertyValue(nameof(Stage), ref stage, value);
        }

        [Size(150)]
        [ToolTip("Column header shown in the Bath Stage Sheet (e.g. 'Препоръка').")]
        public string Name
        {
            get => name;
            set => SetPropertyValue(nameof(Name), ref name, value);
        }

        [ToolTip("Display order of this criterion column (ascending).")]
        public int SortOrder
        {
            get => sortOrder;
            set => SetPropertyValue(nameof(SortOrder), ref sortOrder, value);
        }

        [ToolTip("Status returned when no branch fires (the ELSE result).")]
        public ParameterStatus DefaultStatus
        {
            get => defaultStatus;
            set => SetPropertyValue(nameof(DefaultStatus), ref defaultStatus, value);
        }

        [Size(300)]
        [ToolTip("Message shown when no branch fires (e.g. 'НОРМАЛНА РАБОТА').")]
        public string DefaultMessage
        {
            get => defaultMessage;
            set => SetPropertyValue(nameof(DefaultMessage), ref defaultMessage, value);
        }

        /// <summary>
        /// Ordered IF-branches. Evaluated in ascending <see cref="StageCriterionBranch.SortOrder"/>;
        /// the first branch whose conditions are satisfied wins.
        /// </summary>
        [Aggregated, Association("Criterion-Branches")]
        [ToolTip("Ordered IF-branches. First match wins (IF … ELSE IF … ELSE).")]
        public XPCollection<StageCriterionBranch> Branches
            => GetCollection<StageCriterionBranch>(nameof(Branches));

        /// <summary>
        /// Convenience: all conditions across all branches (for topological sort in the sheet service).
        /// </summary>
        public IEnumerable<StageCriterionCondition> AllConditions
            => Branches.SelectMany(b => b.Conditions);

        /// <summary>
        /// Evaluates this criterion against measured parameter values and already-computed
        /// sibling criterion results.  Returns the first matching branch result, or
        /// (DefaultStatus, DefaultMessage) if none match.
        /// </summary>
        public (ParameterStatus Status, string Message) Evaluate(
            IReadOnlyDictionary<Guid, ParameterMeasurement> measurements,
            IReadOnlyDictionary<Guid, ParameterStatus>? criterionResults = null)
        {
            foreach (var branch in Branches.OrderBy(b => b.SortOrder))
            {
                if (branch.IsTriggered(measurements, criterionResults))
                    return (branch.ResultStatus, branch.ResultMessage ?? string.Empty);
            }

            return (DefaultStatus, DefaultMessage ?? string.Empty);
        }
    }

    public enum CriterionConditionMode
    {
        [Description("All conditions must be true (AND)")]
        All,
        [Description("At least one condition must be true (OR)")]
        Any
    }
}
