using DevExpress.Data.Filtering;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.ApplicationBuilder;
using DevExpress.ExpressApp.Blazor.ApplicationBuilder;
using DevExpress.ExpressApp.Blazor.Services;
using DevExpress.ExpressApp.MultiTenancy;
using DevExpress.ExpressApp.Security;
using DevExpress.ExpressApp.Security.Authentication;
using DevExpress.ExpressApp.Security.Authentication.ClientServer;
using DevExpress.ExpressApp.WebApi.Services;
using DevExpress.ExpressApp.Xpo;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.PermissionPolicy;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.OData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PowderCoatingWizard.Blazor.Server.Services;
using PowderCoatingWizard.Module.Services.AI;
using PowderCoatingWizard.WebApi.JWT;
using System.Security.Claims;
using System.Security.Principal;
using System.Text;

namespace PowderCoatingWizard.Blazor.Server
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton(typeof(Microsoft.AspNetCore.SignalR.HubConnectionHandler<>), typeof(ProxyHubConnectionHandler<>));

            services.AddRazorPages();
            services.AddServerSideBlazor();
            services.AddHttpContextAccessor();
            services.AddScoped<IAuthenticationTokenProvider, JwtTokenProviderService>();
            services.AddScoped<CircuitHandler, CircuitHandlerProxy>();
            services.AddScoped<AISettingsService>();
            services.AddXaf(Configuration, builder =>
            {
                builder.UseApplication<PowderCoatingWizardBlazorApplication>();

                builder.AddXafWebApi(webApiBuilder =>
                {
                    webApiBuilder.AddXpoServices();

                    webApiBuilder.ConfigureOptions(options =>
                    {
                        // Make your business objects available in the Web API and generate the GET, POST, PUT, and DELETE HTTP methods for it.
                        // options.BusinessObject<YourBusinessObject>();
                    });
                });

                builder.Modules
                    .AddAuditTrailXpo()
                    .AddCloning()
                    .AddConditionalAppearance()
                    .AddDashboards(options =>
                    {
                        options.DashboardDataType = typeof(DevExpress.Persistent.BaseImpl.DashboardData);
                    })
                    .AddFileAttachments()
                    .AddNotifications()
                    .AddOffice()
                    .AddReports(options =>
                    {
                        options.EnableInplaceReports = true;
                        options.ReportDataType = typeof(DevExpress.Persistent.BaseImpl.ReportDataV2);
                        options.ReportStoreMode = DevExpress.ExpressApp.ReportsV2.ReportStoreModes.XML;
                    })
                    .AddScheduler()
                    .AddStateMachine(options =>
                    {
                        options.StateMachineStorageType = typeof(DevExpress.ExpressApp.StateMachine.Xpo.XpoStateMachine);
                    })
                    .AddValidation(options =>
                    {
                        options.AllowValidationDetailsAccess = false;
                    })
                    .AddViewVariants()
                    .Add<PowderCoatingWizard.Module.PowderCoatingWizardModule>()
                    .Add<PowderCoatingWizardBlazorModule>();
                builder.AddMultiTenancy()
                    .WithHostDatabaseConnectionString(Configuration.GetConnectionString("ConnectionString"))
#if EASYTEST
                    .WithHostDatabaseConnectionString(Configuration.GetConnectionString("EasyTestConnectionString"))
#endif
                    .WithMultiTenancyModelDifferenceStore(options =>
                    {
#if !RELEASE
                        options.UseTenantSpecificModel = false;
#endif
                    })
                    .WithTenantResolver<TenantByEmailResolver>();

                builder.ObjectSpaceProviders
                    .AddSecuredXpo((serviceProvider, options) =>
                    {
                        string connectionString = serviceProvider.GetRequiredService<IConnectionStringProvider>().GetConnectionString();
                        options.ConnectionString = connectionString;
                        options.ThreadSafe = true;
                        options.UseSharedDataStoreProvider = true;
                    })
                    .AddNonPersistent();
                builder.Security
                    .UseIntegratedMode(options =>
                    {
                        options.Lockout.Enabled = true;

                        options.RoleType = typeof(PermissionPolicyRole);
                        // ApplicationUser descends from PermissionPolicyUser and supports the OAuth authentication. For more information, refer to the following topic: https://docs.devexpress.com/eXpressAppFramework/402197
                        // If your application uses PermissionPolicyUser or a custom user type, set the UserType property as follows:
                        options.UserType = typeof(PowderCoatingWizard.Module.BusinessObjects.ApplicationUser);
                        // ApplicationUserLoginInfo is only necessary for applications that use the ApplicationUser user type.
                        // If you use PermissionPolicyUser or a custom user type, comment out the following line:
                        options.UserLoginInfoType = typeof(PowderCoatingWizard.Module.BusinessObjects.ApplicationUserLoginInfo);
                        options.UseXpoPermissionsCaching();
                        options.Events.OnSecurityStrategyCreated += securityStrategy =>
                        {
                            // Use the 'PermissionsReloadMode.NoCache' option to load the most recent permissions from the database once
                            // for every Session instance when secured data is accessed through this instance for the first time.
                            // Use the 'PermissionsReloadMode.CacheOnFirstAccess' option to reduce the number of database queries.
                            // In this case, permission requests are loaded and cached when secured data is accessed for the first time
                            // and used until the current user logs out.
                            // See the following article for more details: https://docs.devexpress.com/eXpressAppFramework/DevExpress.ExpressApp.Security.SecurityStrategy.PermissionsReloadMode.
                            ((SecurityStrategy)securityStrategy).PermissionsReloadMode = PermissionsReloadMode.NoCache;
                        };
                    })
                    .AddPasswordAuthentication(options =>
                    {
                        options.IsSupportChangePassword = true;
                    })
                    .AddWindowsAuthentication(options =>
                    {
                        options.CreateUserAutomatically();
                    })
                    .AddAuthenticationProvider<PowderCoatingWizard.Module.CustomAuthenticationProvider>();
            });
            var authentication = services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            });
            // ── DevExpress AI Integration ─────────────────────────────────────────
            // IChatClient is built lazily per-scope using saved AIProviderSettings.
            services.AddScoped<Microsoft.Extensions.AI.IChatClient>(sp =>
            {
                try
                {
                    var settingsService = sp.GetRequiredService<AISettingsService>();
                    var settings = settingsService.LoadSettings();
                    var client = AISettingsService.BuildChatClient(settings);
                    if (client != null)
                        return client;
                }
                catch (Exception ex)
                {
                    DevExpress.Persistent.Base.Tracing.Tracer.LogError(ex);
                }
                // Return a no-op stub so DI never fails when AI is not configured
                return new NoOpChatClient();
            });
            services.AddDevExpressAI();
            // ─────────────────────────────────────────────────────────────────────
            authentication.AddCookie(options =>
            {
                options.LoginPath = "/LoginPage";
            });
            authentication.AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters()
                {
                    ValidateIssuerSigningKey = true,
                    //ValidIssuer = Configuration["Authentication:Jwt:Issuer"],
                    //ValidAudience = Configuration["Authentication:Jwt:Audience"],
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Configuration["Authentication:Jwt:IssuerSigningKey"])),
                    AuthenticationType = JwtBearerDefaults.AuthenticationScheme
                };
            });
            //Configure OAuth2 Identity Providers based on your requirements. For more information, see
            //https://docs.devexpress.com/eXpressAppFramework/402197/task-based-help/security/how-to-use-active-directory-and-oauth2-authentication-providers-in-blazor-applications
            //https://developers.google.com/identity/protocols/oauth2
            //https://docs.microsoft.com/en-us/azure/active-directory/develop/v2-oauth2-auth-code-flow
            //https://developers.facebook.com/docs/facebook-login/manually-build-a-login-flow
            authentication.AddMicrosoftIdentityWebApp(options =>
            {
                Configuration.Bind("Authentication:AzureAd", options);
            }, openIdConnectScheme: "AzureAD", cookieScheme: null);
            authentication.AddMicrosoftIdentityWebApi(
                jwtBearerOptions =>
                {
                    jwtBearerOptions.TokenValidationParameters.NameClaimType = "preferred_username";
                },
                msIdentityOptions =>
                {
                    Configuration.Bind("Authentication:AzureAd", msIdentityOptions);
                },
                jwtBearerScheme: "AzureAd");
            services.AddAuthorization(options =>
            {
                options.DefaultPolicy = new AuthorizationPolicyBuilder(
                    JwtBearerDefaults.AuthenticationScheme,
                    "AzureAd",
                    "Windows")
                        .RequireAuthenticatedUser()
                        .RequireXafAuthentication()
                        .Build();
            });

            services
                .AddControllers()
                .AddOData((options, serviceProvider) =>
                {
                    options
                        .AddRouteComponents("api/odata", new EdmModelBuilder(serviceProvider).GetEdmModel(), Microsoft.OData.ODataVersion.V401, _routeServices =>
                        {
                            _routeServices.ConfigureXafWebApiServices();
                        })
                        .EnableQueryFeatures(100);
                });

            services.AddSwaggerGen(c =>
            {
                c.EnableAnnotations();
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "PowderCoatingWizard API",
                    Version = "v1",
                    Description = @"Use AddXafWebApi(options) in the PowderCoatingWizard.Blazor.Server\Startup.cs file to make Business Objects available in the Web API."
                });
                c.AddSecurityDefinition("JWT", new OpenApiSecurityScheme()
                {
                    Type = SecuritySchemeType.Http,
                    Name = "Bearer",
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header
                });
                c.AddSecurityRequirement(new OpenApiSecurityRequirement() {
                    {
                        new OpenApiSecurityScheme() {
                            Reference = new OpenApiReference() {
                                Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                                Id = "JWT"
                            }
                        },
                        new string[0]
                    },
                });
                var azureAdAuthorityUrl = $"{Configuration["Authentication:AzureAd:Instance"]}{Configuration["Authentication:AzureAd:TenantId"]}";
                c.AddSecurityDefinition("AzureAd", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.OAuth2,
                    Flows = new OpenApiOAuthFlows()
                    {
                        AuthorizationCode = new OpenApiOAuthFlow()
                        {
                            AuthorizationUrl = new Uri($"{azureAdAuthorityUrl}/oauth2/v2.0/authorize"),
                            TokenUrl = new Uri($"{azureAdAuthorityUrl}/oauth2/v2.0/token"),
                            Scopes = new Dictionary<string, string> {
                                // Configure scopes corresponding to https://docs.microsoft.com/en-us/azure/active-directory/develop/quickstart-configure-app-expose-web-apis
                                { @"[Enter the scope name in the PowderCoatingWizard.Blazor.Server\Startup.cs file]", @"[Enter the scope description in the PowderCoatingWizard.Blazor.Server\Startup.cs file]" }
                            }
                        }
                    }
                });
                c.AddSecurityRequirement(new OpenApiSecurityRequirement() {
                    {
                        new OpenApiSecurityScheme {
                            Reference = new OpenApiReference {
                                Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                                Id = "AzureAd"
                            },
                            In = ParameterLocation.Header
                        },
                        new string[0]
                    }
                });
            });

            services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(o =>
            {
                //The code below specifies that the naming of properties in an object serialized to JSON must always exactly match
                //the property names within the corresponding CLR type so that the property names are displayed correctly in the Swagger UI.
                //XPO is case-sensitive and requires this setting so that the example request data displayed by Swagger is always valid.
                //Comment this code out to revert to the default behavior.
                //See the following article for more information: https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonserializeroptions.propertynamingpolicy
                o.JsonSerializerOptions.PropertyNamingPolicy = null;
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "PowderCoatingWizard WebApi v1");
                    c.OAuthClientId(Configuration["Authentication:AzureAd:ClientId"]);
                    c.OAuthUsePkce();
                });
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. To change this for production scenarios, see: https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }
            app.UseHttpsRedirection();
            app.UseRequestLocalization();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseAntiforgery();
            app.UseXaf();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapXafEndpoints();
                endpoints.MapBlazorHub();
                endpoints.MapFallbackToPage("/_Host");
                endpoints.MapControllers();
            });
        }
    }
}
