using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.IO;
using VDS.RDF;
using VDS.RDF.JsonLd;
using VDS.RDF.Writing;
using System.Threading;
using Sintef.Pgo.Cim;
using Sintef.Scoop.Utilities;
using System.Linq;
using UnitsNet;
using VDS.RDF.Nodes;
using System.Collections.Generic;
using Newtonsoft.Json.Serialization;
using Sintef.Pgo.DataContracts;

namespace Sintef.Pgo.Core.IO
{
	/// <summary>
	/// Exports a <see cref="CimSolution"/> to JSON-LD.
	/// 
	/// May be extended to exporting other CIM data if necessary.
	/// </summary>
	public class CimJsonExporter
	{
		/// <summary>
		/// The parser that was used to parse the network that the solution is for
		/// </summary>
		private CimJsonParser _networkParser;

		/// <summary>
		/// Initializes the exporter
		/// </summary>
		/// <param name="networkParser">The parser that was used to parse the network that the solution is for</param>
		public CimJsonExporter(CimJsonParser networkParser)
		{
			_networkParser = networkParser;
		}

		/// <summary>
		/// Convert the given period solution to a corresponding JSON-LD representation.
		/// </summary>
		/// <param name="cimPeriodSolution">The period solution to convert</param>
		/// <param name="metadata">Metadata to include in the Json</param>
		public JObject ToJson(CimPeriodSolution cimPeriodSolution, SolutionMetadata metadata)
		{
			_ = metadata ?? throw new ArgumentNullException(nameof(metadata));

			var store = new TripleStore();

			Graph solutionGraph = CreateSolutionGraph(cimPeriodSolution, metadata.GraphGuid);
			store.Add(solutionGraph);

			Graph metaGraph = CreateMetaGraph(solutionGraph.Name, metadata);
			store.Add(metaGraph);

			return SerializeToJObject(store);
		}

		/// <summary>
		/// Creates the RDF graph that represents the given period solution
		/// </summary>
		/// <param name="cimPeriodSolution">The period solution to export</param>
		/// <param name="graphGuid">The GUID to use in the graph URI</param>
		private Graph CreateSolutionGraph(CimPeriodSolution cimPeriodSolution, Guid? graphGuid)
		{
			Uri graphUri = new Uri($"urn:uuid:{graphGuid ?? Guid.NewGuid()}");
			var solutionGraph = new Graph(graphUri);

			INode switchOpenPredicate = Nodes.CimProperty(typeof(Switch), nameof(Switch.Open));

			// Add triples for each switch
			foreach (var theSwitch in cimPeriodSolution.Switches.OrderBy(s => s.MRID))
			{
				// Define the switch's object type
				INode subjectNode = AddTypeTriple(solutionGraph, theSwitch);

				// State the value of Open
				solutionGraph.Assert(subjectNode, switchOpenPredicate, BoolNode(theSwitch.Open.Value));
			}

			return solutionGraph;
		}

		/// <summary>
		/// Creates the RDF graph with metadata about the solution
		/// </summary>
		/// <param name="graphNode">The RDF node that represents the solution graph</param>
		/// <param name="metadata">The metadata to export</param>
		private Graph CreateMetaGraph(IRefNode graphNode, SolutionMetadata metadata)
		{
			var metaGraph = new Graph();

			metaGraph.Assert(graphNode, Nodes.RdfType, Nodes.Dcat("Dataset"));
			metaGraph.Assert(graphNode, Nodes.Dcterms("generatedAtTime"), DateTimeNode(metadata.GeneratedAtTime));
			metaGraph.Assert(graphNode, Nodes.Dcterms("creator"), CimJsonExporter.StringNode(metadata.Creator)); 

			return metaGraph;
		}

		/// <summary>
		/// Serializes the given triple store to a <see cref="JObject"/>
		/// </summary>
		private static JObject SerializeToJObject(TripleStore store)
		{
			// Serialize to Json objects

			var writer = new JsonLdWriter(new JsonLdWriterOptions()
			{
				UseNativeTypes = true
			});

			var document = writer.SerializeStore(store);

			// Define context

			var context = new JObject();
			context.Add("rdf", $"{Uris.RdfBase}");
			context.Add("cim", $"{Uris.CimBase}");
			context.Add("eu", "http://iec.ch/TC57/CIM100-European#");
			context.Add("dcterms", $"{Uris.DcTermsBase}");
			context.Add("dcat", $"{Uris.DcatBase}");
			context.Add("prov", "http://www.w3.org/ns/prov#");
			context.Add("xsd", $"{Uris.XmlSchemaBase}");

			// Compact json with respect to the context

			var compacted = JsonLdProcessor.Compact(document, context, new JsonLdProcessorOptions() { });

			// Compactification puts the context property last.
			// Move it first, like we see in the DIGIN documents

			var ctx = compacted["@context"];
			compacted.Remove("@context");
			compacted.AddFirst(new JProperty("@context", ctx));

			return compacted;
		}

		/// <summary>
		/// Adds a RDF triple to the given graph that defines the type of the given <see cref="IdentifiedObject"/>
		/// </summary>
		/// <returns>The subject node that represents <paramref name="identifiedObject"/></returns>
		private INode AddTypeTriple(Graph graph, IdentifiedObject identifiedObject)
		{
			// Look up the URI used for the object in the network data, by MRID.
			// The URI is normally a UUID identical to the MRID. This is true in all data we
			// have seen, but not necessary according to the CIM spec, hence we use the
			// mapping from the network parser.
			INode subjectNode = new UriNode(_networkParser.UriFor(identifiedObject.MRID));

			INode typePredicate = Nodes.RdfType;
			INode typeNode = Nodes.CimType(identifiedObject);

			graph.Assert(subjectNode, typePredicate, typeNode);

			return subjectNode;
		}

		/// <summary>
		/// Returns a RDF node that represents the given string literal
		/// </summary>
		private static INode StringNode(string value)
		{
			return new LiteralNode(value);
		}

		/// <summary>
		/// Returns a RDF node that represents the given DateTime literal
		/// </summary>
		private static INode DateTimeNode(DateTime time)
		{
			return new LiteralNode(time.ToInvariantString(),  Uris.XmlSchema("dateTime"));
		}

		/// <summary>
		/// Returns a RDF node that represents the given bool literal
		/// </summary>
		private static INode BoolNode(bool value)
		{
			return new LiteralNode(value ? "true" : "false", Uris.XmlSchema("boolean"));
		}

		/// <summary>
		/// Metadata for a CIM solution
		/// </summary>
		public class SolutionMetadata
		{
			/// <summary>
			/// The GUID of the RDF graph that represents the solution
			/// </summary>
			public Guid GraphGuid = Guid.NewGuid();

			/// <summary>
			/// The solution's timestamp
			/// </summary>
			public DateTime GeneratedAtTime = DateTime.Now;

			/// <summary>
			/// The solution's creator
			/// </summary>
			public string Creator = "SINTEF PGO";
		}
	}
}