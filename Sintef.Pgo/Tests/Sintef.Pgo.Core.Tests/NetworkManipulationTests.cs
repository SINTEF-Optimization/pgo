using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core.Test
{
	/// <summary>
	/// Tests for the network aggregation logic
	/// </summary>
	[TestClass]
	public class NetworkManipulationTests
	{
		/// <summary>
		/// The original network
		/// </summary>
		PowerNetwork _originalNetwork;

		/// <summary>
		/// The original problem, based on the original network
		/// </summary>
		PgoProblem _originalProblem;

		/// <summary>
		/// The aggregation of the original network
		/// </summary>
		NetworkAggregation _aggregation;

		/// <summary>
		/// The aggregated network
		/// </summary>
		PgoProblem _aggregateProblem;

		/// <summary>
		/// A flow provider to use.
		/// </summary>
		IFlowProvider _flowP;


		[TestInitialize]
		public void Setup()
		{
			DefaultFlowProviderFactory fact = new DefaultFlowProviderFactory();
			_flowP = fact.CreateFlowProvider(FlowApproximation.IteratedDF);

			//TODO make spesific network?
			Random rand = new Random(42);
			_originalNetwork = TestUtils.LargeRandomNetwork(rand);
			//SetupNetwork("Generator[generator]  -- B[breaker] -o- S[closed] -o-  Line[faultsPerYear=0.1; sectioningTime=PT4H; repairTime=PT2H]  --  Consumer");
			_originalProblem = TestUtils.ProblemWithRandomDemands(_originalNetwork, rand, flowProvider: _flowP);

			_aggregation = NetworkAggregation.MakeAcyclicAndConnected(_originalProblem.Network);
			_aggregateProblem = _originalProblem.CreateAggregatedProblem(_aggregation, true);


		}

		/// <summary>
		/// Checks that a flow computed for the aggregate network equals the flow computed 
		/// for the original network, when the configuration computed for the aggregated network 
		/// is transferred to the original one.
		/// </summary>
		[TestMethod]
		public void TestAggregationOnFlows()
		{
			PgoSolution aggSol = Utils.ConstructFeasibleSolution(_aggregateProblem, new Random(42));
			Assert.IsTrue(aggSol.IsFeasible, "A feasible solution was not found for the aggregate");

			PeriodSolution aggPerSol = aggSol.SinglePeriodSolutions.Single();
			NetworkConfiguration config = aggSol.SinglePeriodSolutions.Single().NetConfig;
			IPowerFlow flowAggregate = aggPerSol.Flow(_flowP);// _flowP.ComputeFlow(new FlowProblem(config, _aggregateProblem.GetData(Period.Default).Demands));

			PgoSolution origSol = new PgoSolution(_originalProblem);
			PeriodSolution perSol = origSol.GetPeriodSolution(Period.Default.Index);
			perSol.NetConfig.CopySwitchSettingsFrom(config, _aggregation, _ => false);
			Assert.IsTrue(origSol.IsFeasible, "Copying switch settings does not yield a feasible solution");
			IPowerFlow origFlow = perSol.Flow(_flowP); //_flowP.ComputeFlow(new FlowProblem(perSol.NetConfig, perSol.PeriodData.Demands));

			//Checking production
			foreach (Bus provider in _originalNetwork.Providers)
			{
				Bus aggProv = _aggregateProblem.Network.GetBus(provider.Name);
				Assert.IsTrue(origFlow.PowerInjection(provider).ComplexEqualsWithTolerance(flowAggregate.PowerInjection(aggProv), 1E-9), "Power production not matching");
			}

			//Checking flow on all lines that were not aggregated
			foreach (Line	line in _originalNetwork.Lines)
			{
				if (_aggregateProblem.Network.TryGetLine(line.Name, out Line aggLine))
					Assert.IsTrue(origFlow.Current(line).ComplexEqualsWithTolerance(flowAggregate.Current(aggLine), 1E-9), "Current in line not matching");
			}

			//Checking voltage at each non-aggregated, non-provider, node 
			foreach (Bus bus in _originalNetwork.Buses.Where(b => !b.IsProvider))
			{
				if (_aggregateProblem.Network.HasBus(bus.Name))
				{
					Bus aggBus = _aggregateProblem.Network.GetBus(bus.Name);
					Assert.IsTrue(origFlow.Voltage(bus).ComplexEqualsWithTolerance(flowAggregate.Voltage(aggBus), 1E-9), "Voltage not matching at bus");
				}
			}
		}

		/// <summary>
		/// Sets up a network based on the given network data
		/// </summary>
		/// <param name="networkSpec">Network data to feed to a <see cref="NetworkBuilder"/></param>
		private PowerNetwork SetupNetwork(params string[] networkSpec)
		{
			NetworkBuilder builder = new NetworkBuilder();
			builder.UsePeriod(Period.Default);

			foreach (var line in networkSpec)
				builder.Add(line);

			return builder.Network;

			//if (_requireRadialNetwork)
			//	Assert.IsTrue(builder.Configuration.IsRadial);
		}
	}
}
