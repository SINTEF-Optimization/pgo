using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core.Test
{
	[TestClass]
	public class IteratedDistflowTests
	{
		/// <summary>
		/// The options given to the flow provider
		/// </summary>
		private IteratedDistFlowProvider.Options _options = new IteratedDistFlowProvider.Options();

		[TestMethod]
		public void ATrivialCaseIsSolved()
		{
			NetworkBuilder builder = new NetworkBuilder();

			builder.Add("Producer[generatorVoltage=1000.0] -- Line1[r=0] -- Consumer1[consumption=(100, 0)]");
			builder.Add("Consumer1 -- Line2[r=1] -- Consumer2[consumption=(100, 0)]");

			AssertFlowStatus(builder, FlowStatus.Exact);
		}

		[TestMethod]
		[ExpectedException(typeof(ArgumentException))]
		public void ConfigurationMustBeRadial()
		{
			NetworkBuilder builder = new NetworkBuilder();

			// Cycle connected to generator.
			builder.Add("Node1[generatorVoltage=1] -- Line1[closed] -- Node2[consumption=(1,0)]");
			builder.Add("Node1 -- Line2[closed] -- Node2");

			FlowProblem flowProblem = builder.FlowProblem;
			var flow = ComputeFlow(flowProblem);
		}

		[TestMethod]
		public void DivergenceIsReported()
		{
			// This flow problem has no solution. Due to the large resistance, 
			// increasing the current causes more power to dissipate in Line1 rather
			// than being delivered to Consumer1.

			var builder = NetworkBuilder.Create(
				"Producer[generatorVoltage=1000.0] -- Line1[r=1250] -- Consumer1[consumption=(100, 0)]",
				"Consumer1 -- Line2[r=1] -- Consumer2[consumption=(100, 0)]"
				);

			AssertFlowStatus(builder, FlowStatus.Failed, "IteratedDistFlow diverged; power loss in line exceeds output power (220 iterations)");
		}

		[TestMethod]
		public void SolverCanStopOnIMaxViolation()
		{
			var builder = NetworkBuilder.Create("Producer[generatorVoltage=1000.0] -- Line1[r=10; iMax=0.1] -- Consumer[consumption=(100, 0)]");
			_options.StopOnIMaxViolation = true;

			AssertFlowStatus(builder, FlowStatus.Approximate, "IMax was exceeded (2 iterations)");
		}

		[TestMethod]
		public void SolverCanStopOnVMinViolation()
		{
			var builder = NetworkBuilder.Create("Producer[generatorVoltage=1000.0] -- Line1[r=10] -- Consumer[consumption=(100, 0); vMinV=1000]");
			_options.StopOnVMinViolation = true;

			AssertFlowStatus(builder, FlowStatus.Approximate, "Voltage dropped below VMin (1 iterations)");
		}

		[TestMethod]
		public void SolverCanStopOnIterationCount()
		{
			var builder = NetworkBuilder.Create("Producer[generatorVoltage=1000.0] -- Line1[r=10] -- Consumer[consumption=(100, 0)]");
			_options.MaxIterations = 3;

			AssertFlowStatus(builder, FlowStatus.Approximate, "Max iterations reached (3 iterations)");
		}

		[TestMethod]
		public void BaranWuIsSolved()
		{
			var network = IO.PgoJsonParser.ParseNetworkFromJsonFile(TestUtils.TestDataFile("baran-wu.json"));
			var periodData = IO.PgoJsonParser.ParseDemandsFromJsonFile(network, TestUtils.TestDataFile("baran-wu_forecast.json"));

			var configuration = new NetworkConfiguration(network, new SwitchSettings(network));
			var flowProblem = new FlowProblem(configuration, periodData[0].Demands);

			var flow = ComputeFlow(flowProblem);

			AssertFlowIsCorrect(flow);
		}

		[TestMethod]
		public void OpenLinesAreHandled()
		{
			NetworkBuilder builder = new NetworkBuilder();

			builder.Add("Producer[generatorVoltage=1000.0] -- Line1[r=1] -- Consumer1[consumption=(100, 0)]");
			builder.Add("Consumer1 -- Line2[r=1] -- Consumer2[consumption=(100, 0)]");
			builder.Add("Producer -- OpenLine[open] -- Consumer2");

			AssertFlowStatus(builder, FlowStatus.Exact);
		}

		[TestMethod]
		public void DisconnectedGeneratorsIsHandled()
		{
			NetworkBuilder builder = new NetworkBuilder();

			builder.Add("Producer1[generatorVoltage=1000.0]");
			builder.Add("Producer2[generatorVoltage=1000.0] -- Line -- Node");

			AssertFlowStatus(builder, FlowStatus.Exact);
		}

		[TestMethod]
		public void ZeroImpedanceIsHandled()
		{
			NetworkBuilder builder = new NetworkBuilder();

			builder.Add("Producer[generatorVoltage=1000.0] -- Line[z=(0,0)] -- Consumer[consumption=(100, 0)]");

			AssertFlowStatus(builder, FlowStatus.Exact);
		}

		[TestMethod]
		public void DeepNetworksAreSolvedWithoutStackOverflow()
		{
			Random r = new Random();

			var deepNetwork = TestUtils.LinearNetwork(50000, r);
			var demands = new PowerDemands(deepNetwork);
			demands.SetAllConsumerDemands(new Complex(1, 0));
			var config = NetworkConfiguration.AllClosed(deepNetwork);
			FlowProblem flowProblem = new FlowProblem(config, demands);

			var flow = ComputeFlow(flowProblem);

			AssertFlowIsCorrect(flow);
		}

		[TestMethod]
		public void ProviderCanTransferAggregatedFlowToOriginalNetwork()
		{
			// Create a network with parallel and serial lines

			NetworkBuilder builder = new NetworkBuilder();
			builder.Add("Producer[generatorVoltage=1000.0] -- Line1[r=1] -- Consumer1[consumption=(100, 0)]");
			builder.Add("Producer -- Line2[r=1] -o- Line3[z=(1,1)] -- Consumer1");
			
			VerifyAggregateSolveAndDisaggregate(builder);
		}

		[TestMethod]
		public void FlowIsCorrect_WithTransformer()
		{
			var builder = new NetworkBuilder();
			builder.Add("N1[generatorVoltage=11000] -- l1 -- N_T_in");
			builder.Add("N_T_out -- l2 -- Consumer[consumption=(100000,0)]");
			builder.AddBus("Transformer1[transformer; ends=(N_T_in, N_T_out); voltages=(11000, 22000); operation=fixed; factor=0.98]");

			AssertFlowStatus(builder, FlowStatus.Exact);
		}

		[TestMethod]
		public void ProviderCanDisaggregate_WithTransformer()
		{
			// Create a network with transformer and parallel lines

			var builder = new NetworkBuilder();
			builder.Add("N1[generatorVoltage=11000] -- l1a -- N_T_in");
			builder.Add("N1 -- l1b -- N_T_in");
			builder.Add("N_T_out -- l2 -- Consumer[consumption=(100000,0)]");
			builder.AddBus("Transformer1[transformer; ends=(N_T_in, N_T_out); voltages=(11000, 22000); operation=fixed; factor=0.98]");

			VerifyAggregateSolveAndDisaggregate(builder);
		}

		[TestMethod]
		public void ProviderCanDisaggregate_WithDanglingLines()
		{
			// Create a network with dangling lines

			var builder = new NetworkBuilder();
			builder.Add("Gen[generatorVoltage=11000] -- L1 -- N1 -- L2 -- N2 -- L3 -- Consumer[consumer]");
			builder.Add("N1 -- L5 -- N5 -- L6 -- N6 -- L7 -- N5");

			VerifyAggregateSolveAndDisaggregate(builder);
		}

		[TestMethod]
		public void ProviderCanDisaggregate_WithZeroImpedance()
		{
			var builder = new NetworkBuilder();
			builder.Add("N1[generatorVoltage=1000] -- L1[z=(0,0)] -o- L2[z=(1,0)] -- N2[consumption=(100,0)]");
			builder.Add("N3[generatorVoltage=1000] -- L3[z=(0,0)] -- N4[consumption=(100,0)]");
			builder.Add("N3 -- L4[z=(1,0)] -- N4");

			VerifyAggregateSolveAndDisaggregate(builder);
		}

		[TestMethod]
		public void ProviderCanDisaggregate_WithZeroImpedanceAndInfiniteImax()
		{
			var builder = new NetworkBuilder();
			builder.Add("N1[generatorVoltage=1000] -- L1[z=(0,0)] -o- L2[z=(1,0);iMax=Infinity] -- N2[consumption=(100,0)]");
			builder.Add("N3[generatorVoltage=1000] -- L3[z=(0,0)] -- N4[consumption=(100,0)]");
			builder.Add("N3 -- L4[z=(1,0);iMax=Infinity] -- N4");

			VerifyAggregateSolveAndDisaggregate(builder);
		}

		[TestMethod]
		public void ProviderCanDisaggregate_WithZeroImpedanceAndZeroImax()
		{
			var builder = new NetworkBuilder();
			builder.Add("N1[generatorVoltage=1000] -- L1[z=(0,0);iMax=0] -o- L2[z=(1,0);iMax=0] -- N2[consumption=(100,0)]");
			builder.Add("N3[generatorVoltage=1000] -- L3[z=(0,0);iMax=0] -- N4[consumption=(100,0)]");
			builder.Add("N3 -- L4[z=(1,0);iMax=0] -- N4");

			VerifyAggregateSolveAndDisaggregate(builder);
		}

		private void AssertFlowStatus(NetworkBuilder builder, FlowStatus expectedStatus, string expectedDetails = null)
		{
			FlowProblem flowProblem = builder.FlowProblem;

			var flow = ComputeFlow(flowProblem);

			Assert.AreEqual(expectedStatus, flow.Status);

			if (expectedDetails != null)
				Assert.AreEqual(expectedDetails, flow.StatusDetails);

			if (flow.Status == FlowStatus.Exact)
				AssertFlowIsCorrect(flow);

			if (flowProblem.NetworkConfig.Network.LineCount < 20)
				flow.Write();
		}

		private IPowerFlow ComputeFlow(FlowProblem flowProblem)
		{
			return new IteratedDistFlowProvider(_options).ComputeFlow(flowProblem);
		}

		private static void VerifyAggregateSolveAndDisaggregate(NetworkBuilder builder)
		{
			// Build aggregated problem

			var aggregation = NetworkAggregation.MakeAcyclicAndConnected(builder.Network);
			var originalProblem = builder.SinglePeriodProblem;
			var aggregatedProblem = originalProblem.CreateAggregatedProblem(aggregation, true);

			// Solve flow problem

			FlowProblem originalFlowProblem = new FlowProblem(
				NetworkConfiguration.AllClosed(originalProblem.Network),
				originalProblem.AllPeriodData.Single().Demands);
			FlowProblem aggregateFlowProblem = new FlowProblem(
				NetworkConfiguration.AllClosed(aggregatedProblem.Network),
				aggregatedProblem.AllPeriodData.Single().Demands);

			var provider = new IteratedDistFlowProvider(IteratedDistFlowProvider.DefaultOptions);
			var aggregateFlow = provider.ComputeFlow(aggregateFlowProblem);

			// The flow is consistent

			aggregateFlow.Write();

			AssertFlowIsCorrect(aggregateFlow);

			// Disaggregate flow

			var originalFlow = provider.DisaggregateFlow(aggregateFlow, aggregation,
				NetworkConfiguration.AllClosed(originalProblem.Network), originalProblem.AllPeriodData.Single().Demands);

			// The disaggregated flow is also consistent

			originalFlow.Write();

			AssertFlowIsCorrect(originalFlow);
		}

		private static void AssertFlowIsCorrect(IPowerFlow flow)
		{
			Assert.AreEqual(
				"NoCurrent: ok; " +
				"Current: ok; " +
				"PowerLoss: ok; " +
				"LineBalance: ok; " +
				"NodeBalance: ok; " +
				"PowerLossTransformer: ok; " +
				"Transformer voltage: ok; " +
				"TransformerLineBalance: ok",
				flow.FlowConsistencyReportWithTransformers());
		}
	}
}

