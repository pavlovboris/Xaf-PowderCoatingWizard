using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects
{
    /// <summary>
    /// Master catalogue record for a chemical product used in pre-treatment baths.
    /// Stores supplier, product reference, safety data, and usage guidelines.
    /// </summary>
    [DefaultClassOptions]
    [DefaultProperty(nameof(Name))]
    [NavigationItem("Chemistry")]
    [ImageName("BO_List")]
    public class ChemicalProduct : BaseObject
    {
        public ChemicalProduct(Session session) : base(session) { }

        string name;
        string productCode;
        string supplier;
        StageChemistryType chemistryType;
        string description;
        string safetyDataSheetRef;
        string applicationNotes;
        bool isActive;

        [Size(150)]
        [ToolTip("Commercial name of the chemical product (e.g. Bonderite C-IC 748/3, Gardoclean S5176).")]
        public string Name
        {
            get => name;
            set => SetPropertyValue(nameof(Name), ref name, value);
        }

        [Size(80)]
        [ToolTip("Supplier's product code or article number.")]
        public string ProductCode
        {
            get => productCode;
            set => SetPropertyValue(nameof(ProductCode), ref productCode, value);
        }

        [Size(150)]
        [ToolTip("Supplier or manufacturer of the product (e.g. Henkel, Chemetall, Atotech).")]
        public string Supplier
        {
            get => supplier;
            set => SetPropertyValue(nameof(Supplier), ref supplier, value);
        }

        [ToolTip("Chemistry type / bath function this product is used for.")]
        public StageChemistryType ChemistryType
        {
            get => chemistryType;
            set => SetPropertyValue(nameof(ChemistryType), ref chemistryType, value);
        }

        [Size(500)]
        [ToolTip("Description of the product and its role in the pre-treatment process.")]
        public string Description
        {
            get => description;
            set => SetPropertyValue(nameof(Description), ref description, value);
        }

        [Size(100)]
        [ToolTip("Safety data sheet reference number or document identifier.")]
        public string SafetyDataSheetRef
        {
            get => safetyDataSheetRef;
            set => SetPropertyValue(nameof(SafetyDataSheetRef), ref safetyDataSheetRef, value);
        }

        [Size(500)]
        [ToolTip("General application notes from the supplier (dosing, temperature, handling).")]
        public string ApplicationNotes
        {
            get => applicationNotes;
            set => SetPropertyValue(nameof(ApplicationNotes), ref applicationNotes, value);
        }

        [ToolTip("Whether this product is currently available and in use.")]
        public bool IsActive
        {
            get => isActive;
            set => SetPropertyValue(nameof(IsActive), ref isActive, value);
        }

        public override string ToString() => string.IsNullOrWhiteSpace(ProductCode)
            ? Name
            : $"{Name} ({ProductCode})";
    }
}
