using System.Linq;
using System.Reflection;
using Swashbuckle.AspNetCore.SwaggerGen;
using Microsoft.OpenApi.Models;
using System.Xml.Serialization;
using System.Xml;
using System.Xml.Linq;
using System.IO;
using System.Collections.Generic;
using System;
using Sintef.Scoop.Utilities;
using Newtonsoft.Json;
using System.Runtime.Serialization;
using Newtonsoft.Json.Linq;

namespace Sintef.Pgo.REST.Middleware
{
	/// <summary>
	/// A Swagger filter that fixes a shortcoming in the documentation:
	/// If a model class has a property that also is a model class, the property's documentation is
	/// not included. We fix this by adding the property's documentation to the description of the model class.
	/// 
	/// Also, we include the documentation of each enum value, which are normally omitted, in the enum's documentation.
	/// 
	/// Also, we correct the schema type for JObject properties, which Swagger does not recognize.
	/// </summary>
	public class FixDocFilter : ISchemaFilter
	{
		/// <summary>
		/// Documentation for all our model types and their properties, by full name
		/// </summary>
		Dictionary<string, string> _docs = new();

		/// <summary>
		/// Initializes the filter
		/// </summary>
		/// <param name="xmlPaths">The paths to the xml documentation files</param>
		public FixDocFilter(IEnumerable<string> xmlPaths)
		{
			foreach (var path in xmlPaths)
			{
				try
				{
					// Parse the xml file

					using var stream = File.OpenRead(path);
					var root = XElement.Load(stream);

					// Extract documentation

					foreach (var member in root.Descendants("member"))
					{
						try
						{
							string name = member.Attribute("name").Value;

							//// Only look in our model namespace
							//if (!name.Contains(".Models."))
							//	continue;

							// Include types, properties and fields
							if (!name.StartsWith("P:") && !name.StartsWith("T:") && !name.StartsWith("F:"))
								continue;

							// Extract member documentation.
							// Don't use XNode.Value, as it removes XML nodes such as <see>
							string doc = member.Element("summary").Nodes().Select(n => n switch
							{
								XText text => text.Value,
								XElement element when element.Name == "see" => element.FirstAttribute.Value.Substring(2),
								_ => "(??)"
							}).Join("");

							if (string.IsNullOrWhiteSpace(doc))
								continue;

							// Remove the margin created by pretty-formatting of the XML documentation file
							string trimmed = doc.TrimStart();
							string lfAndMargin = doc.Substring(0, doc.Length - trimmed.Length);
							doc = doc.Replace(lfAndMargin, "\n");

							_docs.Add(name.Substring(2), doc.Trim());
						}
						catch (Exception) { }
					}
				}
				catch (Exception) { }
			}
		}

		/// <summary>
		/// Fixes the documentation for the given schema
		/// </summary>
		/// <param name="schema"></param>
		/// <param name="context"></param>
		public void Apply(OpenApiSchema schema, SchemaFilterContext context)
		{
#if !DEBUG
			try
#endif
			{
				// Check each property of the documented type

				var properties = context.Type.GetProperties();

				List<string> propertyDescriptions = new();

				foreach (var property in properties)
				{
					Type propertyType = property.PropertyType;

					// If property is nullable, get the non-nullable type
					if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
						propertyType = propertyType.GenericTypeArguments[0];

					if (_docs.ContainsKey(propertyType.FullName))
					{
						// The property's type is a model class

						if (_docs.TryGetValue($"{context.Type.FullName}.{property.Name}", out string propertyDoc))
						{
							// We've found the property's documentation.

							// Format as item in bullet list
							if (JsonName(property) is string jsonName)
								propertyDescriptions.Add($" - {jsonName}: {propertyDoc.Replace("\n", "\n   ")}");
						}
					}

					if (property.PropertyType == typeof(JObject))
					{
						// Find the corresponding schema property and give it object type.
						// This makes Swagger produce a more correct example value for the schema.
						if (schema.Properties.TryGetValue(JsonName(property), out var propertySchema))
							propertySchema.Type = "object";
					}
				}

				// Add property documentation to the schema description (in a bullet list)
				if (propertyDescriptions.Any())
					schema.Description += "\n\n" + propertyDescriptions.Join("\n");

				if (context.Type.IsEnum)
				{
#if DEBUG
					if (schema.Type != "string")
						throw new Exception($"\nEnum type {context.Type.Name} is not documented as a strings. Please add the attribute\n" +
							$"[JsonConverter(typeof(StringEnumConverter))] to make the Swagger documentation nicer.\n");
#endif
					var enumValues = context.Type.GetEnumValues();

					List<string> valueDescriptions = new();

					foreach (object value in enumValues)
					{
						MemberInfo memberInfo = context.Type.GetMember(value.ToString()).First();

						if (_docs.TryGetValue($"{context.Type.FullName}.{memberInfo.Name}", out string enumValueDoc))
						{
							// We've found the enum value's documentation.

							// Format as item in bullet list
							if (JsonName(memberInfo) is string jsonName)
								valueDescriptions.Add($" - \"{jsonName}\": {enumValueDoc.Replace("\n", "\n   ")}");
						}
					}

					// Add values documentation to the schema description (in a bullet list)
					if (valueDescriptions.Any())
						schema.Description += "\n\n" + valueDescriptions.Join("\n");
				}
			}
#if! DEBUG
			catch (Exception)
			{
				throw;
			}
#endif
		}

		/// <summary>
		/// Returns the name used in Json for the given property or enum value, or null if it's excluded
		/// </summary>
		private string JsonName(MemberInfo member)
		{
			if (member.GetCustomAttribute<JsonIgnoreAttribute>() != null)
				return null;

			if (member.GetCustomAttribute<EnumMemberAttribute>() is EnumMemberAttribute enumAttr)
				return enumAttr.Value;

			if (member.GetCustomAttribute<JsonPropertyAttribute>() is JsonPropertyAttribute propertyAttr)
				return propertyAttr.PropertyName;

			string name = member.Name;
			string camelCasedName = char.ToLower(name[0]) + name.Substring(1);
			return camelCasedName;
		}
	}
}
