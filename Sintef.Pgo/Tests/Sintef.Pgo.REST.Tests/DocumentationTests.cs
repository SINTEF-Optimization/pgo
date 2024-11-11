using System;
using System.IO;
using System.Linq;
using System.Threading;
using Sintef.Pgo.Core;
using Sintef.Pgo.Core.Test;
using Sintef.Pgo.Server;
using Xunit;

namespace Sintef.Pgo.REST.Tests
{
	public class DocumentationTests // To make sure all the documentation tests run and work
	{
		Server.Server _server;

		public DocumentationTests()
		{
			_server = Server.Server.Create();
		}


		[Fact]
		public void TestProviderConsumer()
		{
			var solution = DoTest(TestUtils.ExampleDataFile("doc-provider-consumer-example.json"), TestUtils.ExampleDataFile("doc-example_forecast.json"));
			var flow = GetFlowFrom(solution);

			// Test that quantities are as expected -- if they change the documentation likely should as well
			Assert.Equal(22000.00, GetVoltageAt(solution, flow, "Source"));
			Assert.Equal(21977.24, GetVoltageAt(solution, flow, "Load"), 0.01);
			Assert.Equal(22.75, GetCurrentMagnitude(solution, flow, "line"), 0.01);
			Assert.Equal(500517.6, GetActivePower(solution, flow, "Source", "line"), 0.1);  // With simplified power should just be preserved here
		}

		[Fact]
		public void TestProviderTransformerConsumer()
		{
			var solution = DoTest(TestUtils.ExampleDataFile("doc-provider-transformer-consumer-example.json"), TestUtils.ExampleDataFile("doc-example_forecast.json"));
			var flow = GetFlowFrom(solution);

			// Test that voltages are as expected -- if they change the documentation likely should as well
			Assert.Equal(22000.00, GetVoltageAt(solution, flow, "Source")); // By definition at 22 kV
			Assert.Equal(21997.73, GetVoltageAt(solution, flow, "Transformer primary"), 0.01);
			Assert.Equal(11000.00, GetVoltageAt(solution, flow, "Transformer secondary"), 1e-10); //Auto-stepping trafo with output at 11 kV
			Assert.Equal(10995.46, GetVoltageAt(solution, flow, "Load"), 0.01);
			Assert.Equal(22.73, GetCurrentMagnitude(solution, flow, "Source-Transformer line"), 0.01);
			Assert.Equal(45.47, GetCurrentMagnitude(solution, flow, "Transformer-Load line"), 0.01);
			Assert.Equal(500258.5, GetActivePower(solution, flow, "Source", "Source-Transformer line"), 0.1); // With simplified power should just be preserved here
			Assert.Equal(500206.8, GetActivePower(solution, flow, "Transformer secondary", "Transformer-Load line"), 0.1);  // With simplified power should just be preserved here
		}

		[Fact]
		public void TestTwoProvidersSingleConsumer()
		{
			var solution = DoTest(TestUtils.ExampleDataFile("doc-two-providers-consumer.json"), TestUtils.ExampleDataFile("doc-example_forecast.json"), 500); // Needs more time than the other tests
			Assert.True(solution.IsComplete(FlowProvider(solution)));
			
			var flow = GetFlowFrom(solution);

			// Test that voltages are as expected -- if they change the documentation likely should as well
			Assert.Equal(22000.00, GetVoltageAt(solution, flow, "Source 1")); // By definition at 22 kV
			Assert.Equal(22000.00, GetVoltageAt(solution, flow, "Source 2")); // By definition at 22 kV
			Assert.Equal(21977.19, GetVoltageAt(solution, flow, "Junction"), 0.01);
			Assert.Equal(21954.40, GetVoltageAt(solution, flow, "Load"), 0.01);
			Assert.Equal(22.77, GetCurrentMagnitude(solution, flow, "Source 1-Junction line"), 0.01);
			Assert.Equal(22.77, GetCurrentMagnitude(solution, flow, "Junction-Load line"), 0.01);
			Assert.Equal(501037.4, GetActivePower(solution, flow, "Source 1", "Source 1-Junction line"), 0.1); // With simplified power should just be preserved here
			Assert.Equal(500518.7, GetActivePower(solution, flow, "Junction", "Junction-Load line"), 0.1);  // With simplified power should just be preserved here
		}

		private IPgoSolution DoTest(string caseFile, string forecastFile, int timeout = 50)
		{
			Session session;

			using (var problemDefinition = File.OpenRead(caseFile))
			{
				_server.LoadNetworkFromJson("network", problemDefinition);
			}

			using (var forecastStream = File.OpenRead(forecastFile))
			{
				session = _server.CreateJsonSession("0", "network", forecastStream, null) as Session;
			}

			session.StartOptimization();

			Thread.Sleep(timeout); // just give it some time to get going
			Assert.True(session.OptimizationIsRunning);

			session.StopOptimization().Wait();

			var solution = session.GetBestSolutionClone();

			Assert.True(solution != null);
			return solution;
		}

		private double GetVoltageAt(IPgoSolution solution, IPowerFlow flow, string name)
		{
			return flow.Voltage((solution.Encoding as PgoProblem).Network.GetBus(name)).Magnitude;
		}

		private double GetCurrentMagnitude(IPgoSolution solution, IPowerFlow flow, string lineName)
		{
			return flow.CurrentMagnitude((solution.Encoding as PgoProblem).Network.GetLine(lineName));
		}

		private double GetActivePower(IPgoSolution solution, IPowerFlow flow, string originBusName, string lineName)
		{
			var network = (solution.Encoding as PgoProblem).Network;
			return flow.PowerFlow(network.GetBus(originBusName), network.GetLine(lineName)).Real;
		}

		private static IPowerFlow GetFlowFrom(IPgoSolution solution)
		{
			return (solution as PgoSolution).SinglePeriodSolutions.First().Flow(FlowProvider(solution));
		}

		private static IFlowProvider FlowProvider(IPgoSolution solution)
		{
			return (solution.Encoding as PgoProblem).FlowProvider;
		}
	}
}
