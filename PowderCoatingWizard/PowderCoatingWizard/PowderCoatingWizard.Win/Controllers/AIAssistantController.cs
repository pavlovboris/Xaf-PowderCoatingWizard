using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.MultiTenancy;
using DevExpress.ExpressApp.Win;
using DevExpress.Persistent.Base;
using Microsoft.Extensions.DependencyInjection;
using PowderCoatingWizard.Module.BusinessObjects;
using PowderCoatingWizard.Module.BusinessObjects.AI;
using PowderCoatingWizard.Module.Services.AI;
using PowderCoatingWizard.Win.Forms;
using System.Configuration;

namespace PowderCoatingWizard.Win.Controllers
{
    /// <summary>
    /// Adds an "AI Assistant" toolbar action available in all views.
    /// Stage context is used when available but is not required.
    /// </summary>
    public class AIAssistantController : ViewController
    {
        readonly SimpleAction _openAction;

        public AIAssistantController()
        {
            // No TargetObjectType restriction — action is visible in every view
            _openAction = new SimpleAction(this, "OpenAIAssistant", PredefinedCategory.Tools)
            {
                Caption = "AI Assistant",
                ToolTip = "Open the AI-powered assistant to analyse measurements and get recommendations",
                ImageName = "BO_Unknown",
                PaintStyle = DevExpress.ExpressApp.Templates.ActionItemPaintStyle.CaptionAndImage
            };
            _openAction.Execute += OnOpenAIAssistant;
        }

        private void OnOpenAIAssistant(object sender, SimpleActionExecuteEventArgs e)
        {
            var winApp = (WinApplication)Application;
            var sp = winApp.ServiceProvider;

            var settingsService = sp?.GetService<AISettingsService>();
            var settings = settingsService?.LoadSettings();
            var chatClient = AISettingsService.BuildChatClient(settings);
            var embGen = AISettingsService.BuildEmbeddingGenerator(settings);

            // Stage context is optional — pick it from the current view if available
            LineStage? currentStage = View switch
            {
                DetailView dv => dv.CurrentObject as LineStage,
                ListView lv   => lv.CurrentObject as LineStage,
                _             => null
            };
            var contextSnapshot = CaptureCurrentContext(e);

            var osFactory = sp?.GetService<IObjectSpaceFactory>();

            // Offer the user a choice of agent profiles if any active agents exist
            AIAgent? selectedAgent = null;
            if (osFactory != null)
            {
                using var agentOs = osFactory.CreateObjectSpace(typeof(AIAgent));
                var activeAgents = agentOs.GetObjects<AIAgent>()
                                          .Where(a => a.IsActive)
                                          .OrderBy(a => a.Name)
                                          .ToList();

                if (activeAgents.Count > 0)
                {
                    using var picker = new AIAgentPickerForm(activeAgents);
                    if (picker.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return; // user cancelled

                    selectedAgent = picker.SelectedAgent;
                }
            }

            var os = Application.CreateObjectSpace(typeof(MeasurementSession));

            // Show session picker so user can restore a previous session or start a new one.
            AIChatSession? selectedSession = null;
            if (osFactory != null)
            {
                using var sessionOs = osFactory.CreateObjectSpace(typeof(AIChatSession));
                using var sessionPicker = new AIChatSessionPickerForm(sessionOs);
                if (sessionPicker.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    os.Dispose();
                    return;
                }
                // Re-load the session in the form's own OS if one was chosen.
                if (!sessionPicker.IsNew && sessionPicker.SelectedSession != null)
                    selectedSession = os.GetObjectByKey<AIChatSession>(sessionPicker.SelectedSession.Oid);
            }

            var sqlConnectionString = sp?.GetService<IConnectionStringProvider>()?.GetConnectionString()
                ?? ConfigurationManager.ConnectionStrings["ConnectionString"]?.ConnectionString
                ?? Application.ConnectionString;
            var form = new AIAssistantForm(chatClient, embGen, os, osFactory, currentStage, selectedAgent, selectedSession, sqlConnectionString, contextSnapshot);
            form.Show();
        }

        private CurrentXafContextSnapshot CaptureCurrentContext(SimpleActionExecuteEventArgs e)
        {
            var objectType = View.ObjectTypeInfo?.Type;
            var selected = e.SelectedObjects
                .Cast<object>()
                .Where(o => o != null)
                .Take(10)
                .Select(CreateObjectSnapshot)
                .Where(o => o != null)
                .Cast<CurrentXafObjectSnapshot>()
                .ToList();

            object? currentObject = View switch
            {
                DetailView dv => dv.CurrentObject,
                ListView lv => lv.CurrentObject,
                _ => null
            };

            return new CurrentXafContextSnapshot
            {
                ViewId = View.Id ?? string.Empty,
                ViewType = View.GetType().Name,
                ObjectTypeName = objectType?.Name ?? string.Empty,
                ObjectTypeFullName = objectType?.FullName ?? string.Empty,
                CurrentObject = CreateObjectSnapshot(currentObject),
                SelectedObjects = selected,
                SelectedObjectCount = e.SelectedObjects.Count,
                IsListView = View is ListView,
                IsDetailView = View is DetailView
            };
        }

        private CurrentXafObjectSnapshot? CreateObjectSnapshot(object? obj)
        {
            if (obj == null)
                return null;

            var type = obj.GetType();
            object? key = null;
            try { key = View.ObjectSpace.GetKeyValue(obj); }
            catch { /* best effort context snapshot */ }

            return new CurrentXafObjectSnapshot
            {
                EntityName = type.Name,
                EntityFullName = type.FullName ?? type.Name,
                Key = key?.ToString() ?? string.Empty,
                DisplayText = obj.ToString() ?? type.Name
            };
        }
    }
}
