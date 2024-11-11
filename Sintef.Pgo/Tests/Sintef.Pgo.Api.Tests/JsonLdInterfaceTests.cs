using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sintef.Pgo.Core.Test;

namespace Sintef.Pgo.Api.Tests
{
	/// <summary>
	/// Tests for the versions of PGO .NET API methods that use JSON-LD format
	/// </summary>
	[TestClass]
	public class JsonLdInterfaceTests : ApiTestFixture
	{
		[TestMethod]
		public void MainWorkflowWorks()
		{
			AddDiginNetwork();

			var session = AddDiginSession();

			// Optimize

			Optimize(session);

			// Extract best solution

			CimJsonLdSolution solution = session.BestCimJsonLdSolution;

			TestUtils.AssertValidDiginSolution(solution);
		}

		[TestMethod]
		public void SolutionCanBeAddedAndExtractedAndUpdated()
		{
			// Setup: get a solution
			AddDiginNetwork();
			var session = AddDiginSession();
			Optimize(session);
			CimJsonLdSolution solution = session.BestCimJsonLdSolution;

			// Add solution

			session.AddSolution("mySol", solution);

			AssertSolutionIds(session, "best mySol");

			// Extract solution

			var solution2 = session.GetCimJsonLdSolution("mySol");

			TestUtils.AssertValidDiginSolution(solution2);

			// Update solution

			session.UpdateSolution("mySol", solution2);
		}

		[TestMethod]
		public void ErrorsInJsonLdAreReported()
		{
			TestUtils.AssertException(() => AddDiginNetwork(modify: Modify), "Error parsing attribute 'BaseVoltage.nominalVoltage' of CIM object" +
				" with URI urn:uuid:2dd90159-bdfb-11e5-94fa-c8f73332c8f4 (value = 100.0.0^^http://www.w3.org/2001/XMLSchema#string):\n" +
				"The input string '100.0.0' was not in a correct format");

			void Modify(JObject network)
			{
				var jProp = network.Descendants().OfType<JProperty>().First(d => d.Name == "cim:BaseVoltage.nominalVoltage");
				jProp.Value = JToken.Parse("\"100.0.0\"");
			}
		}

		[TestMethod]
		public void CannotExtractJsonLdSolutionForNonJsonLdNetwork()
		{
			AddCimTestNetwork();
			var session = AddCimTestSession();

			TestUtils.AssertException(() => _ = session.BestCimJsonLdSolution,
				"Cannot export solution to JSON-LD: URIs are unknown since the network was not created from JSON-LD data.");
		}
	}
}