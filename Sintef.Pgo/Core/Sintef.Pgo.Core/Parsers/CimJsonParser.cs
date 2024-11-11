using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using VDS.RDF;
using VDS.RDF.Parsing;
using Sintef.Scoop.Utilities;
using System.Reflection;
using UnitsNet;
using Sintef.Pgo.Cim;
using System.Linq.Expressions;

using Voltage = UnitsNet.ElectricPotential;
using Resistance = UnitsNet.ElectricResistance;
using Conductance = UnitsNet.ElectricConductance;
using CurrentFlow = UnitsNet.ElectricCurrent;
using ActivePower = UnitsNet.Power;
using ReactivePower = UnitsNet.ReactivePower;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Globalization;

namespace Sintef.Pgo.Core.IO
{
	/// <summary>
	/// Parses CIM data on JSON-LD format into our internal CIM modelling 
	/// classes in the namespace <see cref="Sintef.Pgo.Cim"/>.
	/// 
	/// The parsing happens in two stages.
	/// 1. Data from one or more files is parsed, using the nuget package "dotNetRdf".
	///   The result of this is a collection of RDF Graphs. Each graph is a collection
	///   of Subject-Predicate-Object triples.
	/// 2. The RDF graphs are converted into CIM objects using reflection.
	///   First, objects are created from triples of the form
	///   (objectId)-isType-(class).
	///   Then, property values a set from triples of the form
	///   (objectId)-(attributeName)-(attributeValue).
	///   
	/// References between CIM objects are resolved correctly. Also, lists of referencing
	/// objects will be filled if present. For example, since <see cref="ConnectivityNode"/> contains
	/// the property <code>public List&lt;Terminal> Terminals</code>, every time a <see cref="Terminal"/>
	/// refers to a <see cref="ConnectivityNode"/>, that <see cref="Terminal"/> will be added to the list
	/// in the correct <see cref="ConnectivityNode"/>.
	/// </summary>
	public class CimJsonParser
	{
		/// <summary>
		/// The units profile used by this parser
		/// </summary>
		public ICimUnitsProfile Units { get; }

		/// <summary>
		/// The number of RDF graphs that have been read by this parser
		/// </summary>
		public int GraphCount => _store.Graphs.Count();

		/// <summary>
		/// The total number of RDF triples in all graphs read by this parser
		/// </summary>
		public int TripleCount => _store.Triples.Count();

		/// <summary>
		/// The total number of different object types in the RDF data read by this parser
		/// </summary>
		public int ObjectTypeCount => TypeSpecs().GroupBy(pair => (pair.TypeUri, pair.TypeUri.Fragment)).Count();

		#region Private data members

		/// <summary>
		/// The JsonLd parser
		/// </summary>
		private JsonLdParser _parser = new();

		/// <summary>
		/// Contains the RDF graphs
		/// </summary>
		private TripleStore _store = new();

		/// <summary>
		/// All CIM objects that have been created, by URI
		/// </summary>
		private Dictionary<Uri, IdentifiedObject> _created = new();

		/// <summary>
		/// The URIs of all CIM objects that have been created
		/// </summary>
		private Dictionary<IdentifiedObject, Uri> _uriOfObject= new();

		/// <summary>
		/// The URIs of all CIM objects that have been created, by their MRID
		/// </summary>
		private Dictionary<string, Uri> _uriOfMrid = new();

		/// <summary>
		/// The names of CIM types that were found in the RDF but do not exist
		/// among our modelling classes (yet)
		/// </summary>
		private HashSet<string> _notFoundTypes = new();

		/// <summary>
		/// The names of CIM attributes that were found in the RDF but do not exist
		/// in our corresponding modelling class
		/// </summary>
		private HashSet<string> _missingProperties = new();

		/// <summary>
		/// Records objects referred to by RDF triples but not found in the data.
		/// (attribute name) -> (missing object count)
		/// </summary>
		private Dictionary<string, int> _missingObjectCounts = new();

		/// <summary>
		/// Records objects referred to by RDF triples but not found in the data.
		/// (attribute name) -> (URI of one missing object)
		/// </summary>
		private Dictionary<string, Uri> _missingObjectExamples = new();

		#endregion

		/// <summary>
		/// Initializes a <see cref="CimJsonParser"/>
		/// </summary>
		/// <param name="unitsProfile">The units profile to use</param>
		public CimJsonParser(ICimUnitsProfile unitsProfile)
		{
			Units = unitsProfile ?? throw new ArgumentNullException(nameof(unitsProfile));
		}

		#region public methods

		/// <summary>
		/// Reads the JSON-LD data in all files with name pattern "*{profile}.jsonld" in the given folder,
		/// or all files with extension ".jsonld" if <paramref name="profile"/> is null.
		/// </summary>
		public void ParseAllJsonFiles(string folderPath, string profile = null)
		{
			string pattern = "*.jsonld";
			if (profile != null)
				pattern = $"*{profile}.jsonld";

			var fileNames = new DirectoryInfo(folderPath).EnumerateFiles(pattern)
				.Select(f => f.FullName)
				.ToList();

			foreach (var file in fileNames)
				Parse(file);
		}

		/// <summary>
		/// Creates a parser and reads all network data in the given folder,
		/// assuming the files are named according to the convention used in DIGIN
		/// </summary>
		/// <param name="folder">The folder to read files from</param>
		/// <param name="unitsProfile">The units profile to use. If null, uses DIGIN units.</param>
		public static CimJsonParser ParseDiginFormatNetworkData(string folder, ICimUnitsProfile unitsProfile = null)
		{
			var parser = new CimJsonParser(unitsProfile ?? new DiginUnitsProfile());

			parser.ParseAllJsonFiles(folder, "BaseVoltage_RD");
			parser.ParseAllJsonFiles(folder, "EQ");
			parser.ParseAllJsonFiles(folder, "BM");

			parser.CreateCimObjects();

			return parser;
		}

		/// <summary>
		/// Reads the JSON-LD data in the given file
		/// </summary>
		public void Parse(string fileName)
		{
			_parser.Load(_store, fileName);
		}

		/// <summary>
		/// Reads the JSON-LD data in the given stream
		/// </summary>
		public void Parse(Stream stream)
		{
			Parse(new StreamReader(stream));
		}

		/// <summary>
		/// Reads the JSON-LD data in the given text reader
		/// </summary>
		public void Parse(TextReader reader)
		{
			_parser.Load(_store, reader);
		}

		/// <summary>
		/// Reads the JSON-LD data represented by the given JObject
		/// </summary>
		public void Parse(JObject jObject)
		{
			// Unfortunately, we know of no better method that going via a string serialization...
			string json = JsonConvert.SerializeObject(jObject);
			Parse(new StringReader(json));
		}

		/// <summary>
		/// Creates CIM objects by processing the RDF data that has been read
		/// </summary>
		public void CreateCimObjects()
		{
			// Create objects

			foreach (var (objectUri, typeUri) in TypeSpecs())
			{
				if (objectUri.AbsoluteUri.StartsWith("urn:uuid:_"))
					// We interpret this as a reference to an object without direction to create it
					continue;

				if (Uris.CimName(typeUri) is not string className)
					// Not a CIM object
					continue;

				if (CimType(className) is not Type type)
					// We're not modelling this CIM type (yet)
					continue;

				var newObject = (IdentifiedObject)type.GetConstructor(Array.Empty<Type>()).Invoke(null);

				_created[objectUri] = newObject;
				_uriOfObject[newObject] = objectUri;
			}

			// Populate object attributes

			foreach (var (objectUri, objectToFill) in _created)
			{
				foreach (var (name, value) in Attributes(objectUri))
				{
					try
					{
						SetAttribute(objectToFill, name, value);
					}
					catch (Exception ex)
					{
						throw new Exception($"Error parsing attribute '{name}' of CIM object with URI {objectUri} (value = {value}):\n{ex.Message}");
					}
				}

				if (objectToFill.MRID != null)
					_uriOfMrid[objectToFill.MRID] = objectUri;
			}
		}

		/// <summary>
		/// Enumerates the CIM objects of the specified type that have been created
		/// </summary>
		/// <typeparam name="T">The type of CIM object</typeparam>
		public IEnumerable<T> CreatedObjects<T>() => _created.Values.OfType<T>();

		/// <summary>
		/// Returns the CIM object with the specified URI, or null if there is none
		/// </summary>
		public IdentifiedObject ObjectWithUri(string uri) => ObjectWithUri(new Uri(uri));

		/// <summary>
		/// Returns the CIM object with the specified URI, or null if there is none
		/// </summary>
		public IdentifiedObject ObjectWithUri(Uri uri) => _created.TryGetValue(uri, out var value) ? value : null;

		/// <summary>
		/// Returns the URI of the given <paramref name="identifiedObject"/>
		/// </summary>
		public Uri UriOf(IdentifiedObject identifiedObject) => _uriOfObject[identifiedObject];

		/// <summary>
		/// Returns the URI corresponding to the given MRID
		/// </summary>
		public Uri UriFor(string mrid) => _uriOfMrid[mrid];

		/// <summary>
		/// Returns the single CIM object of the specified type with the given <see cref="IdentifiedObject.MRID"/>
		/// </summary>
		public T CreatedObject<T>(string mrid) where T : IdentifiedObject
		{
			return CreatedObjects<T>().Single(x => x.MRID == mrid);
		}

		/// <summary>
		/// If <paramref name="identifiedObject"/> has no MRID, assigns it the MRID of
		/// the object with the same Uri found in <paramref name="otherParser"/>.
		/// If no such object is found, sets the MRID from the URI of <paramref name="identifiedObject"/>.
		/// </summary>
		public void FillMridIfMissing(IdentifiedObject identifiedObject, CimJsonParser otherParser)
		{
			if (identifiedObject.MRID != null)
				return;

			var uri = UriOf(identifiedObject);
			if (otherParser.ObjectWithUri(uri) is IdentifiedObject otherObj)
				identifiedObject.MRID = otherObj.MRID;
			else
				identifiedObject.MRID = uri.AbsolutePath.Replace("uuid:", "");
		}

		/// <summary>
		/// Writes a report to the console on how many RDF graphs and triples have
		/// been read and what types CIM objects they represent
		/// </summary>
		public void ReportTriplesAndObjects()
		{
			Console.WriteLine($"{_store.Graphs.Count} graphs:");

			foreach (var g in _store.Graphs)
			{
				Console.WriteLine($"- {g.Triples.Count} triples, name: {g.Name}");
			}

			var groups = TypeSpecs().GroupBy(pair => (pair.TypeUri, pair.TypeUri.Fragment));

			Console.WriteLine();
			Console.WriteLine($"{groups.Count()} object types:");

			foreach (var group in groups)
			{
				Console.WriteLine($"{group.Key.Fragment}: {group.Count()}");
			}
		}

		/// <summary>
		/// Writes a report to the console on how many CIM objects have been created
		/// of what types, and what CIM classes and properties were references in the data
		/// but not present in our modelling classes
		/// </summary>
		public void ReportCreatedObjects()
		{
			ReportCreatedObjects(Console.Out);
		}

		/// <summary>
		/// Writes a report to the given writer on how many CIM objects have been created
		/// of what types, and what CIM classes and properties were references in the data
		/// but not present in our modelling classes
		/// </summary>
		public void ReportCreatedObjects(TextWriter writer)
		{
			writer.WriteLine();
			writer.WriteLine($"Created {_created.Count} objects:");
			var groups = _created.Values.GroupBy(x => x.GetType());
			foreach (var group in groups.OrderBy(x => x.Key.Name))
				writer.WriteLine($"  {group.Count()} {group.Key}");

			writer.WriteLine();
			writer.WriteLine($"{_notFoundTypes.Count} types were found in the data, but do not exist in C#:");
			foreach (var type in _notFoundTypes.OrderBy(x => x))
				writer.WriteLine($"  {type}");

			writer.WriteLine();
			writer.WriteLine($"{_missingProperties.Count} properties were found in the data, but do not exist in C#:");
			foreach (var property in _missingProperties.OrderBy(x => x))
				writer.WriteLine($"  {property}");
		}

		/// <summary>
		/// Writes a report to the console on objects that were referenced in
		/// the RDF data but not found.
		/// </summary>
		public void ReportMissingObjects()
		{
			ReportMissingObjects(Console.Out);
		}

		/// <summary>
		/// Writes a report to the given writer on objects that were referenced in
		/// the RDF data but not found.
		/// </summary>
		public void ReportMissingObjects(TextWriter writer)
		{
			if (!_missingObjectCounts.Any())
				return;

			writer.WriteLine();
			writer.WriteLine("Existing properties whose referenced object was not found:");

			foreach (var (key, value) in _missingObjectCounts)
			{
				Uri exampleUri = _missingObjectExamples[key];
				INode exampleTypeNode = _store.GetTriplesWithSubjectPredicate(new UriNode(exampleUri), Nodes.RdfType)
					.SingleOrDefault()?.Object;

				string exampleType = "";
				if (exampleTypeNode is UriNode)
					exampleType = $", type {Nodes.CimName(exampleTypeNode as UriNode)} (not modelled)";

				writer.WriteLine($"  {key}: {value} (e.g. {exampleUri}{exampleType})");
			}
		}

		#endregion

		#region Private methods

		/// <summary>
		/// If there is a class with the given name in the CIM namespace,
		/// returns that class.
		/// Otherwise returns null.
		/// </summary>
		private Type CimType(string className)
		{
			string typeName = $"{typeof(IdentifiedObject).Namespace}.{className}, {typeof(IdentifiedObject).Assembly.FullName}";

			if (Type.GetType(typeName) is not Type type)
			{
				_notFoundTypes.Add(typeName);
				return null;
			}

			return type;
		}

		/// <summary>
		/// Sets an attribute on a CIM object
		/// </summary>
		/// <param name="objectToFill">The CIM object whose attribute to set</param>
		/// <param name="fullAttributeName">The full name of the attribute, as (type).(attibute)</param>
		/// <param name="valueNode">The RDF node that contains the attribute value</param>
		private void SetAttribute(object objectToFill, string fullAttributeName, INode valueNode)
		{
			// Use reflection to find the property to set

			var (className, attributeName) = fullAttributeName.Split('.');

			if (CimType(className) is not Type baseType)
			{
				// Attribute belongs to a base type that we don't model (yet)
				return;
			}

			VerifyBaseType(baseType, objectToFill.GetType());

			var property = baseType.GetProperty(attributeName, BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public);
			if (property is null)
			{
				_missingProperties.Add($"{baseType.Name}.{attributeName}");
				return;
			}

			object value = ParseValue(valueNode, property);

			// Set property
			property.SetValue(objectToFill, value);

			if (value is IdentifiedObject)
				AddObjectToList(value, objectToFill);
		}

		/// <summary>
		/// Parses and returns a CIM attribute value. 
		/// The returned value has type <code>property.PropertyType</code>, and is often
		/// boxed (integer, double, Voltage etc.)
		/// </summary>
		/// <param name="valueNode">The node containing the attribute value</param>
		/// <param name="property">The property that will receive the value</param>
		private object ParseValue(INode valueNode, PropertyInfo property)
		{
			string propertyName = property.Name;
			string className = property.DeclaringType.Name;

			// Split in cases depending on property type

			Type propertyType = property.PropertyType;
			if (propertyType.Name == "Nullable`1" && propertyType.Namespace == "System")
				propertyType = propertyType.GenericTypeArguments[0];

			if (valueNode is LiteralNode literalValue && literalValue.DataType == Uris.XmlSchemaBase)
			{
				// Attribute value is a literal value of a known XML type

				if (propertyType == typeof(string) && literalValue.DataType.Fragment == "#string")
				{
					// String: no conversion needed
					return literalValue.Value;
				}
				else if (propertyType == typeof(int) && literalValue.DataType.Fragment == "#integer")
				{
					// Int: parse 
					return ParseInt(literalValue, propertyName);
				}
				else if (propertyType == typeof(double))
				{
					// Double: parse
					return ParseDouble(literalValue, propertyName);
				}
				else if (propertyType == typeof(bool) && literalValue.DataType.Fragment == "#boolean")
				{
					// Bool: parse 
					return ParseBool(literalValue, propertyName);
				}
				else if (Units.Handles(propertyType))
				{
					// Electrical value: parse as double, assign unit

					double dValue = ParseDouble(literalValue, propertyName);

					var value = Units.AssignUnit(dValue, propertyType);

					if (value.GetType() != propertyType)
						throw new Exception("Internal error");

					return value;
				}
				else
					throw new Exception($"Unknown type combination {propertyType}/{valueNode}");
			}
			else if (valueNode is UriNode uriValue)
			{
				// Attribute value is a URI.

				if (propertyType.IsEnum)
				{
					// Enum property: parse enum value

					var (enumName, valueName) = Nodes.CimName(uriValue).Split('.');
					if (enumName != propertyType.Name)
						throw new Exception($"Property {className}.{propertyName} does not accept value {enumName}.{valueName}");

					var enumType = CimType(enumName);
					return Enum.Parse(enumType, valueName, ignoreCase: true);
				}
				else
				{
					// Reference property: Look up referred object

					if (_created.TryGetValue(uriValue.Uri, out var value))
					{
						// Verify that the value has a suitable type
						var expectedType = property.PropertyType;
						var actualType = value.GetType();
						VerifyBaseType(expectedType, actualType);
						return value;
					}
					else
					{
						_missingObjectCounts.AddOrNew($"{className}.{propertyName}", 1);
						_missingObjectExamples[$"{className}.{propertyName}"] = uriValue.Uri;
						return null;
					}
				}
			}
			else
				throw new Exception("Unsupported node type");
		}

		/// <summary>
		/// If <paramref name="objectWithList"/> has a member with name
		/// (Type)s and type <code>List&lt;Type></code>, where Type is the type of
		/// <paramref name="objectToAdd"/> or one of its base types, adds <paramref name="objectToAdd"/> to that list.
		/// </summary>
		private static void AddObjectToList(object objectWithList, object objectToAdd)
		{
			var listItemType = objectToAdd.GetType();

			while (listItemType != null)
			{
				// Look for a property named (type)s, where (type) is the type of
				// objectToAdd or (in later iterations) a base class

				string listName = $"{listItemType.Name}s";

				var listProperty = objectWithList.GetType().GetProperty(listName,
					BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);

				if (listProperty == null)
				{
					// Nope, try the base type
					listItemType = listItemType.BaseType;
					continue;
				}

				// Verify that property has type List<objectType>

				var listType = listProperty.PropertyType;

				if (!listType.IsGenericType
					|| listType.GetGenericTypeDefinition() != typeof(List<>)
					|| listType.GenericTypeArguments[0] != listItemType)
					throw new Exception($"Expected type List<{listItemType.Name}> for property {listProperty.DeclaringType.Name}.{listName}");

				// Get the list from the value object, create if null

				var list = listProperty.GetValue(objectWithList);
				if (list == null)
				{
					list = listType.GetConstructor(Array.Empty<Type>()).Invoke(Array.Empty<object>());
					listProperty.SetValue(objectWithList, list);
				}

				// Add objectToAdd to the list

				listProperty.PropertyType.GetMethod("Add").Invoke(list, new[] { objectToAdd });
				return;
			}
		}

		/// <summary>
		/// Parses the given literal RDF node as an integer.
		/// </summary>
		/// <param name="literalValue">The literal value to parse</param>
		/// <param name="propertyName">The property to reference in the exception message if the literal has a wrong type</param>
		private static int ParseInt(LiteralNode literalValue, string propertyName)
		{
			return literalValue.DataType.Fragment switch
			{
				"#integer" => int.Parse(literalValue.Value),
				_ => throw new Exception($"Bad data type '{literalValue.DataType.Fragment}' for integer property {propertyName}")
			};
		}

		/// <summary>
		/// Parses the given literal RDF node as a double.
		/// </summary>
		/// <param name="literalValue">The literal value to parse</param>
		/// <param name="propertyName">The property to reference in the exception message if the literal has a wrong type</param>
		private static double ParseDouble(LiteralNode literalValue, string propertyName)
		{
			return literalValue.DataType.Fragment switch
			{
				"#integer" => int.Parse(literalValue.Value),
				"#double" => double.Parse(literalValue.Value, CultureInfo.InvariantCulture),
				"#string" => double.Parse(literalValue.Value, CultureInfo.InvariantCulture),
				_ => throw new Exception($"Bad data type '{literalValue.DataType.Fragment}' for numeric property {propertyName}")
			};
		}

		/// <summary>
		/// Parses the given literal RDF node as an bool.
		/// </summary>
		/// <param name="literalValue">The literal value to parse</param>
		/// <param name="propertyName">The property to reference in the exception message if the literal has a wrong type</param>
		private static bool ParseBool(LiteralNode literalValue, string propertyName)
		{
			return literalValue.DataType.Fragment switch
			{
				"#boolean" => bool.Parse(literalValue.Value),
				_ => throw new Exception($"Bad data type '{literalValue.DataType.Fragment}' for boolean property {propertyName}")
			};
		}

		/// <summary>
		/// Throws an exception if <paramref name="baseType"/> is not a base type of <paramref name="type"/>
		/// </summary>
		private void VerifyBaseType(Type baseType, Type type)
		{
			Type typeOrBase = type;
			while (typeOrBase != null)
			{
				if (typeOrBase == baseType)
					return;
				typeOrBase = typeOrBase.BaseType;
			}

			throw new Exception($"{baseType.Name} is not a base type of {type.Name}");
		}

		/// <summary>
		/// Enumerates the (object URI, type URI) in all (object)-isType-(class) RDF triples
		/// </summary>
		private IEnumerable<(Uri ObjectUri, Uri TypeUri)> TypeSpecs()
		{
			foreach (var t in _store.GetTriplesWithPredicate(Nodes.RdfType))
			{
				if (t.Subject is UriNode subjectNode && t.Object is UriNode objectNode)
				{
					yield return (subjectNode.Uri, objectNode.Uri);
				}
			}
		}

		/// <summary>
		/// Enumerates the (attributeName, attributeValue) in all (objectId)-(attributeName)-(attributeValue) RDF triples
		/// that refer to the given object URI.
		/// </summary>
		private IEnumerable<(string AttributeName, INode AttributeValue)> Attributes(Uri objectUri)
		{
			foreach (var t in _store.GetTriplesWithSubject(objectUri))
			{
				if (t.Predicate is not UriNode predicateNode)
					throw new Exception("Expected predicate to be an URI");

				if (Nodes.CimName(predicateNode) is not string attributeName)
					// e.g. rdf:type
					continue;

				yield return (attributeName, t.Object);
			}
		}

		/// <summary>
		/// Returns the reflection info on the property identified by the given lambda expression
		/// </summary>
		private static PropertyInfo PropertyInfo<TCimType, TProperty>(Expression<Func<TCimType, TProperty>> lambdaExpression)
		{
			if (lambdaExpression.NodeType != ExpressionType.Lambda)
				throw new ArgumentException("Expression must be a lambda");

			if (lambdaExpression.Body is not MemberExpression member || member.Member is not PropertyInfo propertyInfo)
				throw new ArgumentException("Expression must be x => x.SomeProperty");

			return propertyInfo;
		}

		#endregion
	}
}
