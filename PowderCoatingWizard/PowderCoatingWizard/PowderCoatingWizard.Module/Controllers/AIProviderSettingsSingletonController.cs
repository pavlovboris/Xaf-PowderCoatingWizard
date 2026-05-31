using DevExpress.ExpressApp;
using DevExpress.ExpressApp.SystemModule;
using DevExpress.Persistent.Base;
using PowderCoatingWizard.Module.BusinessObjects.AI;

namespace PowderCoatingWizard.Module.Controllers
{
    /// <summary>
    /// Intercepts navigation to AIProviderSettings and opens the single record's
    /// Detail View directly instead of the default List View.
    /// </summary>
    public class AIProviderSettingsSingletonController : WindowController
    {
        public AIProviderSettingsSingletonController()
        {
            TargetWindowType = WindowType.Main;
        }

        protected override void OnActivated()
        {
            base.OnActivated();
            var navController = Frame.GetController<ShowNavigationItemController>();
            if (navController != null)
                navController.CustomShowNavigationItem += OnCustomShowNavigationItem;
        }

        protected override void OnDeactivated()
        {
            var navController = Frame.GetController<ShowNavigationItemController>();
            if (navController != null)
                navController.CustomShowNavigationItem -= OnCustomShowNavigationItem;
            base.OnDeactivated();
        }

        private void OnCustomShowNavigationItem(object sender, CustomShowNavigationItemEventArgs e)
        {
            var item = e.ActionArguments.SelectedChoiceActionItem;
            if (item == null) return;

            // Diagnostic: log the item details so we can verify the correct Id/Data
            var shortcutData = item.Data as ViewShortcut;
            Tracing.Tracer.LogText(
                $"[AIProviderSettings] Nav item Id='{item.Id}' Data={item.Data?.GetType().Name} ShortcutViewId='{shortcutData?.ViewId}'");

            // Try item.Id first, then ViewShortcut.ViewId
            var viewId = item.Id;
            if (string.IsNullOrEmpty(viewId) || !viewId.Contains(nameof(AIProviderSettings)))
                viewId = shortcutData?.ViewId ?? string.Empty;

            if (string.IsNullOrEmpty(viewId) || !viewId.Contains(nameof(AIProviderSettings)))
                return;

            // Only intercept List Views
            if (!viewId.Contains("List"))
                return;

            // Ensure the singleton record exists
            using var seedOs = Application.CreateObjectSpace(typeof(AIProviderSettings));
            if (seedOs.GetObjectsCount(typeof(AIProviderSettings), null) == 0)
            {
                var seed = seedOs.CreateObject<AIProviderSettings>();
                seed.DisplayName = "Default AI Provider";
                seed.IsEnabled = true;
                seedOs.CommitChanges();
            }

            // Open the single record directly in a Detail View
            // Note: no 'using' — XAF owns and disposes the ObjectSpace with the view
            var detailOs = Application.CreateObjectSpace(typeof(AIProviderSettings));
            var record = detailOs.FirstOrDefault<AIProviderSettings>(s => true)!;
            var detailView = Application.CreateDetailView(detailOs, record);
            detailView.ViewEditMode = DevExpress.ExpressApp.Editors.ViewEditMode.Edit;

            // Block the default List View navigation and show the Detail View manually
            e.ActionArguments.ShowViewParameters.CreatedView = detailView;
            e.ActionArguments.ShowViewParameters.TargetWindow = TargetWindow.Current;
            e.Handled = true;
            Application.ShowViewStrategy.ShowView(
                e.ActionArguments.ShowViewParameters,
                new ShowViewSource(Frame, e.ActionArguments.Action));
        }
    }
}
