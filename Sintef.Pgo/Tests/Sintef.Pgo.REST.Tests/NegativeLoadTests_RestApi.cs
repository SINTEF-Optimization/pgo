using System.Linq;
using Xunit;
using Xunit.Abstractions;
using Sintef.Pgo.DataContracts;
using Sintef.Pgo.Core.Test;

namespace Sintef.Pgo.REST.Tests
{
	/// <summary>
	/// Tests of consumers with negative active load (net export) through the REST API.
	/// The network and demands are Baran-Wu (baran-wu.json), with every consumer's active load negated.
	/// </summary>
	public class NegativeLoadTests_RestApi : LiveServerFixture
	{
		public NegativeLoadTests_RestApi(ITestOutputHelper output) : base(output) { }

		private static string NetworkFile => TestUtils.TestDataFile("baran-wu.json");

		private static string DemandsFile => TestUtils.TestDataFile("baran-wu_forecast.json");

		[Fact]
		public void SessionWithNetExportReportsReverseFlow()
		{
			CreateDefaultClient(NetworkFile);
			Client.CreateJsonSession(SessionId, NetworkId, ExportingDemands());

			var solution = Client.GetBestSolution(SessionId);
			var flow = solution.Flows.Single();
			Assert.Equal(FlowStatus.Exact, flow.Status);

			// Each line is reported in the direction of active power flow, so the reverse flow in the line
			// from the provider (bus 1) to bus 2 is reported from bus 2 to the provider
			Assert.Contains(flow.Powers["2"], p => p.Target == "1" && p.ActivePower > 0);

			// The provider absorbs the export less the losses, and the voltage rises toward the exporters
			double absorbed = flow.Powers.Sum(kv => kv.Value.Sum(p =>
				p.Target == "1" ? p.ActivePower - p.PowerLoss :
				kv.Key == "1" ? -p.ActivePower : 0));
			Assert.InRange(absorbed, 0, 3.715);
			Assert.True(flow.Voltages["18"] > flow.Voltages["2"] && flow.Voltages["2"] > 12.66);

			var info = Client.GetBestSolutionInfo(SessionId);
			Assert.True(info.IsFeasible);
			Assert.All(info.ObjectiveComponents, c => Assert.True(c.Value >= 0));
		}

		[Fact]
		public void ProviderAbsorbingMoreThanItsCapacityIsReported()
		{
			CreateDefaultClient();
			Client.LoadNetwork(NetworkId, FileToModifiedJsonStream<PowerGrid>(NetworkFile,
				network => network.Nodes.Single(n => n.Id == "1").MaximumActiveGeneration = 1.0), "baran-wu.json");
			Client.CreateJsonSession(SessionId, NetworkId, ExportingDemands());

			var info = Client.GetBestSolutionInfo(SessionId);

			Assert.False(info.IsFeasible);
			Assert.Contains("Capacity of Provider 1 is exceeded", info.ViolatedConstraints.Single().Description);
		}

		/// <summary>
		/// Returns Baran-Wu's demands with every active load negated, as a JSON stream
		/// </summary>
		private System.IO.MemoryStream ExportingDemands()
		{
			return FileToModifiedJsonStream<Demand>(DemandsFile, demand =>
			{
				foreach (var load in demand.Loads)
					load.ActiveLoads = load.ActiveLoads.Select(p => -p).ToList();
			});
		}
	}
}
