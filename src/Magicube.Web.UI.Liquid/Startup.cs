using Fluid.MvcViewEngine;
using Fluid.ViewEngine;
using Magicube.Web.UI.Liquid.Entities;
using Magicube.Web.UI.Liquid.LiquidCore;
using Magicube.Web.UI.Liquid.LiquidCore.FileProviders;
using Magicube.Web.UI.Liquid.LiquidCore.MvcViewEngine;
using Magicube.Core;
using Magicube.Data.Abstractions;
using Magicube.Data.Migration;
using Magicube.Data.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Threading.Tasks;

namespace Magicube.Web.UI.Liquid {
    public class Startup {
        public Startup(IConfiguration builder) {
            Configuration = builder;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services) {
            services.AddRazorPages();

            services.AddCore()
                .AddDatabaseCore()
                .AddEntity<WebWidgetEntity, WebWidgetEntityMapping>()
                .AddEntity<WebLayoutEntity, WebLayoutEntityMapping>()
                .AddEntity<WebPageEntity, WebPageEntityMapping>()
                .AddTransient<IWidgetService, WidgetService>()
                .UseSqlite(new DatabaseOptions { Value = $"Data Source=magicube.db" });

            services.Configure<FluidMvcViewOptions>(options => { 
                options.Parser = new MagicubeLiquidParser();
            });
            services.AddSingleton<ILiquidViewProvider, LiquidViewProvider>();
            services.AddTransient<IConfigureOptions<MvcViewOptions>, MvcViewOptionsSetup>();
            services.AddSingleton(typeof(FluidRendering), typeof(MagicubeFluidRendering));
            services.AddSingleton(typeof(IFluidViewEngine), typeof(MagicubeFluidViewEngine));
            services.ConfigureOptions<LiquidViewEngineOptionsSetup>();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env) {
            if (env.IsDevelopment()) {
                app.UseDeveloperExceptionPage();
                app.UseMigrationsEndPoint();
            } else {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseMiddleware<DatabaseMigrationMiddleware>();

            app.UseRouting();

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints => {
                endpoints.MapRazorPages();
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}"
                    );
            });
        }

        public class DatabaseMigrationMiddleware {
            private readonly RequestDelegate _next;

            public DatabaseMigrationMiddleware(RequestDelegate next) {
                _next = next;
            }

            public Task Invoke(HttpContext httpContext) {
                var migration = httpContext.RequestServices.  GetRequiredService<IMigrationManagerFactory>().GetMigrationManager();
                migration.SchemaUp();
                return _next.Invoke(httpContext);
            }
        }

        class MvcViewOptionsSetup : IConfigureOptions<MvcViewOptions> {
            private readonly IFluidViewEngine _fluidViewEngine;

            public MvcViewOptionsSetup(IFluidViewEngine fluidViewEngine) {
                _fluidViewEngine = fluidViewEngine;
            }

            public void Configure(MvcViewOptions options) {
                options.ViewEngines.Add(_fluidViewEngine);
            }
        }
    }

    public static class StringExtension {
        public static string TrimStart(this string v, string prefix) {
            if (string.IsNullOrEmpty(prefix)) return v;

            string result = v;
            while (result.StartsWith(prefix)) {
                result = result.Substring(prefix.Length);
            }

            return result;
        }

        public static string TrimEnd(this string v, string subfix) {
            if (string.IsNullOrEmpty(subfix)) return v;

            string result = v;
            while (result.EndsWith(subfix)) {
                result = result.Substring(0, result.Length - subfix.Length);
            }

            return result;
        }
    }
}
