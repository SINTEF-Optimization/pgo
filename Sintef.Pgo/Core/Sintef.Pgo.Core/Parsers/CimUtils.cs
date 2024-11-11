using Sintef.Pgo.Cim;
using System;
using System.Reflection;
using VDS.RDF;

namespace Sintef.Pgo.Core.IO
{
	/// <summary>
	/// Helper class for creating URIs used in CIM RDF
	/// </summary>
	internal class Uris
	{
		/// <summary>
		/// The base URI for RDF
		/// </summary>
		public static Uri RdfBase { get; } = new Uri("http://www.w3.org/1999/02/22-rdf-syntax-ns#");

		/// <summary>
		/// The base URI for CIM
		/// </summary>
		public static Uri CimBase { get; } = new Uri("http://ucaiug.org/ns/CIM#");

		/// <summary>
		/// The base URI for DcTerms
		/// </summary>
		public static Uri DcTermsBase { get; } = new Uri("http://purl.org/dc/terms/");

		/// <summary>
		/// The base URI for Dcat
		/// </summary>
		public static Uri DcatBase { get; } = new Uri("http://www.w3.org/ns/dcat#");

		/// <summary>
		/// The base URI for Xml schemas
		/// </summary>
		public static Uri XmlSchemaBase { get; } = new Uri("http://www.w3.org/2001/XMLSchema#");

		/// <summary>
		/// The URI representing a @type predicate in RDF
		/// </summary>
		public static Uri RdfType { get; } = new Uri(RdfBase, "#type");

		/// <summary>
		/// Returns the URI representing the schema of the given XML type name
		/// </summary>
		public static Uri XmlSchema(string type) => new Uri(XmlSchemaBase, $"#{type}");

		/// <summary>
		/// If the given URI refers to a CIM name, returns that name.
		/// Otherwise, returns null.
		/// </summary>
		public static string CimName(Uri uri)
		{
			var relativeUri = CimBase.MakeRelativeUri(uri).OriginalString;

			if (relativeUri[0] != '#')
				return null;

			return relativeUri.Substring(1);
		}
	}

	/// <summary>
	/// Helper for creating RDF nodes used in CIM JsonLd
	/// </summary>
	internal class Nodes
	{
		/// <summary>
		/// The RDF node representing the @type predicate
		/// </summary>
		public static INode RdfType { get; } = new UriNode(Uris.RdfType);

		/// <summary>
		/// Returns a RDF predicate node representing the named property of the given CIM object
		/// (which may be declared in a base class)
		/// </summary>
		public static INode CimProperty(object cimObject, string propertyName)
		{
			return CimProperty(cimObject.GetType(), propertyName);
		}

		/// <summary>
		/// Returns a RDF predicate node representing the named property of the given CIM type
		/// (or one of its base types)
		/// </summary>
		public static INode CimProperty(Type type, string propertyName)
		{
			var property = type.GetProperty(propertyName,
				BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);
			var declaringType = property.DeclaringType;

			// Ensure property name has correct casing for CIM: camelCase
			string cimPropertyName = propertyName.Substring(0, 1).ToLower() + propertyName.Substring(1);

			return new UriNode(new Uri($"{Uris.CimBase}{declaringType.Name}.{cimPropertyName}"));
		}

		/// <summary>
		/// Returns a RDF node representing the CIM type of the given object
		/// </summary>
		public static INode CimType(IdentifiedObject identifiedObject)
		{
			return new UriNode(new Uri(Uris.CimBase, $"#{identifiedObject.GetType().Name}"));
		}

		/// <summary>
		/// Returns the name within the CIM namespace represented by the given node
		/// </summary>
		public static string CimName(UriNode uriNode) => Uris.CimName(uriNode.Uri);

		/// <summary>
		/// Returns a RDF node representing the named Dcat type
		/// </summary>
		public static INode Dcat(string typeName)
		{
			return new UriNode(new Uri(Uris.DcatBase, $"#{typeName}"));
		}

		/// <summary>
		/// Returns a RDF node representing the named Dcterms term
		/// </summary>
		public static INode Dcterms(string term)
		{
			return new UriNode(new Uri(Uris.DcTermsBase, term));
		}
	}
}
