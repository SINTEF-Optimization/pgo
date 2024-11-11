using Xunit;
using Xunit.Abstractions;
using System.Linq;
using Sintef.Pgo.Core;
using Sintef.Pgo.Core.IO;
using IO = Sintef.Pgo.Core.IO;
using Sintef.Pgo.Core.Test;

namespace Sintef.Pgo.REST.Tests
{
	/// <summary>
	/// Tests for retrieving the data that was used to define networks and sessions
	/// </summary>
	public class DataRoundtripTests : LiveServerFixture
	{
		public DataRoundtripTests(ITestOutputHelper output) : base(output) { }

		[Fact]
		public void CanReconstructPgoSolutionFromServerResponse()
		{
			var network = IO.PgoJsonParser.ParseNetworkFromJsonFile(BaranWuModifiedNetworkFile);
			var periodData = IO.PgoJsonParser.ParseDemandsFromJsonFile(network, BaranWuModifiedDemandsFile);
			var problem = new PgoProblem(periodData, new IteratedDistFlowProvider(new IteratedDistFlowProvider.Options()), "test");

			CreateDefaultClient(BaranWuModifiedNetworkFile);
			Client.CreateJsonSession(SessionId, NetworkId, BaranWuModifiedDemandsFile, null);
			Optimize();
			var solutionInfo = Client.GetBestSolutionInfo(SessionId);
			var ioSolution = Client.GetBestSolution(SessionId);
			var pgoSolution = IO.PgoJsonParser.ParseSolution(problem, ioSolution);

			Assert.Equal(solutionInfo.IsFeasible, pgoSolution.IsFeasible);
		}

		[Fact]
		public void CanReconstructPgoProblemFromServerResponse()
		{
			var uploadedNetwork = IO.PgoJsonParser.ParseNetworkFromJsonFile(BaranWuModifiedNetworkFile);
			var uploadedPeriodData = IO.PgoJsonParser.ParseDemandsFromJsonFile(uploadedNetwork, BaranWuModifiedDemandsFile);

			CreateDefaultClient(BaranWuModifiedNetworkFile);
			Client.CreateJsonSession(SessionId, NetworkId, BaranWuModifiedDemandsFile, null);

			var ioNetwork = Client.GetNetwork(NetworkId);
			var downloadedNetwork = IO.PgoJsonParser.ParseNetwork(ioNetwork);
			var ioPeriodData = Client.GetSessionDemands(SessionId);
			var downloadedPeriodData = IO.PgoJsonParser.ParseDemands(downloadedNetwork, ioPeriodData);

			AssertEquivalent(downloadedNetwork.Buses.Select(b => b.Name), uploadedNetwork.Buses.Select(b => b.Name));
			AssertEquivalent(downloadedNetwork.Lines.Select(b => b.Name), uploadedNetwork.Lines.Select(b => b.Name));
			AssertEquivalent(downloadedNetwork.PowerTransformers.Select(b => b.Bus.Name), uploadedNetwork.PowerTransformers.Select(b => b.Bus.Name));
			AssertEquivalent(downloadedPeriodData.SelectMany(p => p.Demands.Demands).Select(d => (d.Key.Name, d.Value)),
					uploadedPeriodData.SelectMany(p => p.Demands.Demands).Select(d => (d.Key.Name, d.Value)));

			var _problem = new PgoProblem(downloadedPeriodData, new IteratedDistFlowProvider(new IteratedDistFlowProvider.Options()), "test");

		}

		[Fact]
		public void CanReconstructPgoSolutionFromServerResponse_BaranWu_10_5()
		{
			var networkFilename = TestUtils.TestDataFile("bw10-5-10.json");
			var demandsFilename = TestUtils.TestDataFile("bw10-5-10_forecast.json");

			CreateDefaultClient(networkFilename);
			Client.CreateJsonSession(SessionId, NetworkId, demandsFilename, null);

			Optimize(Client);

			var solutionInfo = Client.GetBestSolutionInfo(SessionId);
			Assert.True(solutionInfo.IsFeasible);

			var downloadedNetwork = IO.PgoJsonParser.ParseNetwork(Client.GetNetwork(NetworkId));
			var downloadedPeriodData = IO.PgoJsonParser.ParseDemands(downloadedNetwork, Client.GetSessionDemands(SessionId));

			var flowProvider = Core.Utils.CreateFlowProvider(FlowApproximation.IteratedDF);
			var problem = new PgoProblem(downloadedPeriodData, flowProvider, "PgoProblem");

			var downloadedSolution = Client.GetBestSolution(SessionId);
			var solution = PgoJsonParser.ParseSolution(problem, downloadedSolution);

			foreach ((var period, var flow) in solution.SinglePeriodSolutions.Zip(downloadedSolution.Flows, (a, b) => (a, b)))
			{
				var periodFlow = PgoJsonParser.ParseFlow(period, flow);
				period.SetFlow(flowProvider, periodFlow);
			}

			Assert.True(solution.IsFeasible);
			Assert.Equal(solutionInfo.ObjectiveValue, solution.ObjectiveValue, precision: 6);
		}
	}
}
