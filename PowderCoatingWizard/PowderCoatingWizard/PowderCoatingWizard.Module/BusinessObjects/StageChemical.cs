using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects
{
    /// <summary>
    /// Links a chemical product to a specific bath/tank stage on a production line.
    /// Stores the configured working concentration and dosing information for that stage.
    /// A single stage can use multiple chemical products (e.g. main agent + additive).
    /// </summary>
    [DefaultClassOptions]
    [DefaultProperty(nameof(DisplayName))]
    //[NavigationItem("Chemistry")]
    [NavigationItem(false)]
    public class StageChemical : BaseObject
    {
        public StageChemical(Session session) : base(session) { }

        LineStage stage;
        ChemicalProduct product;
        double targetConcentration;
        double minConcentration;
        double maxConcentration;
        ParameterUnit concentrationUnit;
        double targetTemperature;
        string dosingNotes;
        bool isActive;

        [Association("Stage-Chemicals")]
        [ToolTip("The bath/tank stage this chemical is configured for.")]
        public LineStage Stage
        {
            get => stage;
            set => SetPropertyValue(nameof(Stage), ref stage, value);
        }

        [ToolTip("The chemical product used at this stage.")]
        public ChemicalProduct Product
        {
            get => product;
            set => SetPropertyValue(nameof(Product), ref product, value);
        }

        [ToolTip("Target working concentration for this product in the bath.")]
        public double TargetConcentration
        {
            get => targetConcentration;
            set => SetPropertyValue(nameof(TargetConcentration), ref targetConcentration, value);
        }

        [ToolTip("Minimum acceptable working concentration.")]
        public double MinConcentration
        {
            get => minConcentration;
            set => SetPropertyValue(nameof(MinConcentration), ref minConcentration, value);
        }

        [ToolTip("Maximum acceptable working concentration.")]
        public double MaxConcentration
        {
            get => maxConcentration;
            set => SetPropertyValue(nameof(MaxConcentration), ref maxConcentration, value);
        }

        [ToolTip("Unit of the concentration values (e.g. g/L, %, mL/L).")]
        public ParameterUnit ConcentrationUnit
        {
            get => concentrationUnit;
            set => SetPropertyValue(nameof(ConcentrationUnit), ref concentrationUnit, value);
        }

        [ToolTip("Target operating temperature for this bath stage in °C.")]
        public double TargetTemperature
        {
            get => targetTemperature;
            set => SetPropertyValue(nameof(TargetTemperature), ref targetTemperature, value);
        }

        [Size(400)]
        [ToolTip("Dosing instructions or make-up procedure for this product.")]
        public string DosingNotes
        {
            get => dosingNotes;
            set => SetPropertyValue(nameof(DosingNotes), ref dosingNotes, value);
        }

        [ToolTip("Whether this chemical is currently active in the stage configuration.")]
        public bool IsActive
        {
            get => isActive;
            set => SetPropertyValue(nameof(IsActive), ref isActive, value);
        }

        [PersistentAlias("Concat([Stage.Name], Concat(' / ', [Product.Name]))")]
        public string DisplayName => $"{Stage?.Name} / {Product?.Name}";
    }
}
