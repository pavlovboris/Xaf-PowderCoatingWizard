using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Win.Editors;
using DevExpress.XtraEditors.Repository;
using DevExpress.XtraGrid.Views.Grid;
//using DSERPEvo.Win.ModelExtendersInterface;
using DevExpress.XtraGrid.Views.Base;
using DevExpress.ExpressApp.TreeListEditors.Win;


namespace DSERPEvo.Win.Controllers
{
    public partial class GridEditor_ListViewController_Win : ViewController<ListView>
    {
        public GridEditor_ListViewController_Win()
        {
            InitializeComponent();
        }
        protected override void OnActivated()
        {
            base.OnActivated();
        }
        protected override void OnViewControlsCreated()
        {
            base.OnViewControlsCreated();

            //if (View.Editor is GridListEditor gridListEditor)
            //{
            //    gridListEditor.GridView.OptionsBehavior.ImmediateUpdateRowPosition = false;
            //}


            //IModelGridEditorOptions IModelGridEditorOptions = View.Model as IModelGridEditorOptions;

            if (/*IModelGridEditorOptions is not null && */View.Editor is GridListEditor gridListEditor && gridListEditor.GridView != null)
            {
                gridListEditor.GridView.OptionsBehavior.ImmediateUpdateRowPosition = false;


                GridView gridView = gridListEditor.GridView;
                gridView.OptionsView.ColumnAutoWidth = false;
                //{

                //    gridView.OptionsView.ColumnAutoWidth = false;//IModelGridEditorOptions.ColumnAutoWidth;


                //    if (IModelGridEditorOptions.RowAutoHeight)
                //    {
                //        gridView.Columns.ForEach(c =>
                //        {
                //            c.ColumnEdit = new RepositoryItemMemoEdit() { WordWrap = true };
                //        });

                //        gridView.OptionsView.RowAutoHeight = IModelGridEditorOptions.RowAutoHeight;
                //    }


                //    gridView.OptionsView.AllowHtmlDrawHeaders = true;
                //    gridView.Appearance.HeaderPanel.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
                //    gridView.OptionsView.ColumnHeaderAutoHeight = DevExpress.Utils.DefaultBoolean.True;


                //}
            }
            //if(IModelGridEditorOptions is not null && View.Editor is TreeListEditor treeListEditor && treeListEditor.TreeList != null)
            //{
            //    DevExpress.XtraTreeList.TreeList treeList = treeListEditor.TreeList;
            //    {
            //        treeList.OptionsView.AutoWidth = false;//IModelGridEditorOptions.ColumnAutoWidth;
            //        if (IModelGridEditorOptions.RowAutoHeight)
            //        {
            //            treeList.Columns.ForEach(c =>
            //            {
            //                c.ColumnEdit = new RepositoryItemMemoEdit() { WordWrap = true };
            //            });
            //            treeList.OptionsView.ColumnHeaderAutoHeight = IModelGridEditorOptions.RowAutoHeight ? DevExpress.Utils.DefaultBoolean.True : DevExpress.Utils.DefaultBoolean.False;
            //        }
            //        treeList.OptionsView.AllowHtmlDrawHeaders = true;
            //        treeList.Appearance.HeaderPanel.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            //        treeList.OptionsView.ColumnHeaderAutoHeight = DevExpress.Utils.DefaultBoolean.True;

            //        treeList.OptionsMenu.EnableNodeMenu = true;
            //        treeList.OptionsSelection.UseIndicatorForSelection = true;
            //        
            //    }
            //}
        }

        protected override void OnDeactivated()
        {
            base.OnDeactivated();
        }
    }
}
