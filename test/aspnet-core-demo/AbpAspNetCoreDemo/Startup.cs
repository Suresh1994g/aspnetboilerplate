﻿using System;
using System.IO;
using System.Linq;
using System.Threading;
using Abp.AspNetCore;
using Abp.AspNetCore.Configuration;
using Abp.AspNetCore.Mvc.Antiforgery;
using Abp.AspNetCore.Mvc.Extensions;
using Abp.Castle.Logging.Log4Net;
using Abp.Dependency;
using Abp.Json;
using Abp.PlugIns;
using AbpAspNetCoreDemo.Controllers;
using AbpAspNetCoreDemo.Core.Domain;
using Castle.Core.Logging;
using Castle.Facilities.Logging;
using Castle.MicroKernel.ModelBuilder.Inspectors;
using Castle.MicroKernel.SubSystems.Conversion;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.OData;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OData.ModelBuilder;
using Newtonsoft.Json.Serialization;

namespace AbpAspNetCoreDemo
{
    public class Startup
    {
        private readonly IWebHostEnvironment _env;

        public static readonly AsyncLocal<IocManager> IocManager = new AsyncLocal<IocManager>();

        public Startup(IWebHostEnvironment env)
        {
            _env = env;
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();
            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton(Configuration);

            //Some test classes
            services.AddTransient<MyTransientClass1>();
            services.AddTransient<MyTransientClass2>();
            services.AddScoped<MyScopedClass>();

            //Add framework services
            services.AddMvc(options =>
            {
                options.Filters.Add(new AbpAutoValidateAntiforgeryTokenAttribute());
            }).AddNewtonsoftJson(options =>
            {
                options.SerializerSettings.ContractResolver = new AbpMvcContractResolver(IocManager.Value)
                {
                    NamingStrategy = new CamelCaseNamingStrategy()
                };
            }).AddRazorRuntimeCompilation().AddOData(opts =>
            {
                var builder = new ODataConventionModelBuilder();
                builder.EntitySet<Product>("Products").EntityType.Expand().Filter().OrderBy().Page().Select();
                builder.EntitySet<Product>("ProductsDto").EntityType.Expand().Filter().OrderBy().Page().Select();
                var edmModel = builder.GetEdmModel();
                
                opts.AddRouteComponents("odata", edmModel);
            });

            //Configure Abp and Dependency Injection. Should be called last.
            return services.AddAbp<AbpAspNetCoreDemoModule>(options =>
            {
                options.IocManager = IocManager.Value ?? new IocManager();

                string plugDllInPath = "";
#if DEBUG
                plugDllInPath = Path.Combine(_env.ContentRootPath,
                    @"..\AbpAspNetCoreDemo.PlugIn\bin\Debug\net6.0\AbpAspNetCoreDemo.PlugIn.dll");
#else
                plugDllInPath = Path.Combine(_env.ContentRootPath,
                    @"..\AbpAspNetCoreDemo.PlugIn\bin\Release\net6.0\AbpAspNetCoreDemo.PlugIn.dll");
#endif
                if (!File.Exists(plugDllInPath))
                {
                    throw new FileNotFoundException("There is no plugin dll file in the given path.", plugDllInPath);
                }

                options.PlugInSources.Add(new AssemblyFileListPlugInSource(plugDllInPath));

                //Configure Log4Net logging
                options.IocManager.IocContainer.AddFacility<LoggingFacility>(
                    f => f.UseAbpLog4Net().WithConfig("log4net.config")
                );

                var propInjector = options.IocManager.IocContainer.Kernel.ComponentModelBuilder
                    .Contributors
                    .OfType<PropertiesDependenciesModelInspector>()
                    .Single();

                options.IocManager.IocContainer.Kernel.ComponentModelBuilder.RemoveContributor(propInjector);
                options.IocManager.IocContainer.Kernel.ComponentModelBuilder.AddContributor(new AbpPropertiesDependenciesModelInspector(new DefaultConversionManager()));
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory)
        {
            app.UseAbp(); //Initializes ABP framework. Should be called first.

            // Return IQueryable from controllers
            app.UseUnitOfWork(options =>
            {
                options.Filter = httpContext => httpContext.Request.Path.Value.StartsWith("/odata");
            });

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseBrowserLink();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            app.UseStaticFiles();
            app.UseEmbeddedFiles(); //Allows to expose embedded files to the web!

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute("defaultWithArea", "{area}/{controller=Home}/{action=Index}/{id?}");
                endpoints.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
                endpoints.MapRazorPages();

                app.ApplicationServices.GetRequiredService<IAbpAspNetCoreConfiguration>().EndpointConfiguration.ConfigureAllEndpoints(endpoints);
            });
        }
    }
}
