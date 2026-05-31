using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects
{
    /// <summary>
    /// Represents a single bath or tank stage in a production line pre-treatment tunnel.
    /// Each stage has a defined chemistry type, position, and set of controlled parameters.
    /// </summary>
    [DefaultClassOptions]
    [DefaultProperty(nameof(DisplayName))]
    [NavigationItem("Line Configuration")]
    public class LineStage : BaseObject
    {
        public LineStage(Session session) : base(session) { }

        ProductionLine line;
        int position;
        string name;
        StageChemistryType chemistryType;
        string description;
        bool isActive;
        StageFunction stageFunction;
        string gridLayoutXml;
        string blazorLayoutJson;

        [Association("Line-Stages")]
        [ToolTip("The production line this stage belongs to.")]
        public ProductionLine Line
        {
            get => line;
            set => SetPropertyValue(nameof(Line), ref line, value);
        }

        [ToolTip("Position of this stage in the pre-treatment sequence (1 = first).")]
        public int Position
        {
            get => position;
            set => SetPropertyValue(nameof(Position), ref position, value);
        }

        [Size(100)]
        [ToolTip("Name or label for this stage (e.g. Degreaser, Rinse 1, Ti/Zr Conversion).")]
        public string Name
        {
            get => name;
            set => SetPropertyValue(nameof(Name), ref name, value);
        }

        [ToolTip("Functional role of this stage in the pre-treatment process.")]
        public StageFunction StageFunction
        {
            get => stageFunction;
            set => SetPropertyValue(nameof(StageFunction), ref stageFunction, value);
        }

        [ToolTip("Type of chemistry used in this bath/tank.")]
        public StageChemistryType ChemistryType
        {
            get => chemistryType;
            set => SetPropertyValue(nameof(ChemistryType), ref chemistryType, value);
        }

        [Size(500)]
        [ToolTip("Additional notes about this stage.")]
        public string Description
        {
            get => description;
            set => SetPropertyValue(nameof(Description), ref description, value);
        }

        [ToolTip("Whether this stage is currently active.")]
        public bool IsActive
        {
            get => isActive;
            set => SetPropertyValue(nameof(IsActive), ref isActive, value);
        }

        /// <summary>Persisted GridView layout XML for the Bath Stage Sheet. Saved per-stage.</summary>
        [Size(-1)]
        [ToolTip("Grid layout XML for the Bath Stage Sheet — saved per stage.")]
        [Browsable(false)]
        public string GridLayoutXml
        {
            get => gridLayoutXml;
            set => SetPropertyValue(nameof(GridLayoutXml), ref gridLayoutXml, value);
        }

        /// <summary>Persisted DxGrid layout JSON for the Blazor Bath Stage Sheet. Saved per-stage.</summary>
        [Size(-1)]
        [Browsable(false)]
        public string BlazorLayoutJson
        {
            get => blazorLayoutJson;
            set => SetPropertyValue(nameof(BlazorLayoutJson), ref blazorLayoutJson, value);
        }

        [Aggregated, Association("Stage-Chemicals")]
        [ToolTip("Chemical products configured for this bath/tank stage.")]
        public XPCollection<StageChemical> Chemicals
            => GetCollection<StageChemical>(nameof(Chemicals));

        [Aggregated, Association("Stage-Parameters")]
        [ToolTip("Bath parameters monitored and controlled at this stage.")]
        public XPCollection<StageParameter> Parameters
            => GetCollection<StageParameter>(nameof(Parameters));

        [Aggregated, Association("Stage-Criteria")]
        [ToolTip("Evaluation criteria configured for this stage — each becomes a column in the Bath Stage Sheet.")]
        public XPCollection<StageCriterion> Criteria
            => GetCollection<StageCriterion>(nameof(Criteria));

        [Aggregated, Association("Stage-CalculatedFields")]
        [ToolTip("Formula-driven calculated columns displayed in the Bath Stage Sheet.")]
        public XPCollection<StageCalculatedField> CalculatedFields
            => GetCollection<StageCalculatedField>(nameof(CalculatedFields));

        [Aggregated, Association("Stage-ExcelTemplates")]
        [ToolTip("Excel template configurations for spreadsheet-driven calculated columns.")]
        public XPCollection<StageExcelTemplate> ExcelTemplates
            => GetCollection<StageExcelTemplate>(nameof(ExcelTemplates));

        [Association("Stage-ArchiveRows")]
        [Browsable(false)]
        public XPCollection<BathStageSheetArchiveRow> ArchiveRows
            => GetCollection<BathStageSheetArchiveRow>(nameof(ArchiveRows));

        [PersistentAlias("Concat(Concat(ToStr([Position]), ' – '), [Name])")]
        public string DisplayName => $"{Position} – {Name} ({ChemistryType})";
    }

    public enum StageFunction
    {
        [Description("Degreasing — remove oils and contaminants")]
        Degreasing,
        [Description("Etching — acid etch / pickling")]
        Etching,
        [Description("Rinsing — water rinse (demineralised or tap)")]
        Rinsing,
        [Description("Conversion — Ti/Zr or chrome-free conversion layer")]
        Conversion,
        [Description("Sealing — post-treatment sealing")]
        Sealing,
        [Description("Activation — surface activation before conversion")]
        Activation,
        [Description("Other / custom stage function")]
        Other
    }

    public enum StageChemistryType
    {
        [Description("Alkaline degreaser")]
        Alkaline,
        [Description("Acid etch / deox")]
        Acid,
        [Description("Ti/Zr conversion chemistry (Qualicoat compliant)")]
        TitaniumZirconium,
        [Description("Chrome-free conversion chemistry")]
        ChromeFree,
        [Description("Demineralised / RO water rinse")]
        DemineralisedWater,
        [Description("City / tap water rinse")]
        TapWater,
        [Description("Post-conversion sealer")]
        Sealer,
        [Description("No chemistry — dry stage or passthrough")]
        None
    }
}
