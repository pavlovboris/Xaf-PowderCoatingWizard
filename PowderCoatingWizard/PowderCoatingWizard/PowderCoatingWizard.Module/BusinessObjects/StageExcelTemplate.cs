using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using PowderCoatingWizard.Module.Editors;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects
{
    /// <summary>
    /// Excel template attached to a <see cref="LineStage"/>.
    /// Configures INPUT cell mappings (stage parameters / criteria / calculated fields -> Excel cells)
    /// and OUTPUT result mappings (Excel cells -> column names shown in the Bath Stage Sheet).
    ///
    /// Lives inside the Stage detail view - just like Parameters and Criteria.
    /// Not visible as a separate navigation item.
    ///
    /// Workflow per row in the Bath Stage Sheet:
    ///   1. For each INPUT mapping: resolve the source value and write it into the configured cell.
    ///   2. Workbook.Calculate()
    ///   3. For each OUTPUT mapping: read the cell value -> display as a column in the sheet.
    /// </summary>
    [DefaultProperty(nameof(Name))]
    public class StageExcelTemplate : BaseObject
    {
        public StageExcelTemplate(Session session) : base(session) { }

        LineStage stage;
        string name;
        FileData templateFile;

        [Association("Stage-ExcelTemplates")]
        [ToolTip("The stage this template belongs to.")]
        public LineStage Stage
        {
            get => stage;
            set => SetPropertyValue(nameof(Stage), ref stage, value);
        }

        [Size(100)]
        [ToolTip("Descriptive name for this template configuration.")]
        public string Name
        {
            get => name;
            set => SetPropertyValue(nameof(Name), ref name, value);
        }

        /// <summary>
        /// Uploaded Excel (.xlsx) template file.
        /// Use the standard XAF file-attachment editor to upload.
        /// </summary>
        [Aggregated]
        [System.ComponentModel.DisplayName("Template (.xlsx)")]
        [ToolTip("Upload the Excel (.xlsx) workbook template here.")]
        public FileData TemplateFile
        {
            get => templateFile;
            set => SetPropertyValue(nameof(TemplateFile), ref templateFile, value);
        }

        /// <summary>Raw bytes of the uploaded template — used by the calculation service.</summary>
        [Browsable(false)]
        public byte[]? TemplateData => TemplateFile?.Content;

        /// <summary>
        /// INPUT mappings: source (parameter / criterion / field) -> Excel input cell.
        /// The service writes the current row value of each source into the cell before recalculating.
        /// </summary>
        [Aggregated, Association("ExcelTemplate-InputMaps")]
        [ToolTip("INPUT mappings: pick a parameter/criterion/field and the target cell address.")]
        public XPCollection<StageExcelInputMap> InputMaps
            => GetCollection<StageExcelInputMap>(nameof(InputMaps));

        /// <summary>
        /// OUTPUT mappings: Excel result cell address -> column name displayed in the Bath Stage Sheet.
        /// After recalculation each result cell value is read and shown as a column.
        /// </summary>
        [Aggregated, Association("ExcelTemplate-OutputMaps")]
        [ToolTip("OUTPUT mappings: Excel result cell address -> column name.")]
        public XPCollection<StageExcelOutputMap> OutputMaps
            => GetCollection<StageExcelOutputMap>(nameof(OutputMaps));
    }

        // -- Input source kind --------------------------------------------------

    /// <summary>What kind of stage value to feed into an Excel cell.</summary>
    public enum ExcelInputSourceKind
    {
        [Description("Parameter - measured value")]
        ParameterValue,
        [Description("Parameter - evaluated status (OK/Warning/Alarm)")]
        ParameterStatus,
        [Description("Criterion - result message")]
        CriterionMessage,
        [Description("Criterion - result status (OK/Warning/Alarm)")]
        CriterionStatus,
        [Description("Calculated field - text result")]
        CalculatedField,
    }

    /// <summary>
    /// One INPUT mapping row.
    /// Instead of a raw context key the user picks a source (parameter / criterion / field)
    /// and a value kind; the <see cref="ContextKey"/> is derived automatically.
    /// </summary>
    [DefaultProperty(nameof(DisplayName))]
    public class StageExcelInputMap : BaseObject
    {
        public StageExcelInputMap(Session session) : base(session) { }

        StageExcelTemplate template;
        ExcelInputSourceKind sourceKind;
        StageParameter sourceParameter;
        StageCriterion sourceCriterion;
        StageCalculatedField sourceCalculatedField;
        string inputCellAddress;
        int sortOrder;

        [Association("ExcelTemplate-InputMaps")]
        public StageExcelTemplate Template
        {
            get => template;
            set => SetPropertyValue(nameof(Template), ref template, value);
        }

        // -- Source selection ------------------------------------------------

        [ToolTip("What type of value to feed into the Excel cell.")]
        public ExcelInputSourceKind SourceKind
        {
            get => sourceKind;
            set
            {
                SetPropertyValue(nameof(SourceKind), ref sourceKind, value);
                OnChanged(nameof(DisplayName));
                OnChanged(nameof(ContextKey));
            }
        }

        [ToolTip("Stage parameter whose value or status will be written into the cell.")]
        [DataSourceProperty(nameof(AvailableParameters))]
        public StageParameter SourceParameter
        {
            get => sourceParameter;
            set
            {
                SetPropertyValue(nameof(SourceParameter), ref sourceParameter, value);
                OnChanged(nameof(DisplayName));
                OnChanged(nameof(ContextKey));
            }
        }

        [ToolTip("Stage criterion whose message or status will be written into the cell.")]
        [DataSourceProperty(nameof(AvailableCriteria))]
        public StageCriterion SourceCriterion
        {
            get => sourceCriterion;
            set
            {
                SetPropertyValue(nameof(SourceCriterion), ref sourceCriterion, value);
                OnChanged(nameof(DisplayName));
                OnChanged(nameof(ContextKey));
            }
        }

        [ToolTip("Calculated field whose result will be written into the cell.")]
        [DataSourceProperty(nameof(AvailableCalculatedFields))]
        public StageCalculatedField SourceCalculatedField
        {
            get => sourceCalculatedField;
            set
            {
                SetPropertyValue(nameof(SourceCalculatedField), ref sourceCalculatedField, value);
                OnChanged(nameof(DisplayName));
                OnChanged(nameof(ContextKey));
            }
        }

        // -- Cell address ---------------------------------------------------

        /// <summary>Target Excel cell address, e.g. "B2" or "Sheet1!B2".</summary>
        [Size(50)]
        [ToolTip("Target Excel cell address, e.g. B2 or Sheet1!B2.")]
        public string InputCellAddress
        {
            get => inputCellAddress;
            set => SetPropertyValue(nameof(InputCellAddress), ref inputCellAddress,
                value?.Trim().ToUpperInvariant());
        }

        [ToolTip("Write order (lower = written first).")]
        public int SortOrder
        {
            get => sortOrder;
            set => SetPropertyValue(nameof(SortOrder), ref sortOrder, value);
        }

        // -- Derived / transient --------------------------------------------

        /// <summary>
        /// The context dictionary key that <see cref="CalculatedFieldEvaluator.BuildContext"/> produces
        /// for the selected source. This is what the service uses to look up the value - no manual typing needed.
        /// </summary>
        [Browsable(false)]
        [NonPersistent]
        public string ContextKey
        {
            get
            {
                string? baseName = sourceKind switch
                {
                    ExcelInputSourceKind.ParameterValue   or
                    ExcelInputSourceKind.ParameterStatus   => sourceParameter?.Parameter?.Name,
                    ExcelInputSourceKind.CriterionMessage or
                    ExcelInputSourceKind.CriterionStatus   => sourceCriterion?.Name,
                    ExcelInputSourceKind.CalculatedField   => sourceCalculatedField?.Name,
                    _ => null
                };

                if (string.IsNullOrWhiteSpace(baseName)) return string.Empty;

                var sanitised = CalculatedFieldEvaluator.Sanitise(baseName);
                return sourceKind is ExcelInputSourceKind.ParameterStatus
                                  or ExcelInputSourceKind.CriterionStatus
                    ? sanitised + "_Status"
                    : sanitised;
            }
        }

        [PersistentAlias(nameof(inputCellAddress))]
        public string DisplayName
        {
            get
            {
                var cell = string.IsNullOrWhiteSpace(inputCellAddress) ? "?" : inputCellAddress;
                var key  = string.IsNullOrWhiteSpace(ContextKey) ? "..." : ContextKey;
                return $"{key} -> {cell}";
            }
        }

        // -- Available source lists (populated from parent template's stage) --

        [Browsable(false)]
        public IList<StageParameter> AvailableParameters
        {
            get
            {
                var stage = Template?.Stage;
                if (stage == null) return [];
                return [.. stage.Parameters
                    .Where(p => p.Parameter != null)
                    .OrderBy(p => p.Parameter.Name)];
            }
        }

        [Browsable(false)]
        public IList<StageCriterion> AvailableCriteria
        {
            get
            {
                var stage = Template?.Stage;
                if (stage == null) return [];
                return [.. stage.Criteria.OrderBy(c => c.SortOrder).ThenBy(c => c.Name)];
            }
        }

        [Browsable(false)]
        public IList<StageCalculatedField> AvailableCalculatedFields
        {
            get
            {
                var stage = Template?.Stage;
                if (stage == null) return [];
                return [.. stage.CalculatedFields.OrderBy(f => f.SortOrder).ThenBy(f => f.Name)];
            }
        }
    }

    /// <summary>
    /// One OUTPUT mapping row: an Excel cell whose value is read after recalculation
    /// and displayed as a column in the Bath Stage Sheet.
    /// </summary>
    [DefaultProperty(nameof(DisplayName))]
    public class StageExcelOutputMap : BaseObject
    {
        public StageExcelOutputMap(Session session) : base(session) { }

        StageExcelTemplate template;
        string columnName;
        string resultCellAddress;
        int sortOrder;
        int width;

        [Association("ExcelTemplate-OutputMaps")]
        public StageExcelTemplate Template
        {
            get => template;
            set => SetPropertyValue(nameof(Template), ref template, value);
        }

        /// <summary>Column header shown in the Bath Stage Sheet.</summary>
        [Size(100)]
        [ToolTip("Column name displayed as the header in the Bath Stage Sheet.")]
        public string ColumnName
        {
            get => columnName;
            set => SetPropertyValue(nameof(ColumnName), ref columnName, value);
        }

        /// <summary>Excel cell address to read after recalculation, e.g. "C5" or "Sheet1!C5".</summary>
        [Size(50)]
        [ToolTip("Excel cell address to read after recalculation, e.g. C5 or Sheet1!C5.")]
        public string ResultCellAddress
        {
            get => resultCellAddress;
            set => SetPropertyValue(nameof(ResultCellAddress), ref resultCellAddress,
                value?.Trim().ToUpperInvariant());
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

        [PersistentAlias(nameof(resultCellAddress))]
        public string DisplayName
        {
            get
            {
                var cell = string.IsNullOrWhiteSpace(resultCellAddress) ? "?" : resultCellAddress;
                var col  = string.IsNullOrWhiteSpace(columnName) ? "..." : columnName;
                return $"{cell} -> \"{col}\"";
            }
        }
    }
}
