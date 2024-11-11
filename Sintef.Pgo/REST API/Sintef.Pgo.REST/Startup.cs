using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Sintef.Pgo.Server;
using Sintef.Pgo.REST.Hubs;
using Sintef.Pgo.REST.Middleware;
using Swashbuckle.AspNetCore.SwaggerGen;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Sintef.Pgo.DataContracts;

namespace Sintef.Pgo.REST
{
	/// <summary>
	/// This class configures the web server at startup.
	/// </summary>
	public class Startup
	{
		/// <summary>
		/// Identifies the Json API for Swagger
		/// </summary>
		public static string JsonApiV1 { get; } = "v1_json";

		/// <summary>
		/// Identifies the CIM API for Swagger
		/// </summary>
		public static string CimApiV1 { get; } = "v1_cim";

		/// <summary>
		/// Configuration parameters
		/// </summary>
		public IConfiguration Configuration { get; }

		/// <summary>
		/// The hosting environment
		/// </summary>
		public IWebHostEnvironment Environment { get; }

		/// <summary>
		/// Initializes the class
		/// </summary>
		public Startup(IConfiguration configuration, IWebHostEnvironment environment)
		{
			Configuration = configuration;
			Environment = environment;
		}

		/// <summary>
		/// This method gets called by the runtime and adds services to the container. 
		/// </summary>
		/// <param name="services"></param>
		public void ConfigureServices(IServiceCollection services)
		{
			IMultiUserServer server = new MultiUserServer();
			services.AddSingleton(server);

			services.AddSingleton(new SessionCleanerUpper(server));
			services.AddHostedService(s => s.GetService<SessionCleanerUpper>());

			services.AddSignalR();

			services.AddCors(options =>
			{
				options.AddDefaultPolicy(
					builder =>
					{
						builder.SetIsOriginAllowed(host => true)
							.AllowAnyHeader()
							.AllowAnyMethod()
							.AllowCredentials();
					});
			});

			services.AddMvc()
				.AddNewtonsoftJson();

			services.AddSwaggerGenNewtonsoftSupport();
			services.AddSwaggerGen(c =>
			{
				c.SwaggerDoc(JsonApiV1, new OpenApiInfo { Title = "Power Grid Optimizer Json API", Version = "v1" });
				c.SwaggerDoc(CimApiV1, new OpenApiInfo { Title = "Power Grid Optimizer CIM API", Version = "v1" });

				// Add documentation, including the models in Sintef.Pgo.DataContracts
				var paths = AddDocumentationFromAssemblies(c, Assembly.GetExecutingAssembly().GetName().Name, "Sintef.Pgo.DataContracts").ToList();

				// Add schema documentation for types not directly referenced in the API
				c.DocumentFilter<IncludeCimTypesFilter>();

				// Add documentation missed by Swagger
				c.SchemaFilter<FixDocFilter>(new[] { paths });

				// Sort controller actions to the correct document
				c.DocInclusionPredicate((string apiName, ApiDescription apiDescription) =>
				{
					return apiDescription.RelativePath.StartsWith("api/cim") == (apiName == CimApiV1);
				});
			});

			// Configure quotas

			UserQuotas defaultQuotas = UserQuotas.NoLimits();
			if (Configuration.GetValue<string>("PgoDefaultQuotas") == "ForDemo")
			{
				defaultQuotas = UserQuotas.ForDemo();
			}

			services.AddSingleton<IUserQoutaProvider>(new UserQoutaProvider(defaultQuotas));

			// Configure authentication

			bool serverIsMultiUser = Configuration.GetValue<string>("PgoMultiUser") == "True";

			services
				.AddAuthentication(options => options.DefaultScheme = CookieAuthenticationHandler.SchemeName)
				.AddScheme<CookieAuthenticationHandlerOptions, CookieAuthenticationHandler>(CookieAuthenticationHandler.SchemeName,
					options => { options.MultiUser = serverIsMultiUser; });
		}

		/// <summary>
		/// This method gets called by the runtime and configures the HTTP request pipeline.
		/// </summary>
		public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
		{
			if (env.IsDevelopment())
			{
				app.UseDeveloperExceptionPage();
			}

			app.UsePathBase(
				env.IsDevelopment()
					? new PathString(Configuration["PathBaseDev"])
					: new PathString(Configuration["PathBase"])
			);

			app.UseAuthentication();
			app.UseRouting();
			app.UseAuthorization();
			app.UseDefaultFiles();
			app.UseStaticFiles(new StaticFileOptions
			{
				OnPrepareResponse = staticFilesResponseContext =>
				{
					// When accessing index.html, require authentication and prevent caching
					if (staticFilesResponseContext.Context.Request.Path.Value == "/index.html")
					{
						if (!staticFilesResponseContext.Context.User.Identity.IsAuthenticated)
						{
							staticFilesResponseContext.Context.ChallengeAsync().GetAwaiter().GetResult();
						}
						staticFilesResponseContext.Context.Response.Headers.Append("Cache-Control", "no-cache");
					}
				}
			});
			app.UseCors();

			app.UseSwagger();
			// Require authentication to access the Swagger UI
			app.UseWhen(context => context.Request.Path.StartsWithSegments("/swagger"), app =>
			{
				app.Use(async (context, next) =>
				{
					if (!context.User.Identity.IsAuthenticated)
					{
						await context.ChallengeAsync();
					}
					else
					{
						await next();
					}
				});
			});
			app.UseSwaggerUI(c =>
			{
				string swaggerJsonBasePath = string.IsNullOrWhiteSpace(c.RoutePrefix) ? "." : "..";
				c.SwaggerEndpoint($"{swaggerJsonBasePath}/swagger/{JsonApiV1}/swagger.json", "Power Grid Optimizer Json API V1");
				c.SwaggerEndpoint($"{swaggerJsonBasePath}/swagger/{CimApiV1}/swagger.json", "Power Grid Optimizer CIM API V1");
			});

			app.UseEndpoints(builder =>
			{
				builder.MapControllers()
					.RequireAuthorization();
				builder.MapHub<SolutionStatusHub>("/solutionStatusHub")
					.RequireAuthorization();
			});


		}

		/// <summary>
		/// Adds xml documentation from the named assemblies to Swagger
		/// </summary>
		private List<string> AddDocumentationFromAssemblies(SwaggerGenOptions c, params string[] assemblyNames)
		{
			List<string> paths = new();

			foreach (var assemblyName in assemblyNames)
			{
				string xmlPath = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.xml");
				c.IncludeXmlComments(xmlPath);
				paths.Add(xmlPath);
			}

			return paths;
		}
	}
}
