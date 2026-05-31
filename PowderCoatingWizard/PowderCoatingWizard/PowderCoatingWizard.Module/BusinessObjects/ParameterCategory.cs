using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using DevExpress.Xpo;
using System.ComponentModel;

namespace PowderCoatingWizard.Module.BusinessObjects
{
    [DefaultClassOptions]
    [DefaultProperty(nameof(Name))]
    [NavigationItem("Bath Configuration")]
    public class ParameterCategory : BaseObject
    {
        public ParameterCategory(Session session) : base(session) { }

        string name;
        string description;
        int sortOrder;

        [Size(100)]
        [ToolTip("Category name (e.g. Chemistry, Process, Quality, Dosing).")]
        public string Name
        {
            get => name;
            set => SetPropertyValue(nameof(Name), ref name, value);
        }

        [Size(300)]
        [ToolTip("Description of the category.")]
        public string Description
        {
            get => description;
            set => SetPropertyValue(nameof(Description), ref description, value);
        }

        [ToolTip("Sort order used in lists and dropdowns.")]
        public int SortOrder
        {
            get => sortOrder;
            set => SetPropertyValue(nameof(SortOrder), ref sortOrder, value);
        }

        public override string ToString() => name;
    }
}
