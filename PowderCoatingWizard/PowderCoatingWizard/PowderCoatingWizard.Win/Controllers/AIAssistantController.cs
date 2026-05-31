using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.Win;
using DevExpress.Persistent.Base;
using Microsoft.Extensions.DependencyInjection;
using PowderCoatingWizard.Module.BusinessObjects;
using PowderCoatingWizard.Module.BusinessObjects.AI;
using PowderCoatingWizard.Module.Services.AI;
using PowderCoatingWizard.Win.Forms;

namespace PowderCoatingWizard.Win.Controllers
{
    /// <summary>
    /// Adds an "AI Assistant" toolbar action on LineStage detail/list views.
    /// As a ViewController it always has a valid View and CurrentObject context.
    /// </summary>
    public class AIAssistantController : ViewController
    {
        readonly SimpleAction _openAction;

        public AIAssistantController()
        {
            TargetObjectType = typeof(LineStage);

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

            LineStage? currentStage = View switch
            {
                DetailView dv => dv.CurrentObject as LineStage,
                ListView lv   => lv.CurrentObject as LineStage,
                _             => null
            };

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
            var form = new AIAssistantForm(chatClient, embGen, os, osFactory, currentStage, selectedAgent);
            form.Show();
        }
    }
}
