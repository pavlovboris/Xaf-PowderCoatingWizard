using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Win.Editors;
using DevExpress.AIIntegration.WinForms;
using DevExpress.Utils.Behaviors;
using DevExpress.XtraEditors.Repository;
using DevExpress.XtraGrid.Views.Grid;
//using DSERPEvo.Win.ModelExtendersInterface;
using DevExpress.XtraGrid.Views.Base;
using DevExpress.ExpressApp.TreeListEditors.Win;


namespace DSERPEvo.Win.Controllers
{
    public partial class GridEditor_ListViewController_Win : ViewController<ListView>
    {
        private BehaviorManager _aiCriteriaBehaviorManager;

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
                gridView.OptionsFilter.AllowFilterEditor = true;

                _aiCriteriaBehaviorManager ??= new BehaviorManager(components);
                _aiCriteriaBehaviorManager.Attach<PromptToExpressionBehavior>(gridView, behavior =>
                {
                    behavior.Properties.RetryAttemptCount = 3;
                    behavior.Properties.Temperature = 1.0f;
                    behavior.Properties.PromptAugmentation =
                        "Generate only valid DevExpress filter criteria for the current grid columns. " +
                        "Do not include explanations or markdown.";
                });
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
