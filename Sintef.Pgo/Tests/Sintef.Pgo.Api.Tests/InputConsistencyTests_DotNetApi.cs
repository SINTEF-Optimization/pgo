using Sintef.Pgo.Cim;
using Sintef.Pgo.Core;
using Sintef.Pgo.Core.Test;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Api.Tests
{
	/// <summary>
	/// Tests for catching errors in the input data (network, forecast etc.) and that
	/// we produce helpful error messages, when using the .NET API
	/// </summary>
	[TestClass]
	public class InputConsistencyTests_DotNetApi : ApiTestFixture
	{
		private DotNetApiVariationTester _tester = new DotNetApiVariationTester() { WriteLine = Console.Out.WriteLine };

		[TestMethod]
		public void ErrorsInJsonNetworkDefinitionAreReported()
		{
			PowerGrid? network = null;

			_tester.RunVariationChecks(Setup, Test, "An error occurred while parsing the network data:"
				//, variationToRun: 46
				);


			VariationTester.ActionSets Setup()
			{
				network = Core.Test.InputConsistencyTests.CreateTestNetwork(out var actions);
				return actions;
			}

			void Test()
			{
				_server.AddNetwork("id", network!);
				_server.RemoveNetwork("id");
			}
		}
		
		[TestMethod]
		public void ErrorsInJsonSessionDefinitionAreReported()
		{
			var builder = new NetworkBuilder();
			builder.Add("Prod[generatorVoltage=100000] -- l1 -- Consumer1[consumption=1]");
			builder.Add("Prod -- l2[open] -- Consumer2[consumption=1]");
			builder.Add("Prod -- l3[open] -- Consumer3[consumption=1]");

			AddNetwork(builder);

			Demand? demand = null;
			SinglePeriodSettings? startConfig = null;
			Func<bool>? allowUnspecifiedConsumerDemands = null;

			_tester.RunVariationChecks(Setup, Test, "Error in the demands:/Error in the start configuration:"
				//, variationToRun: 21
				);


			VariationTester.ActionSets Setup()
			{
				(demand, startConfig, allowUnspecifiedConsumerDemands) = InputConsistencyTests.CreateTestSession(out var actions);
				return actions;
			}

			void Test()
			{
				var session = AddJsonSession("id", _jsonNetworkId, demand!, startConfig!, allowUnspecifiedConsumerDemands!());

				_server.RemoveSession(session);
			}
		}

		[TestMethod]
		public void ErrorsInCimSessionDefinitionAreReported()
		{
			AddCimTestNetwork();

			CimSessionParameters? parameters = null;

			_tester.RunVariationChecks(Setup, Test, ""
				//, variationToRun: 9
				);


			VariationTester.ActionSets Setup()
			{
				parameters = CreateTestCimSession(out var actions);
				return actions;
			}

			void Test()
			{
				var session = _server.AddSession("id", parameters!);

				_server.RemoveSession(session);
			}
		}

		/// <summary>
		/// Creates parameters for a session and the actions that modify the parameters to produce
		/// variants that should be accepted (good) or cause an error (bad)
		/// </summary>
		private CimSessionParameters CreateTestCimSession(out VariationTester.ActionSets actions)
		{
			actions = new VariationTester.ActionSets();
			var good = actions.Good;
			var bad = actions.Bad;

			good.Add(() => { }, "Unmodified session");

			CimPeriodAndDemands periodAndDemands = new();

			CimSessionParameters parameters = new()
			{
				NetworkId = _cimNetworkId,
				PeriodsAndDemands = new() { periodAndDemands }
			};

			// Tests for main parameters

			bad.Add(() => parameters.NetworkId = null, "The network ID is null");
			bad.Add(() => parameters.NetworkId = "???", "There is no power network with ID '???'");


			// Create periods and demands

			var builder = new CimBuilder();
			var consumerDemand = builder.AddConsumer("consumer", activeDemandWatt: 10, reactiveDemandWatt: 10);

			periodAndDemands.Period = TestUtils.DefaultPeriod;
			periodAndDemands.Demands = builder.Demands;

			// Tests for periods and demands

			bad.Add(() => parameters.PeriodsAndDemands = null, "The list of periods and demands is null");
			bad.Add(() => parameters.PeriodsAndDemands.Add(null), "Error in the demands: The list of periods and demands contains null");
			bad.Add(() => periodAndDemands.Period = null, "Error in the demands: The list contains a null period");

			// More detailed tests for periods are found elsewhere. Example:
			bad.Add(() => periodAndDemands.Period.Id = null, "Error in the demands: A period ID (at index 0) is null or empty");

			bad.Add(() => periodAndDemands.Demands = null, "Error in the demands: Period 'Period 1': The demands is null");
			bad.Add(() => periodAndDemands.Demands.EnergyConsumers = null, "Error in the demands: Period 'Period 1': The list of energy consumers is null");
			bad.Add(() => periodAndDemands.Demands.EnergyConsumers.Add(null), "Error in the demands: Period 'Period 1': The list of energy consumers contains null");
			bad.Add(() => periodAndDemands.Demands.EquivalentInjections = null, "Error in the demands: Period 'Period 1': The list of equivalent injections is null");
			bad.Add(() => periodAndDemands.Demands.EquivalentInjections.Add(null), "Error in the demands: Period 'Period 1': The list of equivalent injections contains null");

			// More detailed tests for demands are found here:
			Action _ = new CimInputConsistencyTests().ErrorsInDemandsDefinitionAreReported;


			// Create start configuration

			var theSwitch = builder.AddSwitch<Breaker>("switch");
			theSwitch.Open = true;
			parameters.StartConfiguration = CimConfiguration.FromObjects(builder.CreatedObjects);

			// Tests for start configuration

			good.Add(() => parameters.StartConfiguration = null, "No start config");
			bad.Add(() => parameters.StartConfiguration.Switches = null, "Error in the start configuration: The list of switches is null");
			bad.Add(() => parameters.StartConfiguration.Switches.Add(null), "Error in the start configuration: The list of switches contains null");
			bad.Add(() => theSwitch.MRID = null, "Error in the start configuration: A switch has null MRID");

			// More detailed tests for configurations are found here:
			_ = new CimInputConsistencyTests().ErrorsInConfigurationDefinitionAreReported;

			return parameters;
		}
	}

	/// <summary>
	/// Helper for testing variations in input data, with adaptations to the
	/// PGO .NET API
	/// </summary>
	internal class DotNetApiVariationTester : VariationTester
	{
		public DotNetApiVariationTester()
		{
			Fail = Assert.Fail;
			ClassifyException = ClassifyDotNetException;
		}

		/// <summary>
		/// Determines whether an exception was thrown in the service or in the client,
		/// or by (de)serialization.
		/// </summary>
		private (ExceptionLocation, Exception) ClassifyDotNetException(Exception exception)
		{
			// Since there is no serialization, we blame all errors on the service
			return (ExceptionLocation.InService, exception);
		}
	}
}
