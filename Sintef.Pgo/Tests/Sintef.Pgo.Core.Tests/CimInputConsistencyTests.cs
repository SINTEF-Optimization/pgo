using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sintef.Pgo.Cim;
using Sintef.Scoop.Utilities;

using Voltage = UnitsNet.ElectricPotential;
using Resistance = UnitsNet.ElectricResistance;
using Conductance = UnitsNet.ElectricConductance;
using CurrentFlow = UnitsNet.ElectricCurrent;
using ActivePower = UnitsNet.Power;
using ReactivePower = UnitsNet.ReactivePower;
using ApparentPower = UnitsNet.ApparentPower;
using Sintef.Pgo.DataContracts;
using Sintef.Pgo.Core.IO;

namespace Sintef.Pgo.Core.Test
{
	/// <summary>
	/// Tests for catching errors in CIM input data (network, demands etc.) and that
	/// we produce helpful error messages
	/// </summary>
	[TestClass]
	public class CimInputConsistencyTests
	{
		[TestMethod]
		public void ErrorsInNetworkDefinitionAreReported()
		{
			// Indirect through a Func because a test action can replace the network object after CreateTestNetwork has been called
			Func<CimNetworkData> getNetworkData = null;

			new CimVariationTester().RunVariationChecks(Setup, Test, "An error occurred while parsing the network data:"
				, variationToRun: 34
				);


			VariationTester.ActionSets Setup()
			{
				getNetworkData = CreateTestNetwork(out var actions);
				return actions;
			}

			void Test()
			{
				var networkData = getNetworkData();
				var converter = new CimNetworkConverter(networkData.Network, networkData.ConversionOptions);
				converter.CreateNetwork();
			}
		}

		[TestMethod]
		public void ErrorsInDemandsDefinitionAreReported()
		{
			// Indirect through Func because a test action can replace objects after CreateTestDemands has been called
			Func<CimNetwork> getNetwork = null;
			Func<CimDemands> getDemands = null;

			CimNetworkConversionOptions options = null;
			new CimVariationTester().RunVariationChecks(Setup, Test, ""
				//, variationToRun: 6
				);


			VariationTester.ActionSets Setup()
			{
				(getNetwork, options, getDemands) = CreateTestDemands(out var actions);
				return actions;
			}

			void Test()
			{
				var pgoNetwork = new CimNetworkConverter(getNetwork(), options).CreateNetwork();

				var converter = new CimDemandsConverter(pgoNetwork);
				converter.ToPowerDemands(getDemands());
			}
		}

		[TestMethod]
		public void ErrorsInConfigurationDefinitionAreReported()
		{
			// Indirect through Func because a test action can replace objects after CreateTestDemands has been called
			Func<CimNetwork> getNetwork = null;
			Func<CimConfiguration> getConfig = null;

			CimNetworkConversionOptions options = null;
			new CimVariationTester().RunVariationChecks(Setup, Test, ""
				//, variationToRun: 6
				);


			VariationTester.ActionSets Setup()
			{
				(getNetwork, options, getConfig) = CreateTestConfiguration(out var actions);
				return actions;
			}

			void Test()
			{
				var networkConverter = new CimNetworkConverter(getNetwork(), options);
				var pgoNetwork = networkConverter.CreateNetwork();

				var converter = new CimConfigurationConverter(networkConverter);
				converter.ToNetworkConfiguration(getConfig());
			}
		}

		[TestMethod]
		public void ErrorsInSolutionDefinitionAreReported()
		{
			// Indirect through Func because a test action can replace objects after CreateTestSolution has been called
			Func<CimNetwork> getNetwork = null;
			Func<CimDemands> getDemands = null;
			Func<CimSolution> getSolution = null;

			CimNetworkConversionOptions options = null;
			new CimVariationTester().RunVariationChecks(Setup, Test, ""
				//, variationToRun: 3
				);


			VariationTester.ActionSets Setup()
			{
				(getNetwork, options, getDemands, getSolution) = CreateTestSolution(out var actions);
				return actions;
			}

			void Test()
			{
				// A lot of setup...

				var networkConverter = new CimNetworkConverter(getNetwork(), options);
				var pgoNetwork = networkConverter.CreateNetwork();

				var demandsConverter = new CimDemandsConverter(pgoNetwork);
				var demands = demandsConverter.ToPowerDemands(getDemands());
				var period = PgoJsonParser.ParsePeriods(new() { TestUtils.DefaultPeriod })[0];
				var periodData = new PeriodData(pgoNetwork, demands, period);

				var problem = new PgoProblem(new[] { periodData }, new SimplifiedDistFlowProvider(), "problem");

				// Do the solution conversion we're testing

				var converter = new CimSolutionConverter(networkConverter);
				converter.ConvertToPgo(getSolution(), problem);
			}
		}

		/// <summary>
		/// Creates a CIM network that exercises all features we support in translation,
		/// and the options that should be used to convert it.
		/// Also sets up the sets of actions that create good or bad variations of the network data.
		/// </summary>
		private Func<CimNetworkData> CreateTestNetwork(out VariationTester.ActionSets actions)
		{
			actions = new VariationTester.ActionSets();
			var good = actions.Good;
			var bad = actions.Bad;

			good.Add(() => { }, "Unmodified problem");

			var builder = new CimBuilder();
			var options = new CimNetworkConversionOptions()
			{
				ConsumerSources = new() { CimConsumerSource.EnergyConsumers, CimConsumerSource.EquivalentInjections }
			};
			Action<CimNetwork> modify = null;

			// Declare some objects that will be created
			PowerTransformer transformer = null;
			PowerTransformerEnd highVoltageEnd = null;
			PowerTransformerEnd lowVoltageEnd = null;

			// Tests for null lists
			bad.Add(() => modify = n => n.ACLineSegments = null, "ACLineSegments is null");
			bad.Add(() => modify = n => n.GeneratingUnits = null, "GeneratingUnits is null");
			bad.Add(() => modify = n => n.PowerTransformers = null, "PowerTransformers is null");
			bad.Add(() => modify = n => n.Switches = null, "Switches = null");
			bad.Add(() => modify = n => n.EnergyConsumers = null, "EnergyConsumers is null");
			bad.Add(() => modify = n => n.EquivalentInjections = null, "EquivalentInjections is null");

			// Tests for lists containing null
			bad.Add(() => modify = n => n.ACLineSegments.Add(null), "ACLineSegments contains null");
			bad.Add(() => modify = n => n.GeneratingUnits.Add(null), "GeneratingUnits contains null");
			bad.Add(() => modify = n => n.PowerTransformers.Add(null), "PowerTransformers contains null");
			bad.Add(() => modify = n => n.Switches.Add(null), "Switches contains null");
			bad.Add(() => modify = n => n.EnergyConsumers.Add(null), "EnergyConsumers contains null");
			bad.Add(() => modify = n => n.EquivalentInjections.Add(null), "EquivalentInjections contains null");

			// Create base voltage and voltage level

			var baseVoltage = builder.AddBaseVoltage("base 240V", 240);
			var voltageLevel = builder.AddVoltageLevel("voltage level 240V", baseVoltage);

			// Tests for base voltage and voltage level

			bad.Add(() => baseVoltage.NominalVoltage = null, "Missing attribute 'nominalVoltage' on BaseVoltage with name 'base 240V' (MRID base 240V)");
			bad.Add(() => baseVoltage.NominalVoltage = Voltage.Zero, "BaseVoltage with name 'base 240V' (MRID base 240V): NominalVoltage must be positive");
			bad.Add(() => voltageLevel.BaseVoltage = null, "Missing associated 'BaseVoltage' for VoltageLevel with name 'voltage level 240V' (MRID voltage level 240V)");


			// Set up a generating unit

			var generatingUnit = builder.AddGeneratingUnit("generator", 1_000_000, 2_000_000);
			var machine = builder.AddSynchronousMachine("machine", generatingUnit, -500_000, 500_000, ratedUVolts: 240);
			var generatorTerminal = machine.Terminals[0];

			// Tests for generating unit

			bad.Add(() => generatingUnit.RotatingMachines = null, "GeneratingUnit with name 'generator' (MRID generator): The generator has no rotating machines");
			bad.Add(() => generatingUnit.RotatingMachines.Clear(), "GeneratingUnit with name 'generator' (MRID generator): The generator has no rotating machines");
			bad.Add(() => generatingUnit.MaxOperatingP = null, "Missing attribute 'maxOperatingP' on GeneratingUnit with name 'generator' (MRID generator)");
			bad.Add(() => generatingUnit.MinOperatingP = null, "Missing attribute 'minOperatingP' on GeneratingUnit with name 'generator' (MRID generator)");
			bad.Add(() => generatingUnit.MaxOperatingP = ActivePower.FromMegawatts(0.5), "GeneratingUnit with name 'generator' (MRID generator): MinOperatingP cannot be larger than MaxOperatingP");
			bad.Add(() => generatingUnit.MinOperatingP = ActivePower.FromMegawatts(-0.5), "GeneratingUnit with name 'generator' (MRID generator): MinOperatingP cannot be negative");

			bad.Add(() => machine.MaxQ = null, "Missing attribute 'maxQ' on SynchronousMachine with name 'machine' (MRID machine)");
			bad.Add(() => machine.MinQ = null, "Missing attribute 'minQ' on SynchronousMachine with name 'machine' (MRID machine)");
			bad.Add(() => machine.MaxQ = ReactivePower.FromMegavoltamperesReactive(-1), "SynchronousMachine with name 'machine' (MRID machine): MinQ cannot be larger than MaxQ");
			bad.Add(() => machine.Type = null, "Missing attribute 'type' on SynchronousMachine with name 'machine' (MRID machine)");
			bad.Add(() => machine.Type = SynchronousMachineKind.Condenser, "SynchronousMachine with name 'machine' (MRID machine): Only type 'Generator' is supported");
			bad.Add(() => machine.Type = SynchronousMachineKind.Motor, "SynchronousMachine with name 'machine' (MRID machine): Only type 'Generator' is supported");
			bad.Add(() => machine.RatedU = Voltage.FromVolts(0), "SynchronousMachine with name 'machine' (MRID machine): RatedU must be positive");
			bad.Add(() => machine.RatedU = null, "Cannot determine base voltage: SynchronousMachine with name 'machine' (MRID machine) has no EquipmentContainer (looking for base voltage because RatedU is not supplied)");
			good.Add(() => { machine.RatedU = null; Associate(machine, baseVoltage); }, "No rated voltage, base voltage given");
			good.Add(() => { machine.RatedU = null; Associate(machine, voltageLevel); }, "No rated voltage, voltage level given");
			TestTerminals(machine);

			good.Add(() => AddSecondMachine(), "Two rotating machines");
			bad.Add(() => { AddSecondMachine(); machine.RatedU = Voltage.FromVolts(241); }, "Found different values among the voltages of rotating machines of GeneratingUnit with name 'generator' (MRID generator)");
			bad.Add(() => AddSecondMachine(useSameNode: false), "Found different values among the ConnectivityNode for rotating machines of GeneratingUnit with name 'generator' (MRID generator)");


			// Set up a provider modelled as an equivalent injection

			var providerInjection = builder.AddEquivalentInjection("providerInjection", minPWatt: -200, maxPWatt: 0, minQVar: -100, maxQVar: 100);
			builder.Associate(providerInjection, voltageLevel);

			// Tests for provider modelled as an equivalent injection

			TestTerminals(providerInjection);
			bad.Add(() => providerInjection.EquipmentContainer = null, "EquivalentInjection with name 'providerInjection' (MRID providerInjection) has no EquipmentContainer (looking for base voltage because the injection is not connected to a transformer end)");
			good.Add(() => { providerInjection.EquipmentContainer = null; builder.Connect(providerInjection.Terminals[0], highVoltageEnd.Terminal); }, "Voltage given by adjacent transformer end");
			bad.Add(() => providerInjection.minP = null, "providerInjection): Either all of min/max P/Q must be given, or none");
			bad.Add(() => providerInjection.maxP = null, "providerInjection): Either all of min/max P/Q must be given, or none");
			bad.Add(() => providerInjection.minQ = null, "providerInjection): Either all of min/max P/Q must be given, or none");
			bad.Add(() => providerInjection.maxQ = null, "providerInjection): Either all of min/max P/Q must be given, or none");
			bad.Add(() => providerInjection.maxP = ActivePower.FromWatts(100), "providerInjection): minP is negative and maxP positive. This is not supported by PGO at the moment. To model a provider, give a negative maxP. To model a consumer, give a positive or zero minP, or leave it unspecified.");
			bad.Add(() => providerInjection.maxP = ActivePower.FromWatts(-300), "providerInjection): minP is larger than maxP");
			bad.Add(() => providerInjection.maxQ = ReactivePower.FromVoltamperesReactive(-200), "providerInjection): minQ is larger than maxQ");


			// Set up a consumer

			var consumer = builder.AddConsumer("consumer");
			builder.Associate(consumer, voltageLevel);

			// Tests for consumer

			TestTerminals(consumer);
			good.Add(() => consumer.EquipmentContainer = null, "No base voltage required");
			bad.Add(() => { consumer.EquipmentContainer = null; options.ConsumerMinVoltageFactor = 1; }, "Cannot determine base voltage: EnergyConsumer with name 'consumer' (MRID consumer) has no EquipmentContainer (looking for base voltage because option ConsumerMinVoltageFactor or ConsumerMaxVoltageFactor is given)");
			good.Add(() => { consumer.EquipmentContainer = null; options.ConsumerMinVoltageFactor = 1; Associate(consumer, baseVoltage); }, "Explicit base voltage given");
			good.Add(() => consumer.P = ActivePower.FromWatts(10), "Consumer active power ignored in network data");
			good.Add(() => consumer.Q = ReactivePower.FromVoltamperesReactive(10), "Consumer reactive power ignored in network data");
			good.Add(() => { consumer.P = ActivePower.FromWatts(10); consumer.Q = ReactivePower.FromVoltamperesReactive(10); }, "Consumer (re)active power ignored in network data");


			// Set up a consumer modelled as an equivalent injection

			var consumerInjection = builder.AddEquivalentInjection("consumerInjection", minPWatt: 100, maxPWatt: 200, minQVar: -100, maxQVar: 100);
			builder.Associate(consumerInjection, voltageLevel);

			// Tests for consumer modelled as an equivalent injection

			TestTerminals(consumerInjection);
			good.Add(() => consumerInjection.EquipmentContainer = null, "No base voltage required 2");
			bad.Add(() => { consumerInjection.EquipmentContainer = null; options.ConsumerMinVoltageFactor = 1; }, "Cannot determine base voltage: EquivalentInjection with name 'consumerInjection' (MRID consumerInjection) has no EquipmentContainer (looking for base voltage because option ConsumerMinVoltageFactor or ConsumerMaxVoltageFactor is given)");
			good.Add(() => { consumerInjection.EquipmentContainer = null; options.ConsumerMinVoltageFactor = 1; Associate(consumerInjection, baseVoltage); }, "Explicit base voltage given 2");
			bad.Add(() => consumerInjection.minP = null, "consumerInjection): Either all of min/max P/Q must be given, or none");
			bad.Add(() => consumerInjection.maxP = null, "consumerInjection): Either all of min/max P/Q must be given, or none");
			bad.Add(() => consumerInjection.minQ = null, "consumerInjection): Either all of min/max P/Q must be given, or none");
			bad.Add(() => consumerInjection.maxQ = null, "consumerInjection): Either all of min/max P/Q must be given, or none");
			good.Add(() => { consumerInjection.minP = consumerInjection.maxP = null; consumerInjection.minQ = consumerInjection.maxQ = null; }, "consumerInjection no min/max P/Q");
			bad.Add(() => consumerInjection.minP = ActivePower.FromWatts(-100), "consumerInjection): minP is negative and maxP positive. This is not supported by PGO at the moment. To model a provider, give a negative maxP. To model a consumer, give a positive or zero minP, or leave it unspecified.");
			bad.Add(() => consumerInjection.maxP = ActivePower.FromWatts(-300), "consumerInjection): minP is larger than maxP");
			bad.Add(() => consumerInjection.maxQ = ReactivePower.FromVoltamperesReactive(-200), "consumerInjection): minQ is larger than maxQ");
			good.Add(() => consumerInjection.P = ActivePower.FromWatts(10), "Injection active power ignored in network data");
			good.Add(() => consumerInjection.Q = ReactivePower.FromVoltamperesReactive(10), "Injection reactive power ignored in network data");
			good.Add(() => { consumerInjection.P = ActivePower.FromWatts(10); consumerInjection.Q = ReactivePower.FromVoltamperesReactive(10); },
				"Injection (re)active power ignored in network data");


			// Set up a transformer

			transformer = builder.AddTransformer("transformer");
			highVoltageEnd = builder.AddEnd(transformer, "high end", 11_000);
			lowVoltageEnd = builder.AddEnd(transformer, "low end", 240);

			// Tests for transformer

			good.Add(() => AddTerminal(transformer), "Don't care about the tranformer's terminals 1");
			good.Add(() => transformer.Terminals = null, "Don't care about the tranformer's terminals 2");
			good.Add(() => transformer.Terminals.Clear(), "Don't care about the tranformer's terminals 3");
			good.Add(() => transformer.Terminals.Add(null), "Don't care about the tranformer's terminals 4");
			bad.Add(() => highVoltageEnd.Terminal = null, "Missing associated 'Terminal' for PowerTransformerEnd with name 'high end' (MRID high end)");
			bad.Add(() => highVoltageEnd.RatedU = null, "Missing attribute 'ratedU' on PowerTransformerEnd with name 'high end' (MRID high end)");
			bad.Add(() => highVoltageEnd.RatedU = Voltage.FromKilovolts(0), "PowerTransformerEnd with name 'high end' (MRID high end): RatedU must be positive");

			bad.Add(() => transformer.PowerTransformerEnds = null, "PowerTransformer with name 'transformer' (MRID transformer): The transformer has no ends");
			bad.Add(() => transformer.PowerTransformerEnds.Clear(), "PowerTransformer with name 'transformer' (MRID transformer): The transformer has no ends");
			bad.Add(() => transformer.PowerTransformerEnds.Add(null), "PowerTransformer with name 'transformer' (MRID transformer): The list of transformer ends contains null");
			bad.Add(() => transformer.PowerTransformerEnds.Remove(highVoltageEnd), "PowerTransformer with name 'transformer' (MRID transformer): The transformer must have at least 2 ends");

			good.Add(() => Add3TerminalTransformer(), "3-terminal transformer is ok");


			// Set up a switch (fuse) from generating unit to transformer

			var fuse = builder.AddSwitch<Fuse>("fuse", generatorTerminal, highVoltageEnd.Terminal);

			// Tests for switches

			TestTerminals(fuse);

			// Check that each type of switch is handled
			TestSwitchType<Jumper>();
			TestSwitchType<Sectionalizer>();
			TestSwitchType<Disconnector>();
			TestSwitchType<GroundDisconnector>();
			TestSwitchType<ProtectedSwitch>();
			TestSwitchType<LoadBreakSwitch>();
			TestSwitchType<Breaker>();
			TestSwitchType<DisconnectingCircuitBreaker>();
			TestSwitchType<Recloser>();


			// Set up an AC line segment from transformer to consumer

			var line = builder.AddLine("line", 0.1, 0.2, lowVoltageEnd.Terminal, consumer.Terminals[0]);

			// Tests for AC line segments

			TestTerminals(line);
			bad.Add(() => line.R = null, "Missing attribute 'r' on ACLineSegment with name 'line' (MRID line)");
			bad.Add(() => line.X = null, "Missing attribute 'x' on ACLineSegment with name 'line' (MRID line)");
			bad.Add(() => line.R = Resistance.FromOhms(-0.1), "ACLineSegment with name 'line' (MRID line): The resistance 'r' cannot be negative");
			good.Add(() => line.X = Resistance.FromOhms(-0.1), "Negative reactance is ok (if dubious)");


			// Tests for general rules

			bad.Add(() => consumer.MRID = null, "EnergyConsumer with name 'consumer' (no MRID): No MRID is given");
			bad.Add(() => consumer.MRID = generatingUnit.MRID, "EnergyConsumer with name 'consumer' (MRID generator): MRID is not unique");
			bad.Add(() => generatorTerminal.ConnectivityNode = null, $"{generatorTerminal.Describe()}: The terminal has no connectivity node");

			good.Add(() =>
			{
				if (builder.CreatedObjects.Except(builder.Network.IdentifiedObjects()).FirstOrDefault() is IdentifiedObject x)
					throw new Exception($"Missed a {x.GetType().Name}");
			}, "Check that IdentifiedObjects() does find all IdentifiedObjects");

			return () =>
			{
				CimNetwork network = builder.Network;
				modify?.Invoke(network);

				return new() { Network = network, ConversionOptions = options };
			};



			// Local functions for modifying the network data

			void AddSecondMachine(bool useSameNode = true)
			{
				Terminal terminal = null;
				if (useSameNode)
					terminal = machine.Terminals[0];

				builder.AddSynchronousMachine("machine2", generatingUnit, -500_000, 500_000, 240, connectTo: terminal);
			}


			void TestSwitchType<TSwitch>() where TSwitch : Switch
			{
				// A switch of this type is accepted
				good.Add(() => { Create(); }, $"OK to add switch of type {typeof(TSwitch).Name}");
				// and errors are found and reported
				bad.Add(() => { var s = Create(); s.Terminals = null; }, $"{typeof(TSwitch).Name} with name 'addedSwitch' (MRID addedSwitch): The conducting equipment has no terminals");


				TSwitch Create() => builder.AddSwitch<TSwitch>("addedSwitch", from: fuse.Terminals[0], to: fuse.Terminals[1]);
			}


			void TestTerminals(ConductingEquipment equipment)
			{
				string id = equipment.Describe();
				int terminalCount = equipment.Terminals.Count;

				bad.Add(() => equipment.Terminals = null, $"{id}: The conducting equipment has no terminals");
				bad.Add(() => equipment.Terminals.Clear(), $"{id}: The conducting equipment has no terminals");
				bad.Add(() => equipment.Terminals.Add(null), $"{id}: The list of terminals contains null");
				bad.Add(() => AddTerminal(equipment), $"{id}: Found {terminalCount + 1} terminals -- expected {terminalCount}");

				if (equipment.Terminals.Count > 1)
				{
					string terimnalId = equipment.Terminals[1].MRID;
					string nodeId = equipment.Terminals[1].ConnectivityNode.MRID;

					bad.Add(() => equipment.Terminals[0] = equipment.Terminals[1], $"{id}: Terminal with MRID '{terimnalId}' appears twice");
					bad.Add(() => equipment.Terminals[0].ConnectivityNode = equipment.Terminals[1].ConnectivityNode,
						$"{id}: More than one terminal is connected to ConnectivityNode with MRID '{nodeId}'");
				}
			}


			void Add3TerminalTransformer()
			{
				var transformer = builder.AddTransformer("transformer2");
				builder.AddEnd(transformer, "high end2", 200_000);
				builder.AddEnd(transformer, "loew end2 A", 22_000);
				builder.AddEnd(transformer, "loew end2 B", 11_000);
			}


			Terminal AddTerminal(ConductingEquipment equipment) => builder.AddTerminal(equipment);

			void Associate(IdentifiedObject object1, IdentifiedObject object2) => builder.Associate(object1, object2);

		}

		/// <summary>
		/// Creates a CIM demands that exercises all features we support in translation.
		/// Also sets up the sets of actions that create good or bad variations of the demands data.
		/// </summary>
		private (Func<CimNetwork> GetNetwork, CimNetworkConversionOptions Options, Func<CimDemands> GetDemands) CreateTestDemands(out VariationTester.ActionSets actions)
		{
			actions = new VariationTester.ActionSets();
			var good = actions.Good;
			var bad = actions.Bad;

			good.Add(() => { }, "Unmodified demands");

			var networkBuilder = new CimBuilder();
			var options = new CimNetworkConversionOptions();
			var builder = new CimBuilder();


			// Add a consumer and demand

			networkBuilder.AddConsumer("consumer");
			var consumerDemand = builder.AddConsumer("consumer", activeDemandWatt: 10, reactiveDemandWatt: 10);

			// Tests for consumer demand

			bad.Add(() => consumerDemand.P = null, "EnergyConsumer with name 'consumer' (MRID consumer): No active power given");
			bad.Add(() => consumerDemand.Q = null, "EnergyConsumer with name 'consumer' (MRID consumer): No reactive power given");
			bad.Add(() => consumerDemand.P = ActivePower.FromWatts(0), "EnergyConsumer with name 'consumer' (MRID consumer): The active power for a consumer must be positive");
			good.Add(() => consumerDemand.Q = ReactivePower.FromVoltamperesReactive(-1), "Negative reactive power given");
			bad.Add(() => networkBuilder.AddConsumer("consumer2"), "No demand is specified for consumer 'consumer2'");
			good.Add(() => builder.AddConsumer("consumer2", activeDemandWatt: 10, reactiveDemandWatt: 10), "Extra demands given for unknown consumer");
			bad.Add(() => builder.AddConsumer("consumer2", activeDemandWatt: -10, reactiveDemandWatt: 10), "EnergyConsumer with name 'consumer2' (MRID consumer2): The active power for a consumer must be positive");


			// Add a consumer equivalent injection and demand

			networkBuilder.AddEquivalentInjection("eqConsumer");
			var eqConsumerDemand = builder.AddEquivalentInjection("eqConsumer", pWatt: 50, qVar: 10);

			// Tests for equivalent injection demand

			bad.Add(() => eqConsumerDemand.P = null, "EquivalentInjection with name 'eqConsumer' (MRID eqConsumer): No active power given");
			bad.Add(() => eqConsumerDemand.Q = null, "EquivalentInjection with name 'eqConsumer' (MRID eqConsumer): No reactive power given");
			bad.Add(() => eqConsumerDemand.P = ActivePower.FromWatts(0), "EquivalentInjection with name 'eqConsumer' (MRID eqConsumer): The active power for a consumer must be positive");
			good.Add(() => eqConsumerDemand.Q = ReactivePower.FromVoltamperesReactive(-1), "Negative reactive power given 2");
			bad.Add(() => networkBuilder.AddEquivalentInjection("eqConsumer2"), "No demand is specified for consumer 'eqConsumer2'");
			good.Add(() => builder.AddEquivalentInjection("eqConsumer2", pWatt: 10, qVar: 10), "Extra demands given for unknown eqconsumer");
			good.Add(() => builder.AddEquivalentInjection("eqConsumer2", pWatt: -10, qVar: 10), "Negative active power given for unknown eqconsumer");


			// Add a provider equivalent injection

			var eqProvider = networkBuilder.AddEquivalentInjection("providerInjection", minPWatt: -200, maxPWatt: 0, minQVar: -100, maxQVar: 100);
			var eqProviderFlow = builder.AddEquivalentInjection("providerInjection", pWatt: -100, qVar: 0);
			// Connect to a transformer to specify the voltage
			var transformer = networkBuilder.AddTransformer("transformer");
			networkBuilder.AddEnd(transformer, "high end", 11_000, connectTo: eqProvider.Terminals[0]);
			networkBuilder.AddEnd(transformer, "low end", 240);


			// Tests for provider equivalent injection

			bad.Add(() => eqProviderFlow.P = null, "EquivalentInjection with name 'providerInjection' (MRID providerInjection): No active power given");
			bad.Add(() => eqProviderFlow.Q = null, "EquivalentInjection with name 'providerInjection' (MRID providerInjection): No reactive power given");
			good.Add(() => builder.Remove(eqProviderFlow), "No power given for provider injection");
			bad.Add(() => eqProviderFlow.P = ActivePower.FromWatts(1), "EquivalentInjection with name 'providerInjection' (MRID providerInjection): The injection models a provider but consumes positive active power");



			return (() => networkBuilder.Network, options, () => builder.Demands);
		}

		/// <summary>
		/// Creates a CIM network configuration that exercises all features we support in translation.
		/// Also sets up the sets of actions that create good or bad variations of the configuration data.
		/// </summary>
		private (Func<CimNetwork> getNetwork, CimNetworkConversionOptions options, Func<CimConfiguration> getConfig) CreateTestConfiguration(out VariationTester.ActionSets actions)
		{
			actions = new VariationTester.ActionSets();
			var good = actions.Good;
			var bad = actions.Bad;

			good.Add(() => { }, "Unmodified configuration");

			var networkBuilder = new CimBuilder();
			var options = new CimNetworkConversionOptions();
			var builder = new CimBuilder();


			// Add switches and settings

			networkBuilder.AddSwitch<Breaker>("breaker");
			networkBuilder.AddSwitch<Fuse>("fuse");
			var breakerSetting = builder.AddSwitch<Breaker>("breaker");
			breakerSetting.Open = false;
			var fuseSetting = builder.AddSwitch<Fuse>("fuse");
			fuseSetting.Open = false;

			// Tests for switch setting

			good.Add(() => breakerSetting.Open = true, "Open breaker");
			bad.Add(() => breakerSetting.Open = null, "Breaker with name 'breaker' (MRID breaker): The 'open' property is missing");
			bad.Add(() => builder.AddSwitch<Breaker>("switch2"), "Breaker with name 'switch2' (MRID switch2): Not found in the network");
			bad.Add(() => fuseSetting.Open = true, "Fuse with name 'fuse' (MRID fuse): Opening a non-controllable switch is not supported");
			bad.Add(() => builder.AddSwitch<Breaker>("breaker"), "Breaker with name 'breaker' (MRID breaker): Occurs more than once");
			bad.Add(() => networkBuilder.AddSwitch<Breaker>("breaker2"), "No setting is given for controllable switch with MRID 'breaker2'");
			good.Add(() => networkBuilder.AddSwitch<Fuse>("fuse2"), "Missing setting for non-controllable switch");


			return (() => networkBuilder.Network, options, () => builder.Configuration);
		}

		/// <summary>
		/// Creates a CIM solution that exercises all features we support in translation.
		/// Also sets up the sets of actions that create good or bad variations of the solution data.
		/// </summary>
		private (Func<CimNetwork> getNetwork, CimNetworkConversionOptions options, Func<CimDemands> getDemands, Func<CimSolution> getSolution) CreateTestSolution(out VariationTester.ActionSets actions)
		{
			actions = new VariationTester.ActionSets();
			var good = actions.Good;
			var bad = actions.Bad;

			good.Add(() => { }, "Unmodified solution");

			var networkBuilder = new CimBuilder();
			var options = new CimNetworkConversionOptions();
			var demandBuilder = new CimBuilder();
			var builder = new CimBuilder();

			Func<List<CimPeriodSolution>> getPeriodSolutions = () => new() { builder.PeriodSolution };


			// Add switches and settings

			networkBuilder.AddSwitch<Breaker>("breaker");
			networkBuilder.AddSwitch<Fuse>("fuse");
			var breakerSetting = builder.AddSwitch<Breaker>("breaker");
			breakerSetting.Open = false;
			var fuseSetting = builder.AddSwitch<Fuse>("fuse");
			fuseSetting.Open = false;


			// Tests for switch setting

			good.Add(() => breakerSetting.Open = true, "Open breaker");
			bad.Add(() => breakerSetting.Open = null, "Period 'Period 1': Breaker with name 'breaker' (MRID breaker): The 'open' property is missing");
			bad.Add(() => builder.AddSwitch<Breaker>("switch2"), "Breaker with name 'switch2' (MRID switch2): Not found in the network");
			bad.Add(() => fuseSetting.Open = true, "Fuse with name 'fuse' (MRID fuse): Opening a non-controllable switch is not supported");
			bad.Add(() => builder.AddSwitch<Breaker>("breaker"), "Breaker with name 'breaker' (MRID breaker): Occurs more than once");
			bad.Add(() => networkBuilder.AddSwitch<Breaker>("breaker2"), "Period 'Period 1': No setting is given for controllable switch with MRID 'breaker2'");
			good.Add(() => networkBuilder.AddSwitch<Fuse>("fuse2"), "Missing setting for non-controllable switch");

			// Tests for period solutions

			bad.Add(() => getPeriodSolutions = () => null, "The list if period solutions is null");
			bad.Add(() => getPeriodSolutions = () => new(), "The problem has 1 periods, but the list of period solutions has 0 elements");
			bad.Add(() => getPeriodSolutions = () => new() { null } , "The list if period solutions contains null");
			bad.Add(() => getPeriodSolutions = () => new() { builder.PeriodSolution, builder.PeriodSolution }, "The problem has 1 periods, but the list of period solutions has 2 elements");
			bad.Add(() => getPeriodSolutions = () => new() { new() { Switches = null } }, "Period 'Period 1': The list of switches is null");
			bad.Add(() => getPeriodSolutions = () => new() { new() { Switches = new() { null } } }, "Period 'Period 1': The list of switches contains null");

			return (() => networkBuilder.Network, options, () => demandBuilder.Demands, () => new CimSolution { PeriodSolutions = getPeriodSolutions() });
		}
	}

	/// <summary>
	/// A variation tester for converting cim data
	/// </summary>
	public class CimVariationTester : VariationTester
	{
		public CimVariationTester()
		{
			// Set up exception classification: All are in service
			ClassifyException = exception => (ExceptionLocation.InService, exception);
		}
	}
}