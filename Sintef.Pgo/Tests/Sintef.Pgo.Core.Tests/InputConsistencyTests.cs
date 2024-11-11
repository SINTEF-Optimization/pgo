using System.Collections.Generic;
using System;
using Sintef.Scoop.Utilities;
using Sintef.Pgo.DataContracts;
using API = Sintef.Pgo.DataContracts;

namespace Sintef.Pgo.Core.Test
{
	/// <summary>
	/// Functions for setting up test input in PGO's JSON format.
	/// The is common code used by tests for both the REST and the .NET APIs.
	/// </summary>
	public class InputConsistencyTests
	{
		/// <summary>
		/// Creates a network to be used as input to the service.
		/// </summary>
		/// <param name="actions">On exit, contains actions that modify the returned network</param>
		public static PowerGrid CreateTestNetwork(out VariationTester.ActionSets actions)
		{
			actions = new VariationTester.ActionSets();
			var good = actions.Good;
			var bad = actions.Bad;

			good.Add(() => { }, "Unmodified problem");

			// Create objects to be referenced from test actions early

			API.Line line = new();

			// Create network

			List<Node> nodes = new();
			List<API.Line> links = new();
			List<API.Transformer> transformers = new();

			var network = new PowerGrid
			{
				Lines = links,
				SchemaVersion = 1,
				Name = "network",
				Nodes = nodes,
				Transformers = transformers
			};


			// Tests for network and properties

			good.Add(() => { network.Nodes.Clear(); network.Lines.Clear(); network.Transformers.Clear(); }, "Empty network");
			good.Add(() => { network.Name = null; }, "No network name");
			good.Add(() => { network.Name = ""; }, "Empty network name");
			bad.Add(() => { network.SchemaVersion = -1; }, "SchemaVersion (json: @schema_version) must be set to 1.");

			// Tests for lists

			bad.Add(() => { network.Nodes = null; }, "The node list is null");
			bad.Add(() => { network.Nodes.Add(null); }, "The node list contains null");
			bad.Add(() => { network.Lines = null; }, "The line list is null");
			bad.Add(() => { network.Lines.Add(null); }, "The line list contains null");
			good.Add(() => { network.Transformers = null; }, "Null transformer list");
			bad.Add(() => { network.Transformers.Add(null); }, "The transformer list contains null");

			// Add nodes

			var generator = new Node
			{
				Id = "Generator",
				Type = NodeType.PowerProvider,
				GeneratorVoltage = 1000,
				MinimumActiveGeneration = 0,
				MaximumActiveGeneration = 100,
				MinimumReactiveGeneration = 0,
				MaximumReactiveGeneration = 100,
				Location = new() { 1, 2 }
			};

			var node = new Node { Id = "Node", Type = NodeType.Connection };

			var consumer = new Node
			{
				Id = "Consumer",
				Type = NodeType.PowerConsumer,
				ConsumerType = API.ConsumerCategory.Household,
				MinimumVoltage = 800,
				MaximumVoltage = 1000
			};

			nodes.Add(generator);
			nodes.Add(node);
			nodes.Add(consumer);

			// Tests for node properties

			bad.Add(() => { generator.Id = null; }, "The node ID may not be null");
			bad.Add(() => { generator.Id = ""; }, "The node ID may not be an empty string");
			bad.Add(() => { generator.Id = node.Id; }, "The ID 'Node' is used by more than one node");
			bad.Add(() => { generator.Location.Clear(); }, "Node 'Generator': The coordinates must contain exactly two values");
			bad.Add(() => { generator.Location.Add(5); }, "Node 'Generator': The coordinates must contain exactly two value");

			// Tests for generator node properties

			bad.Add(() => { generator.ConsumerType = API.ConsumerCategory.Agriculture; }, "Node 'Generator': A provider node may not specify consumer type(s)");
			bad.Add(() => { generator.ConsumerTypeFractions = new(); }, "Node 'Generator': A provider node may not specify consumer type(s");
			bad.Add(() => { generator.MinimumVoltage = 5; }, "Node 'Generator': A provider node may not specify minimum voltage");
			bad.Add(() => { generator.MaximumVoltage = 5; }, "Node 'Generator': A provider node may not specify maximum voltage");
			bad.Add(() => { generator.GeneratorVoltage = 0; }, "Node 'Generator': The generator voltage must be positive");
			good.Add(() => { generator.MinimumActiveGeneration = 100; }, "Fixed active generation");
			bad.Add(() => { generator.MinimumActiveGeneration = 101; }, "Node 'Generator': The maximum active generation must be larger than the minimum active generation");
			bad.Add(() => { generator.MinimumActiveGeneration = -1; }, "Node 'Generator': The minimum active generation may not be negative");
			good.Add(() => { generator.MinimumReactiveGeneration = 100; }, "Fixed reactive generation");
			bad.Add(() => { generator.MinimumReactiveGeneration = 101; }, "Node 'Generator': The maximum reactive generation must be larger than the minimum reactive generation");
			good.Add(() => { generator.MinimumReactiveGeneration = -10; generator.MaximumReactiveGeneration = -5; }, "Negative reactive generation");

			// Tests for transition node properties

			bad.Add(() => { node.ConsumerType = API.ConsumerCategory.Agriculture; }, "Node 'Node': A transition node may not specify consumer type(s)");
			bad.Add(() => { node.ConsumerTypeFractions = new(); }, "Node 'Node': A transition node may not specify consumer type(s");
			good.Add(() => { node.MinimumVoltage = 5; }, "Transition node minimum voltage");
			good.Add(() => { node.MaximumVoltage = 5; }, "Transition node maximum voltage");
			bad.Add(() => { node.MinimumVoltage = 6; node.MaximumVoltage = 5; }, "Node 'Node': The maximum voltage must be larger than the minimum voltage");
			bad.Add(() => { node.GeneratorVoltage = 1; }, "Node 'Node': A transition node may not specify a generator voltage");
			bad.Add(() => { node.MinimumActiveGeneration = 100; }, "Node 'Node': A transition node may not specify a minimum active generation");
			bad.Add(() => { node.MaximumActiveGeneration = 100; }, "Node 'Node': A transition node may not specify a maximum active generation");
			bad.Add(() => { node.MinimumReactiveGeneration = 100; }, "Node 'Node': A transition node may not specify a minimum reactive generation");
			bad.Add(() => { node.MaximumReactiveGeneration = 100; }, "Node 'Node': A transition node may not specify a maximum reactive generation");

			// Tests for consumer node properties

			bad.Add(() => { consumer.ConsumerType = API.ConsumerCategory.Undefined; line.FaultsPerYear = 1; },
				"Node 'Consumer': A consumer type or types must be specified, because a fault frequency is given for one or more lines");
			bad.Add(() => { consumer.ConsumerTypeFractions = new(); }, "Node 'Consumer': Only one of ConsumerType and ConsumerTypeFractions may be given.");
			good.Add(() => { UseConsumerTypeFractions(); }, "Using consumer type fractions");
			bad.Add(() => { UseConsumerTypeFractions(); consumer.ConsumerTypeFractions[0].ConsumerType = consumer.ConsumerTypeFractions[1].ConsumerType; },
				"Node 'Consumer': A consumption fraction for consumer type Household is given more than once");
			bad.Add(() => { UseConsumerTypeFractions(); consumer.ConsumerTypeFractions[0].ConsumerType = API.ConsumerCategory.Undefined; },
				"Node 'Consumer': Consumer type must be specified for each consumer type fraction");
			bad.Add(() => { UseConsumerTypeFractions(); ++consumer.ConsumerTypeFractions[0].PowerFraction; }, "Node 'Consumer': Fractions of consumer categories sums to 2 (should be 1)");
			bad.Add(() => { UseConsumerTypeFractions(); ++consumer.ConsumerTypeFractions[0].PowerFraction; --consumer.ConsumerTypeFractions[1].PowerFraction; },
				"Node 'Consumer': Each fraction of consumer categories must be positive");
			good.Add(() => { consumer.MinimumVoltage = 0; }, "Consumer no minimum voltage");
			good.Add(() => { consumer.MaximumVoltage = double.PositiveInfinity; }, "Consumer no maximum voltage");
			bad.Add(() => { consumer.MinimumVoltage = 6; consumer.MaximumVoltage = 5; }, "Node 'Consumer': The maximum voltage must be larger than the minimum voltage");
			bad.Add(() => { consumer.GeneratorVoltage = 1; }, "Node 'Consumer': A consumer node may not specify a generator voltage");
			bad.Add(() => { consumer.MinimumActiveGeneration = 100; }, "Node 'Consumer': A consumer node may not specify a minimum active generation");
			bad.Add(() => { consumer.MaximumActiveGeneration = 100; }, "Node 'Consumer': A consumer node may not specify a maximum active generation");
			bad.Add(() => { consumer.MinimumReactiveGeneration = 100; }, "Node 'Consumer': A consumer node may not specify a minimum reactive generation");
			bad.Add(() => { consumer.MaximumReactiveGeneration = 100; }, "Node 'Consumer': A consumer node may not specify a maximum reactive generation");

			void UseConsumerTypeFractions()
			{
				consumer.ConsumerType = API.ConsumerCategory.Undefined;
				consumer.ConsumerTypeFractions = new()
				{
					new ConsumerTypeFracton { ConsumerType = API.ConsumerCategory.Public, PowerFraction = 0.2 },
					new ConsumerTypeFracton { ConsumerType = API.ConsumerCategory.Household, PowerFraction = 0.8 }
				};
			}

			// Add lines

			line = new API.Line { Id = "Line", SourceNode = "Generator", TargetNode = "Node" };
			var line2 = new API.Line { Id = "Line2", SourceNode = "Node", TargetNode = "Consumer" };

			links.Add(line);
			links.Add(line2);

			// Tests for lines

			bad.Add(() => { line.Id = null; }, "The line ID may not be null");
			bad.Add(() => { line.Id = ""; }, "The line ID may not be an empty string");
			bad.Add(() => { line2.Id = line.Id; }, "The ID 'Line' is used by more than one line");
			bad.Add(() => { line.SourceNode = null; }, "Line 'Line': The source node ID may not be null");
			bad.Add(() => { line.SourceNode = "???"; }, "Line 'Line': There is no node with ID '???' (used as the line source)");
			bad.Add(() => { line.TargetNode = null; }, "Line 'Line': The target node ID may not be null");
			bad.Add(() => { line.TargetNode = "???"; }, "Line 'Line': There is no node with ID '???' (used as the line target)");
			good.Add(() => { line.Resistance = 0; }, "Zero resistance");
			good.Add(() => { line.Resistance = 1; }, "Positive resistance");
			bad.Add(() => { line.Resistance = -1; }, "Line 'Line': The resistance may not be negative");
			good.Add(() => { line.Reactance = 0; }, "Zero reactance");
			good.Add(() => { line.Reactance = 1; }, "Positive reactance");
			good.Add(() => { line.Reactance = -1; }, "Negative reactance");
			good.Add(() => { line.CurrentLimit = 0; }, "Zero current limit");
			good.Add(() => { line.CurrentLimit = 1; }, "Positive current limit");
			bad.Add(() => { line.CurrentLimit = -1; }, "Line 'Line': The maximum current limit may not be negative");
			good.Add(() => { line.VoltageLimit = 1; }, "Positive voltage limit");
			bad.Add(() => { line.VoltageLimit = 0; }, "Line 'Line': The maximum voltage limit must be positive");
			good.Add(() => { line.IsSwitchable = false; }, "Not switchable");
			good.Add(() => { line.IsSwitchable = true; }, "Switchable");
			good.Add(() => { line.SwitchingCost = null; }, "No switching cost");
			good.Add(() => { line.SwitchingCost = 0; }, "Zero switching cost");
			good.Add(() => { line.SwitchingCost = 1; }, "Positivie switching cost");
			good.Add(() => { line.SwitchingCost = -1; }, "Negative switching cost");
			good.Add(() => { line.IsBreaker = false; }, "Not breaker");
			good.Add(() => { line.IsBreaker = true; }, "Breaker");

			good.Add(() => { line.FaultsPerYear = null; }, "No faults");
			good.Add(() => { line.FaultsPerYear = 0; }, "Zero faults");
			good.Add(() => { line.FaultsPerYear = 1; }, "Positive faults");
			bad.Add(() => { line.FaultsPerYear = -1; }, "Line 'Line': The fault frequency may not be negative");
			good.Add(() => { line.SectioningTime = null; }, "No sectioning time");
			good.Add(() => { line.SectioningTime = Hours(0); }, "Zero sectioning time");
			good.Add(() => { line.SectioningTime = Hours(1); }, "Positive sectioning time");
			bad.Add(() => { line.SectioningTime = Hours(-1); }, "Line 'Line': The sectioning time may not be negative");
			good.Add(() => { line.RepairTime = null; }, "No repair time");
			good.Add(() => { line.RepairTime = Hours(0); }, "Zero repair time");
			good.Add(() => { line.RepairTime = Hours(1); }, "Positive repair time");
			bad.Add(() => { line.RepairTime = Hours(-1); }, "Line 'Line': The repair time may not be negative");

			TimeSpan Hours(int hours) => TimeSpan.FromHours(hours);

			// Add transformers

			var transformer = new API.Transformer()
			{
				Id = "Transformer",
				CenterLocation = new() { 1, 2 }
			};
			TransformerConnection connection = new TransformerConnection { NodeId = "Node", Voltage = 50 };
			TransformerConnection connection2 = new TransformerConnection { NodeId = "Consumer", Voltage = 20 };
			transformer.Connections.Add(connection);
			transformer.Connections.Add(connection2);
			var mode = AddMode(transformer, "Node", "Consumer");

			TransformerMode AddMode(API.Transformer t, string fromNode, string toNode)
			{
				TransformerMode mode = new TransformerMode
				{
					Source = fromNode,
					Target = toNode,
					Operation = TransformerOperation.Automatic,
					Bidirectional = true,
					PowerFactor = 1
				};
				t.Modes.Add(mode);
				return mode;
			}

			var transformer2 = new API.Transformer() { Id = "Transformer2", };
			transformer2.Connections.Add(new TransformerConnection { NodeId = "Generator", Voltage = 70 });
			transformer2.Connections.Add(new TransformerConnection { NodeId = "Node", Voltage = 50 });
			transformer2.Connections.Add(new TransformerConnection { NodeId = "Consumer", Voltage = 20 });
			AddMode(transformer2, "Node", "Consumer");
			AddMode(transformer2, "Node", "Generator");

			transformers.Add(transformer);
			transformers.Add(transformer2);

			// Tests for transformers

			bad.Add(() => { transformer.Id = null; }, "The transformer ID may not be null");
			bad.Add(() => { transformer.Id = ""; }, "The transformer ID may not be an empty string");
			bad.Add(() => { transformer2.Id = transformer.Id; }, "The ID 'Transformer' is used by more than one transformer");

			bad.Add(() => { transformer.Connections = null; }, "Transformer 'Transformer': The list of connections is null");
			bad.Add(() => { transformer.Connections.Add(null); }, "Transformer 'Transformer': The list of connections contains null");
			bad.Add(() => { transformer.Connections.Clear(); }, "Transformer 'Transformer': No connections are defined");
			bad.Add(() => { transformer.Connections.RemoveAt(0); },
				"Transformer 'Transformer': 1 connections is not allowed. Only transformers with two or three connections are supported");
			bad.Add(() => { transformer2.Connections.Add(new TransformerConnection { NodeId = "Node", Voltage = 20 }); },
				"Transformer 'Transformer2': 4 connections is not allowed. Only transformers with two or three connections are supported");
			bad.Add(() => { connection.NodeId = null; }, "Transformer 'Transformer': A connection node ID is null");
			bad.Add(() => { connection.NodeId = "???"; }, "Transformer 'Transformer': There is no node with ID '???' (used as a connection node)");
			bad.Add(() => { connection.NodeId = connection2.NodeId; }, "Transformer 'Transformer': The node 'Consumer' is used in more than one connection");
			good.Add(() => { connection.Voltage = 1; }, "Positive connection voltage");
			bad.Add(() => { connection.Voltage = 0; }, "Transformer 'Transformer': The voltage at each connection must be positive");

			bad.Add(() => { transformer.Modes = null; }, "Transformer 'Transformer': The list of modes is null");
			bad.Add(() => { transformer.Modes.Clear(); }, "Transformer 'Transformer': No modes are defined");
			bad.Add(() => { transformer.Modes.Add(null); }, "Transformer 'Transformer': The list of modes contains null");
			bad.Add(() => { mode.Source = null; }, "Transformer 'Transformer': The source node ID of a mode is null");
			bad.Add(() => { mode.Source = "???"; }, "Transformer 'Transformer': There is no node with ID '???' (used as a mode's source node)");
			bad.Add(() => { mode.Target = null; }, "Transformer 'Transformer': The target node ID of a mode is null");
			bad.Add(() => { mode.Target = "???"; }, "Transformer 'Transformer': There is no node with ID '???' (used as a mode's target node)");
			bad.Add(() => { mode.Target = mode.Source; }, "Transformer 'Transformer': Illegal mode has node 'Node' both as source and target");
			bad.Add(() => { mode.Ratio = 1; }, "Transformer 'Transformer': A voltage ratio may not be given for a mode with automatic operation");
			good.Add(() => { mode.Operation = TransformerOperation.FixedRatio; mode.Ratio = 1; }, "Fixed ratio transformer mode");
			bad.Add(() => { mode.Operation = TransformerOperation.FixedRatio; mode.Ratio = 0; }, "Transformer 'Transformer': A mode's voltage ratio must be positive");
			bad.Add(() => { mode.Operation = TransformerOperation.FixedRatio; mode.Ratio = null; },
				"Transformer 'Transformer': A voltage ratio is required for a mode with fixed ratio operation");
			bad.Add(() => { mode.PowerFactor = 0; }, "Transformer 'Transformer': A mode's power factor must be positive");

			bad.Add(() => { transformer.CenterLocation.Clear(); }, "Transformer 'Transformer': The coordinates must contain exactly two values");
			bad.Add(() => { transformer.CenterLocation.Add(5); }, "Transformer 'Transformer': The coordinates must contain exactly two value");

			return network;
		}

		/// <summary>
		/// Creates the parameters for adding a session:
		///  - Demands
		///  - Initial configuration
		///  - Flag for whether (some) demands may be omitted
		/// </summary>
		/// <param name="actions">On exit, contains actions that modify the returned network</param>
		public static (Demand, SinglePeriodSettings, Func<bool>) CreateTestSession(out VariationTester.ActionSets actions)
		{
			actions = new VariationTester.ActionSets();
			var good = actions.Good;
			var bad = actions.Bad;

			good.Add(() => { }, "Unmodified session");


			// Create demands and start configuration

			Demand forecast = new() { SchemaVersion = 1 };
			SinglePeriodSettings config = new();
			bool allowUnspecifiedConsumerDemands = false;

			// Check for version tag
			bad.Add(() => { forecast.SchemaVersion = -1; }, "Error in the demands: SchemaVersion (json: @schema_version) must be set to 1.");

			// Add periods

			API.Period period1 = new API.Period { Id = "Period1", StartTime = Day(1), EndTime = Day(2) };
			API.Period period2 = new API.Period { Id = "Period2", StartTime = Day(2), EndTime = Day(10) };

			forecast.Periods = new() { period1, period2, };

			// Tests for periods

			bad.Add(() => { forecast.Periods = null; }, "The period list is null");
			bad.Add(() => { forecast.Periods.Clear(); }, "Error in the demands: No demand periods were specified");
			bad.Add(() => { forecast.Periods.Add(null); }, "Error in the demands: The list contains a null period");
			bad.Add(() => { period1.Id = null; }, "Error in the demands: A period ID (at index 0) is null or empty");
			bad.Add(() => { period1.Id = ""; }, "Error in the demands: A period ID (at index 0) is null or empty");
			bad.Add(() => { period1.Id = period2.Id; }, "Error in the demands: The ID 'Period2' is used for more than one period");
			bad.Add(() => { period1.EndTime = period1.StartTime; }, "Error in the demands: Period 'Period1' (at index 0) does not have a positive duration");
			bad.Add(() => { period1.EndTime = Day(3); }, "Error in the demands: Period 'Period2' does not start at the same time as period 'Period1' ends");
			bad.Add(() => { period2.StartTime = Day(3); }, "Error in the demands: Period 'Period2' does not start at the same time as period 'Period1' end");
			bad.Add(() => { period1.StartTime = Day(10); period1.EndTime = Day(11); }, "Error in the demands: Period 'Period2' does not start at the same time as period 'Period1' en");

			// Add demandss

			LoadSeries loads1 = new LoadSeries { NodeId = "Consumer1", ActiveLoads = new() { 1, 1 }, ReactiveLoads = new() { 1, 1 } };
			LoadSeries loads2 = new LoadSeries { NodeId = "Consumer2", ActiveLoads = new() { 1, 1 }, ReactiveLoads = new() { 1, 1 } };
			LoadSeries loads3 = new LoadSeries { NodeId = "Consumer3", ActiveLoads = new() { 1, 1 }, ReactiveLoads = new() { 1, 1 } };

			forecast.Loads = new() { loads1, loads2, loads3, };

			// Tests for demands

			bad.Add(() => { forecast.Loads = null; }, "The loads list is null");
			bad.Add(() => { forecast.Loads.Clear(); }, "Error in the demands: Loads are missing for the following consumers: Consumer1, Consumer2, Consumer3");
			bad.Add(() => { forecast.Loads.Add(null); }, "Error in the demands: The loads list contains null");
			bad.Add(() => { forecast.Loads.RemoveAt(0); }, "Error in the demands: Loads are missing for the following consumers: Consumer1");
			good.Add(() => { forecast.Loads.RemoveAt(0); allowUnspecifiedConsumerDemands = true; }, "Missing loads; allowed");
			bad.Add(() => { loads1.NodeId = null; }, "Loads are given for node ID null");
			bad.Add(() => { loads1.NodeId = "???"; }, "Error in the demands: Loads are given for unknown node '???'");
			bad.Add(() => { loads1.NodeId = "Prod"; }, "Error in the demands: Loads are given for node 'Prod' that is not a consumer");
			bad.Add(() => { loads1.NodeId = loads2.NodeId; }, "Error in the demands: Loads are given more than once for node 'Consumer2'");
			bad.Add(() => { loads1.ActiveLoads = null; }, "The active loads is null");
			bad.Add(() => { loads1.ActiveLoads.Add(1); }, "Error in the demands: Node 'Consumer1': The number of active loads does not equal the number of periods");
			good.Add(() => { loads1.ActiveLoads[0] = 0; }, "Zero active load");
			bad.Add(() => { loads1.ActiveLoads[0] = -1; }, "Error in the demands: Node 'Consumer1': Active loads may not be negative");
			bad.Add(() => { loads1.ReactiveLoads = null; }, "The reactive loads is null");
			bad.Add(() => { loads1.ReactiveLoads.Add(1); }, "Error in the demands: Node 'Consumer1': The number of reactive loads does not equal the number of periods");
			good.Add(() => { loads1.ReactiveLoads[0] = 0; }, "Zero reactive load");
			good.Add(() => { loads1.ReactiveLoads[0] = -1; }, "Negative reactive load");

			// Add switch settings

			SwitchState setting1 = new SwitchState { Id = "l1", Open = false };
			SwitchState setting2 = new SwitchState { Id = "l2", Open = true };
			SwitchState setting3 = new SwitchState { Id = "l3", Open = false };

			config.SwitchSettings = new() { setting2, setting3 };

			// Tests for switch settings

			good.Add(() => { config.Period = "???"; }, "Period is ignored");
			bad.Add(() => { config.SwitchSettings = null; }, "The list of switch settings is null");
			bad.Add(() => { config.SwitchSettings.Clear(); }, "Error in the start configuration: Settings are missing for one or more switchable lines: l2, l3");
			bad.Add(() => { config.SwitchSettings.Add(null); }, "Error in the start configuration: The list of switch settings contains null");
			bad.Add(() => { config.SwitchSettings.RemoveAt(0); }, "Error in the start configuration: Settings are missing for one or more switchable lines: l2");
			bad.Add(() => { setting2.Id = null; }, "Settings are given for line ID null");
			bad.Add(() => { setting2.Id = "???"; }, "Error in the start configuration: Settings are given for unknown line IDs: ???");
			bad.Add(() => { config.SwitchSettings.Add(setting2); }, "Error in the start configuration: Setting is given more than once for line 'l2'");
			bad.Add(() => { config.SwitchSettings.Add(setting1); }, "Error in the start configuration: Settings are given for lines that are not switchable: l1");

			return (forecast, config, () => allowUnspecifiedConsumerDemands);

			DateTime Day(int day) => new DateTime(2020, 1, 1, 0, 0, 0).AddDays(day);
		}

		/// <summary>
		/// Creates a solution to be added to the service.
		/// </summary>
		/// <param name="actions">On exit, contains actions that modify the returned solution</param>
		public static Solution CreateSolution(out VariationTester.ActionSets actions)
		{
			actions = new VariationTester.ActionSets();
			var good = actions.Good;
			var bad = actions.Bad;

			good.Add(() => { }, "Unmodified solution");

			var solution = new Solution();

			// Add switch settings

			SinglePeriodSettings settings1 = new SinglePeriodSettings
			{
				Period = "1",
				SwitchSettings = new List<SwitchState>()
				{
					new SwitchState{ Id = "l2", Open = true },
					new SwitchState{ Id = "l3", Open = false }
				}
			};
			SinglePeriodSettings settings2 = new SinglePeriodSettings
			{
				Period = "0",
				SwitchSettings = new List<SwitchState>()
				{
					new SwitchState{ Id = "l2", Open = true },
					new SwitchState{ Id = "l3", Open = false }
				}
			};

			solution.PeriodSettings = new() { settings1, settings2 };

			// Tests for switch settings

			bad.Add(() => { solution.PeriodSettings = null; }, "Null settings");
			bad.Add(() => { solution.PeriodSettings.Clear(); }, "The solution does not cover some periods: 0, 1");
			bad.Add(() => { solution.PeriodSettings.Add(null); }, "The list of period solutions contains null");

			bad.Add(() => { settings1.Period = null; }, "The period ID is missing for a period solution");
			bad.Add(() => { settings1.Period = "???"; }, "The solution contains unknown periods: ???");
			bad.Add(() => { settings1.Period = "0"; }, "The solution contains period '0' more than once");

			bad.Add(() => { settings1.SwitchSettings[0] = null; }, "Period '1': The list of switch settings contains null");
			// Switch settings are tested more fully in CreateTestSession -- no need to duplicate here

			// Flows and KILE cost are ignored in the input

			solution.Flows = new List<PowerFlow> { null };
			solution.KileCosts = new List<KileCosts> { null };

			return solution;
		}
	}
}
