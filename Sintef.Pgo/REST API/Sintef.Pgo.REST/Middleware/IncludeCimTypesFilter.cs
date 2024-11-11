using System.Linq;
using Swashbuckle.AspNetCore.SwaggerGen;
using Microsoft.OpenApi.Models;
using System.Collections.Generic;
using Sintef.Pgo.DataContracts;
using Sintef.Pgo.Cim;
using System.Text.RegularExpressions;

namespace Sintef.Pgo.REST.Middleware
{
	/// <summary>
	/// A document filter that adds documentation to Swagger for types used in the CIM API,
	/// but not directly referenced by any Controller.
	/// 
	/// Also adds links to schemas in the documentation 
	/// </summary>
	public class IncludeCimTypesFilter : IDocumentFilter
	{
		/// <inheritdoc/>
		public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
		{
			if (context.DocumentName == Startup.CimApiV1)
			{
				AddSchemaFor<CimNetwork>();
				AddSchemaFor<CimDemands>();
				AddSchemaFor<CimConfiguration>();
				AddSchemaFor<CimPeriodSolution>();

				AddSchemaFor<SynchronousMachine>();
				AddSchemaFor<Disconnector>();
				AddSchemaFor<LoadBreakSwitch>();
				AddSchemaFor<Breaker>();
				AddSchemaFor<DisconnectingCircuitBreaker>();
				AddSchemaFor<Fuse>();
				AddSchemaFor<Sectionalizer>();
				AddSchemaFor<Recloser>();
				AddSchemaFor<Jumper>();
				AddSchemaFor<GroundDisconnector>();
			}

			List<string> schemaNames = swaggerDoc.Components.Schemas.Keys.ToList();

			// Add links in operation decriptions
			foreach (var path in swaggerDoc.Paths)
			{
				foreach (var op in path.Value.Operations)
				{
					op.Value.Description = AddModelLinks(op.Value.Description);
				}
			}

			// Add links in schema descriptions
			foreach (var schema in swaggerDoc.Components.Schemas)
			{
				schema.Value.Description = AddModelLinks(schema.Value.Description);

				foreach (var propertySchema in schema.Value.Properties)
					propertySchema.Value.Description = AddModelLinks(propertySchema.Value.Description);
			}


			//-- local functions

			// Adds T to the documentation
			void AddSchemaFor<T>()
			{
				context.SchemaGenerator.GenerateSchema(typeof(T), context.SchemaRepository);
			}

			// Updates the given documentation string with Markdown links to schemas
			// E.g.   Sintef.x.y.z.Type   ->   [Type](#model-Type)
			string AddModelLinks(string description)
			{
				if (description == null)
					return null;

				// First, fix links to any type that has a known schema, by exact name
				foreach (string name in schemaNames)
				{
					description = Regex.Replace(description,
						$"\\bSintef\\.(\\w+\\.)*{name}\\b",
						$"[{name}](#model-{name})");
				}

				// Fix link to anything that looks like a plural of a type
				description = Regex.Replace(description,
					"\\bSintef\\.(\\w+\\.)*(\\w+)s\\b",
					"[$2](#model-$2)s");

				// Fix link to anything remaining that looks like a type
				description = Regex.Replace(description,
					"\\bSintef\\.(\\w+\\.)*(\\w+)\\b",
					"[$2](#model-$2)");

				return description;
			}
		}
	}
}
