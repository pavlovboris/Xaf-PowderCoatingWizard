using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects
{
    /// <summary>
    /// Defines a type of quality analysis performed on a production line
    /// (e.g. Panel Analysis, Wet Adhesion Test, Part/Detail Analysis).
    /// An AnalysisType acts as a template — it groups the criteria (checks)
    /// that must be performed and recorded during each analysis session.
    /// </summary>
    [DefaultClassOptions]
    [DefaultProperty(nameof(Name))]
    [NavigationItem("Analysis Configuration")]
    [ImageName("BO_Audit")]
    public class AnalysisType : BaseObject
    {
        public AnalysisType(Session session) : base(session) { }

        string name;
        AnalysisCategory category;
        string standardReference;
        string description;
        bool isActive;
        int performanceFrequencyDays;
        ProductionLine line;

        [Size(150)]
        [ToolTip("Name of this analysis type (e.g. Panel Analysis – Qualicoat, Wet Adhesion, Detail Inspection).")]
        public string Name
        {
            get => name;
            set => SetPropertyValue(nameof(Name), ref name, value);
        }

        [ToolTip("Category of this analysis.")]
        public AnalysisCategory Category
        {
            get => category;
            set => SetPropertyValue(nameof(Category), ref category, value);
        }

        [Size(150)]
        [ToolTip("Reference to the standard or clause that mandates this analysis (e.g. Qualicoat §3.2.1, EN 13523-4).")]
        public string StandardReference
        {
            get => standardReference;
            set => SetPropertyValue(nameof(StandardReference), ref standardReference, value);
        }

        [Size(500)]
        [ToolTip("Description of the analysis procedure or purpose.")]
        public string Description
        {
            get => description;
            set => SetPropertyValue(nameof(Description), ref description, value);
        }

        [ToolTip("Minimum required frequency in days (e.g. 1 = daily, 7 = weekly). 0 = per batch.")]
        public int PerformanceFrequencyDays
        {
            get => performanceFrequencyDays;
            set => SetPropertyValue(nameof(PerformanceFrequencyDays), ref performanceFrequencyDays, value);
        }

        [ToolTip("Production line this analysis type is configured for. Leave empty if it applies to all lines.")]
        public ProductionLine Line
        {
            get => line;
            set => SetPropertyValue(nameof(Line), ref line, value);
        }

        [ToolTip("Whether this analysis type is currently active and in use.")]
        public bool IsActive
        {
            get => isActive;
            set => SetPropertyValue(nameof(IsActive), ref isActive, value);
        }

        [Aggregated, Association("AnalysisType-Criteria")]
        [ToolTip("Individual checks and measurements that make up this analysis.")]
        public XPCollection<AnalysisCriterion> Criteria
            => GetCollection<AnalysisCriterion>(nameof(Criteria));
    }

    public enum AnalysisCategory
    {
        [Description("Coated test panel — Qualicoat mandatory")]
        PanelAnalysis,
        [Description("Wet adhesion / water resistance test")]
        WetAdhesion,
        [Description("Analysis performed directly on a coated part")]
        DetailAnalysis,
        [Description("Dry film thickness measurement")]
        FilmThickness,
        [Description("Specular gloss measurement")]
        GlossCheck,
        [Description("Direct / reverse impact resistance")]
        ImpactResistance,
        [Description("Cross-cut / lattice cut adhesion")]
        CrossCutAdhesion,
        [Description("Erichsen cupping test")]
        CuplingTest,
        [Description("Neutral salt spray (NSS) — EN ISO 9227")]
        SaltSpray,
        [Description("Condensation / humidity cabinet resistance")]
        HumidityResistance,
        [Description("Mortar / solvent chemical resistance")]
        ChemicalResistance,
        [Description("Accelerated UV aging")]
        UVResistance,
        [Description("General visual / surface quality check")]
        VisualInspection,
        [Description("General in-process quality check")]
        ProcessControl,
        [Description("Other / custom analysis")]
        Other
    }
}
