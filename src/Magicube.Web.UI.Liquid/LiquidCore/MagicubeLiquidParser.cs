using Fluid;
using Fluid.Ast;
using Fluid.MvcViewEngine;
using Fluid.ViewEngine;
using Magicube.Web.UI.Liquid.Entities;
using Magicube.Web.UI.Liquid.LiquidCore.FileProviders;
using Magicube.Web.UI.Liquid.LiquidCore.MvcViewEngine;
using Magicube.Web.UI.Liquid.LiquidCore.Statements;
using Magicube.Core;
using Magicube.Core.Reflection;
using Magicube.Core.Signals;
using Magicube.Data.Abstractions;
using Magicube.Data.Abstractions.ViewModel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Parlot.Fluent;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using ReflectionContext = Magicube.Core.Reflection.ReflectionContext;

namespace Magicube.Web.UI.Liquid.LiquidCore {
    public class LiquidViewEngineOptionsSetup : IConfigureOptions<FluidMvcViewOptions> {
        private readonly IServiceProvider _serviceProvider;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public LiquidViewEngineOptionsSetup(IServiceProvider serviceProvider, IWebHostEnvironment webHostEnvironment){
            _serviceProvider    = serviceProvider;
            _webHostEnvironment = webHostEnvironment;
        }
        public void Configure(FluidMvcViewOptions options) {
            if(options.PartialsFileProvider == null) {
                options.PartialsFileProvider = new FileProviderMapper(_webHostEnvironment.ContentRootFileProvider, "Views");
            }

            options.ViewsFileProvider = _serviceProvider.GetService<ILiquidViewProvider>();
            if(options.ViewsFileProvider == null) {
                options.ViewsFileProvider = new FileProviderMapper(_webHostEnvironment.ContentRootFileProvider, "Views");
            }

            options.ViewsLocationFormats.Clear();
            options.ViewsLocationFormats.Add("/{1}/{0}" + Constants.ViewExtension);
            options.ViewsLocationFormats.Add("/Shared/{0}" + Constants.ViewExtension);

            options.PartialsLocationFormats.Clear();
            options.PartialsLocationFormats.Add("{0}" + Constants.ViewExtension);
            options.PartialsLocationFormats.Add("/Partials/{0}" + Constants.ViewExtension);
            options.PartialsLocationFormats.Add("/Partials/{1}/{0}" + Constants.ViewExtension);
            options.PartialsLocationFormats.Add("/Shared/Partials/{0}" + Constants.ViewExtension);

            options.LayoutsLocationFormats.Clear();
            options.LayoutsLocationFormats.Add("/Shared/{0}" + Constants.ViewExtension);


            options.TemplateOptions.MemberAccessStrategy.Register<DynamicEntity, object>((source, name) => source[name]);
            options.TemplateOptions.MemberAccessStrategy.Register<Dictionary<string, object>, object>((source, name) => source[name]);
            options.TemplateOptions.MemberAccessStrategy.Register<IEntityViewModel, object>((source, name) => source[name]);
        }
    }

    public class MagicubeLiquidTemplateContext : TemplateContext {
        public MagicubeLiquidTemplateContext(IServiceProvider services) {
            Services = services;
        }

        public MagicubeLiquidTemplateContext(object model, IServiceProvider services) : base(model) {
            Services = services;
        }

        public IServiceProvider Services { get; }
    }

    public class MagicubeLiquidParser : FluidViewParser {
        public const string RenderKey = "render";

        /// <summary>
        /// {% render {widgetType}:{entity} %}
        /// </summary>
        public MagicubeLiquidParser() {
            var widgetRenderTag = Identifier.ElseError($"An identifier was expected after the '{RenderKey}' tag")
                .AndSkip(Colon.ElseError($"':' was expected after the identifier of '{RenderKey}'"))
                .And(Identifier)
                .AndSkip(TagEnd.ElseError("'%}' was expected"))
                .Then<Statement>(x => {
                    return new RenderWidgetStatement(this, x.Item1, x.Item2);
                });
            RegisteredTags[RenderKey] = widgetRenderTag;
        }
    }

    interface IWidgetService {
        Task<WebWidgetEntity> GetWidget(string widget);
    }

    class WidgetService : IWidgetService {
        private readonly IRepository<WebWidgetEntity, int> _repository;

        public WidgetService(IRepository<WebWidgetEntity, int> repository) {
            _repository = repository;
        }

        public async Task<WebWidgetEntity> GetWidget(string widget) {
            return await _repository.GetAsync(x => x.Name == widget);
        }
    }

    namespace FileProviders {
        public interface ILiquidViewProvider : IFileProvider { }
        public class LiquidViewProvider : ILiquidViewProvider {
            private readonly Dictionary<string, LiquidViewInfo> _content = new();
            private readonly IServiceScopeFactory _serviceScopeFactory;
            private readonly IFileProvider _partialsFileProvider;

            public LiquidViewProvider(IServiceScopeFactory serviceScopeFactory, IWebHostEnvironment webHostEnvironment) {
                _serviceScopeFactory = serviceScopeFactory;
                _partialsFileProvider = new FileProviderMapper(webHostEnvironment.ContentRootFileProvider, "Views");
            }

            public IDirectoryContents GetDirectoryContents(string subpath) => throw new NotImplementedException();

            public IFileInfo GetFileInfo(string subpath) {
                using(var scoped = _serviceScopeFactory.CreateScope()) {
                    var rep    = scoped.ServiceProvider.GetService<IRepository<WebPageEntity, int>>();
                    var signal = scoped.ServiceProvider.GetService<ISignal>();
                    var fluidRender = scoped.ServiceProvider.GetService<FluidRendering>() as MagicubeFluidRendering;
                    var result = new LiquidViewInfo(subpath, signal, rep);
                    if (result.Exists) {
                        result.ChangeToken.RegisterChangeCallback(obj => {
                            fluidRender.ExpireView(subpath, this);
                        }, null);
                        return result;
                    }

                    return _partialsFileProvider.GetFileInfo(subpath);
                }
            }

            public IChangeToken Watch(string filter) {
                if (_content.TryGetValue(filter, out var fileInfo)) {
                    return fileInfo.ChangeToken;
                }

                return NullChangeToken.Singleton;
            }
        }

        public class LiquidViewInfo : IFileInfo {
            private readonly IRepository<WebPageEntity, int> _rep;

            private bool           _exists;
            private string         _viewPath;
            private byte[]         _viewContent;
            private DateTimeOffset _lastModified;

            public LiquidViewInfo(string viewPath, ISignal signal, IRepository<WebPageEntity, int> rep) {
                _rep      = rep;
                _viewPath = viewPath.TrimEnd(".liquid").ToLower();
                ChangeToken = signal.GetToken(_viewPath);
                GetView();
            }
            
            public string         Name         => Path.GetFileName(_viewPath);

            public bool           Exists       => _exists;

            public bool           IsDirectory  => false;

            public DateTimeOffset LastModified => _lastModified;

            public string         PhysicalPath => null;

            public IChangeToken   ChangeToken  { get; }

            public long           Length {
                get {
                    using (var stream = new MemoryStream(_viewContent)) {
                        return stream.Length;
                    }
                }
            }

            public Stream CreateReadStream() {
                return new MemoryStream(_viewContent);
            }

            private void GetView() {
                var entity = _rep.Get(x => x.Path == _viewPath);
                if (entity != null) {
                    _exists = true;
                    _viewContent = entity.Body.ToByte();
                    _lastModified = DateTimeOffset.FromUnixTimeSeconds(entity.UpdateAt.GetValueOrDefault(DateTimeOffset.Now.ToUnixTimeSeconds()));
                }
            }
        }
    }

    namespace MvcViewEngine {
        public class MagicubeFluidView : IView {
            private string _path;
            private MagicubeFluidRendering _fluidRendering;

            public MagicubeFluidView(string path, MagicubeFluidRendering fluidRendering) {
                _path = path;
                _fluidRendering = fluidRendering;
            }

            public string Path {
                get {
                    return _path;
                }
            }

            public async Task RenderAsync(ViewContext context) {
                await _fluidRendering.RenderAsync(
                    context.Writer,
                    Path,
                    context.ViewData.Model,
                    context.ViewData,
                    context.ModelState,
                    context.HttpContext.RequestServices
                    );
            }
        }

        public class MagicubeFluidViewEngine : IFluidViewEngine {
            private MagicubeFluidRendering _fluidRendering;
            private readonly IWebHostEnvironment _hostingEnvironment;
            private const string ControllerKey = "controller";
            private const string AreaKey = "area";
            private FluidMvcViewOptions _options;

            public MagicubeFluidViewEngine(
                FluidRendering fluidRendering,
                IOptions<FluidMvcViewOptions> optionsAccessor,
                IWebHostEnvironment hostingEnvironment
                ) {
                _options = optionsAccessor.Value;
                _fluidRendering = fluidRendering as MagicubeFluidRendering;
                _hostingEnvironment = hostingEnvironment;

                _fluidRendering.NotNull();
            }

            public ViewEngineResult FindView(ActionContext context, string viewName, bool isMainPage) {
                return LocatePageFromViewLocations(context, viewName);
            }

            private ViewEngineResult LocatePageFromViewLocations(ActionContext actionContext, string viewName) {
                var controllerName = GetNormalizedRouteValue(actionContext, ControllerKey);
                var areaName = GetNormalizedRouteValue(actionContext, AreaKey);

                var fileProvider = _options.ViewsFileProvider ?? _hostingEnvironment.ContentRootFileProvider;

                var checkedLocations = new List<string>();

                foreach (var location in _options.ViewsLocationFormats) {
                    var view = String.Format(location, viewName, controllerName, areaName);

                    if (fileProvider.GetFileInfo(view).Exists) {
                        return ViewEngineResult.Found(viewName, new MagicubeFluidView(view, _fluidRendering));
                    }

                    checkedLocations.Add(view);
                }

                return ViewEngineResult.NotFound(viewName, checkedLocations);
            }

            public ViewEngineResult GetView(string executingFilePath, string viewPath, bool isMainPage) {
                var applicationRelativePath = GetAbsolutePath(executingFilePath, viewPath);

                if (!(IsApplicationRelativePath(viewPath) || IsRelativePath(viewPath))) {
                    return ViewEngineResult.NotFound(applicationRelativePath, Enumerable.Empty<string>());
                }

                return ViewEngineResult.Found("Default", new MagicubeFluidView(applicationRelativePath, _fluidRendering));
            }

            public string GetAbsolutePath(string executingFilePath, string pagePath) {
                if (pagePath.IsNullOrEmpty()) {
                    return pagePath;
                }

                if (IsApplicationRelativePath(pagePath)) {
                    return pagePath.Replace("~/", "");
                }

                if (!IsRelativePath(pagePath)) {
                    return pagePath;
                }

                if (executingFilePath.IsNullOrEmpty()) {
                    return "/" + pagePath;
                }

                var index = executingFilePath.LastIndexOf('/');
                Debug.Assert(index >= 0);
                return executingFilePath.Substring(0, index + 1) + pagePath;
            }

            private static bool IsApplicationRelativePath(string name) {
                Debug.Assert(!name.IsNullOrEmpty());
                return name[0] == '~' || name[0] == '/';
            }

            private static bool IsRelativePath(string name) {
                Debug.Assert(!name.IsNullOrEmpty());
                return name.EndsWith(Constants.ViewExtension, StringComparison.OrdinalIgnoreCase);
            }

            public static string GetNormalizedRouteValue(ActionContext context, string key) {
                context.NotNull(nameof(context));
                key.NotNull(nameof(key));

                if (!context.RouteData.Values.TryGetValue(key, out object routeValue)) {
                    return null;
                }

                var actionDescriptor = context.ActionDescriptor;
                string normalizedValue = null;

                if (actionDescriptor.RouteValues.TryGetValue(key, out string value) && !value.IsNullOrEmpty()) {
                    normalizedValue = value;
                }

                var stringRouteValue = routeValue?.ToString();
                if (string.Equals(normalizedValue, stringRouteValue, StringComparison.OrdinalIgnoreCase)) {
                    return normalizedValue;
                }

                return stringRouteValue;
            }
        }

        public class MagicubeFluidRendering : FluidRendering {
            private readonly IDictionary _innerCache;
            private readonly FluidViewRenderer _fluidViewRenderer;
            public MagicubeFluidRendering(IOptions<FluidMvcViewOptions> optionsAccessor, IWebHostEnvironment hostingEnvironment)
                : base(optionsAccessor, hostingEnvironment) {
                _fluidViewRenderer = new FluidViewRenderer(optionsAccessor.Value);
                _innerCache        = (IDictionary)_fluidViewRenderer.GetType().GetField("_cache", BindingFlags.NonPublic | BindingFlags.GetField | BindingFlags.Instance).GetValue(_fluidViewRenderer);
            }

            public async Task RenderAsync(TextWriter writer, string path, object model, ViewDataDictionary viewData, ModelStateDictionary modelState, IServiceProvider serviceProvider) {
                TemplateContext context = model == null ? new MagicubeLiquidTemplateContext(serviceProvider) : new MagicubeLiquidTemplateContext(model, serviceProvider);
                context.SetValue("ViewData", viewData);
                context.SetValue("ModelState", modelState);
                context.SetValue("Model", model);

                await _fluidViewRenderer.RenderViewAsync(writer, path, context);
            }

            public void ExpireView(string subPath, IFileProvider fileProvider) {
                if (_innerCache.Contains(fileProvider)) {
                    var cache = _innerCache[fileProvider];
                    var templateCache = (IDictionary<string, IFluidTemplate>)cache.GetType().GetField("TemplateCache").GetValue(cache);
                    templateCache.Remove(subPath);
                }
            }
        }
    }

    namespace Statements {
        public class RenderWidgetStatement : Statement {
            private readonly FluidParser _parser;
            private IFluidTemplate _template;

            public string Widget { get; }
            public string Entity { get; }

            public RenderWidgetStatement(FluidParser parser, string widget, string value) {
                Widget = widget;
                Entity = value;
                _parser = parser;
            }

            public override async ValueTask<Completion> WriteToAsync(TextWriter writer, TextEncoder encoder, TemplateContext context) {
                context.IncrementSteps();
                var ctx = context as MagicubeLiquidTemplateContext;
                if (ctx == null) throw new Exception($"render need MagicubeFluidTemplateContext");

                var widgetDataProvider = ctx.Services.GetRequiredService<IWidgetService>();
                var widget = await widgetDataProvider.GetWidget(Widget);
                //1、 get Widget Content
                var template = widget.Content;
                //2、 get data by url & widget & entity
                object model = new { };

                if (_template == null) {
                    if (!_parser.TryParse(template, out _template, out var errors)) {
                        throw new ParseException(errors);
                    }
                }

                try {
                    context.EnterChildScope();
                    await _template.RenderAsync(writer, encoder, new TemplateContext(model, context.Options));
                } finally {
                    context.ReleaseScope();
                }

                return Completion.Normal;
            }
        }
    }

}
