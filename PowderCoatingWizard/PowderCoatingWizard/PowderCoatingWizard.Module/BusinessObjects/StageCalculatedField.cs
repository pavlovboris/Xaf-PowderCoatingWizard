using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects
{
    /// <summary>
    /// A calculated (formula-driven) column displayed in the Bath Stage Sheet alongside
    /// measurement parameters and criteria results.
    ///
    /// The <see cref="Formula"/> uses DevExpress Criteria Expression syntax, e.g.:
    ///   Iif([pH] > 7, 'Alkaline', Iif([pH] &lt; 6, 'Acid', 'OK'))
    ///   [Conductivity] * 0.001
    ///
    /// Available context names (case-sensitive, spaces replaced by _):
    ///   [ParameterName]          — raw numeric or text value of that parameter
    ///   [ParameterName_Status]   — 'OK', 'Warning', or 'Alarm'
    ///   [CriterionName]          — result message of that criterion
    ///   [CriterionName_Status]   — 'OK', 'Warning', or 'Alarm'
    /// </summary>
    [DefaultClassOptions]
    [DefaultProperty(nameof(Name))]
    //[NavigationItem("Line Configuration")]
    [NavigationItem(false)]
    public class StageCalculatedField : BaseObject
    {
        public StageCalculatedField(Session session) : base(session) { }

        LineStage stage;
        string name;
        string formula;
        int sortOrder;
        int width;

        [Association("Stage-CalculatedFields")]
        [ToolTip("The stage this calculated field belongs to.")]
        public LineStage Stage
        {
            get => stage;
            set => SetPropertyValue(nameof(Stage), ref stage, value);
        }

        [Size(100)]
        [ToolTip("Display name shown as the column header.")]
        public string Name
        {
            get => name;
            set => SetPropertyValue(nameof(Name), ref name, value);
        }

        /// <summary>
        /// DevExpress Criteria Expression evaluated per row.
        /// Reference parameter/criterion values by sanitised name (spaces → _).
        /// </summary>
        [Size(-1)]
        [ToolTip("DevExpress criteria expression. Use [ParameterName], [ParameterName_Status], [CriterionName], [CriterionName_Status].")]
        public string Formula
        {
            get => formula;
            set => SetPropertyValue(nameof(Formula), ref formula, value);
        }

        [ToolTip("Column order in the sheet (lower = earlier).")]
        public int SortOrder
        {
            get => sortOrder;
            set => SetPropertyValue(nameof(SortOrder), ref sortOrder, value);
        }

        [ToolTip("Column width in pixels (0 = default 140).")]
        public int Width
        {
            get => width;
            set => SetPropertyValue(nameof(Width), ref width, value);
        }
    }
}
