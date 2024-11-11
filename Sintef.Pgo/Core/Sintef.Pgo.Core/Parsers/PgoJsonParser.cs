using System;
using System.Numerics;
using System.IO;
using Newtonsoft.Json;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using Sintef.Scoop.Utilities;
using Sintef.Scoop.Utilities.GeoCoding;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Linq.Expressions;
using Sintef.Pgo.DataContracts;

using Core = Sintef.Pgo.Core;
using API = Sintef.Pgo.DataContracts;

namespace Sintef.Pgo.Core.IO
{
	/// <summary>
	/// Parser for our own Json format.
	/// 
	/// See the Examples folder for examples of how the format looks.
	/// </summary>
	public static class PgoJsonParser
	{
		/// <summary>
		///  Parses a <see cref="PowerNetwork"/> from the Parses the JSON file at the given location.
		/// </summary>
		/// <param name="inputfile">The name of the file to parse</param>
		/// <param name="modifyAction">If not null, this action is applied the the parsed power
		///   grid before it's turned into a <see cref="PowerNetwork"/></param>
		/// <returns>The PowerNetwork described by the file.</returns>
		public static Core.PowerNetwork ParseNetworkFromJsonFile(string inputfile, Action<PowerGrid> modifyAction = null)
		{
			// This is a bad and slow implementation
			return ParseNetworkJSON(File.ReadAllText(inputfile), modifyAction);
		}


		/// <summary>
		/// Saves the given problem to files, with file names for network and forecasts
		/// generated from the given file name basis.
		/// </summary>
		public static void SaveToFiles(string fileName, PgoProblem problem)
		{
			string networkFileName = Path.Combine(Path.GetDirectoryName(fileName), $"{Path.GetFileNameWithoutExtension(fileName)}_network.json");
			string forecastFilename = Path.Combine(Path.GetDirectoryName(fileName), $"{Path.GetFileNameWithoutExtension(fileName)}_forecast.json");

			using (var networkFileStream = File.Open(networkFileName, FileMode.Create))
			{
				IO.PgoJsonParser.WriteNetwork(problem.Network, networkFileStream, true);
			}
			using (var forecastFileStream = File.Open(forecastFilename, FileMode.Create))
			{
				IO.PgoJsonParser.WriteDemands(problem.AllPeriodData, forecastFileStream, true);
			}
		}

		/// <summary>
		/// Reads a problem from files, with file names for network and forecasts
		/// generated from the given file name basis in the same way as for <see cref="SaveToFiles"/>.
		/// Uses the given flow provider.
		/// </summary>
		/// <param name="fileName"></param>
		/// <param name="flowProvider">The flow provider to use for the new problem.</param>
		public static PgoProblem CreateProblemFromFiles(string fileName, IFlowProvider flowProvider)
		{
			string localFileNameBase = Path.GetFileNameWithoutExtension(fileName);
			string networkFileName = Path.Combine(Path.GetDirectoryName(fileName), $"{localFileNameBase}_network.json");
			string forecastFilename = Path.Combine(Path.GetDirectoryName(fileName), $"{localFileNameBase}_forecast.json");

			var network = ParseNetworkFromJsonFile(networkFileName);
			List<PeriodData> forecasts = ParseDemandsFromJsonFile(network, forecastFilename);

			PgoProblem p = new PgoProblem(forecasts, flowProvider, localFileNameBase);
			return p;
		}

		/// <summary>
		/// Parses a <see cref="PowerNetwork"/> from the given stream of JSON data.
		///  Throws an exception if cycles or inter-sub-station paths were found that cannot
		///  be removed by opening switches (i.e. the network can never be radial).
		/// </summary>
		/// <param name="stream">A stream containing the data to parse.</param>
		/// <returns>The PowerNetwork described by the file.</returns>
		public static PowerNetwork ParseNetworkFromJson(Stream stream)
		{
			return ParseNetworkJSON(new StreamReader(stream, System.Text.Encoding.UTF8, true, 1024, true).ReadToEnd());
		}

		/// <summary>
		/// Parses the given json data
		/// </summary>
		/// <param name="data">The JSON data to parse</param>
		/// <param name="modifyAction">If not null, this action is applied the the parsed power
		///   grid before it's turned into a <see cref="PowerNetwork"/></param>
		public static PowerNetwork ParseNetworkJSON(string data, Action<PowerGrid> modifyAction = null)
		{
			JsonSerializerSettings serializerSettings = new JsonSerializerSettings();
			serializerSettings.DefaultValueHandling = DefaultValueHandling.Populate;
			var networkInFile = JsonConvert.DeserializeObject<PowerGrid>(data, serializerSettings);

			modifyAction?.Invoke(networkInFile);

			return ParseNetwork(networkInFile);
		}

		/// <summary>
		/// Parses the given network from the IO format to the internal power network class.
		/// </summary>
		public static PowerNetwork ParseNetwork(PowerGrid inputNetwork)
		{
			if (inputNetwork.Nodes == null)
				throw new ArgumentException("The node list is null");

			if (inputNetwork.Lines == null)
				throw new ArgumentException("The line list is null");

			if (inputNetwork.SchemaVersion != 1)
				throw new ArgumentException("SchemaVersion (json: @schema_version) must be set to 1.");

			PowerNetwork network = new PowerNetwork(name: inputNetwork.Name);

			// First add the nodes
			foreach (Node node in inputNetwork.Nodes)
			{
				CheckNodeForErrors(node, network);

				// Find voltage limits -- the file is given in kV
				double vMin = 1e3 * node.MinimumVoltage;
				double vMax = 1e3 * node.MaximumVoltage;

				// Set up location if it is given
				Coordinate location = null;
				if (node.Location != null)
				{
					// Must be exactly two elements
					if (node.Location.Count != 2)
						throw new Exception($"Node '{node.Id}': The coordinates must contain exactly two values");
					location = new Coordinate(node.Location[0], node.Location[1]);
				}

				switch (node.Type)
				{
					case NodeType.Connection:
						network.AddTransition(vMin, vMax, name: node.Id, location: location);
						break;
					case NodeType.PowerProvider:
						// The power values are all in MW, so we convert
						double pGenMax = 1e6 * node.MaximumActiveGeneration;
						double pGenMin = 1e6 * node.MinimumActiveGeneration;
						double qGenMax = 1e6 * node.MaximumReactiveGeneration;
						double qGenMin = 1e6 * node.MinimumReactiveGeneration;

						network.AddProvider(1e3 * node.GeneratorVoltage, // This is in kV
							new Complex(pGenMax, qGenMax),
							new Complex(pGenMin, qGenMin),
							name: node.Id,
							location: location);
						break;
					case NodeType.PowerConsumer:
						Bus bus = network.AddConsumer(vMin, vMax, name: node.Id, location: location);

						if (node.ConsumerType != API.ConsumerCategory.Undefined)
						{
							var category = MapConsumerType(node.ConsumerType);
							network.CategoryProvider.Set(bus, category);
						}
						else if (node.ConsumerTypeFractions != null)
						{
							foreach (ConsumerTypeFracton type in node.ConsumerTypeFractions)
							{
								var cat = MapConsumerType(type.ConsumerType);
								network.CategoryProvider.Set(bus, cat, type.PowerFraction);
							}
						}
						break;
					default:
						throw new Exception($"Unsupported node type {node.Type}");

				}
			}

			// Now add the lines
			foreach (API.Line line in inputNetwork.Lines)
			{
				CheckLineForErrors(line, network);

				string sourceId = line.SourceNode;
				string targetId = line.TargetNode;
				Complex impedance = new Complex(line.Resistance, line.Reactance); //These are ohm in the data
				double imax = line.CurrentLimit; //This is A in the data
				double vmax = 1.0e3 * line.VoltageLimit;
				bool switchable = line.IsSwitchable;
				double faultFrequency = line.FaultsPerYear ?? 0.0;
				TimeSpan sectioningTime = line.SectioningTime ?? TimeSpan.Zero;
				TimeSpan repairTime = line.RepairTime ?? TimeSpan.Zero;

				Core.Line addedLine;
				if (switchable) // If it is switchable add the cost of switching -- if not given, 0 is used
				{
					// TODO clean up use of associated switches. We're just shoving in the line name here because it will work with the current output format
					addedLine = network.AddLine(sourceId, targetId, impedance, imax, vmax, isBreaker: line.IsBreaker, switchable: switchable, switchingCost: line.SwitchingCost ?? 0, name: line.Id);
				}
				else
				{
					addedLine = network.AddLine(sourceId, targetId, impedance, imax, vmax, isBreaker: line.IsBreaker, switchable: switchable, name: line.Id);
				}

				// Add the fault information to the PropertiesProvider if the line is not a breaker
				if (!line.IsBreaker)
					network.PropertiesProvider.Add(addedLine, faultFrequency, sectioningTime, repairTime);
			}

			// Then add transformers
			if (inputNetwork.Transformers != null) //Robostness for backward compatibility
			{
				foreach (API.Transformer transformer in inputNetwork.Transformers)
				{
					CheckTransformerForError(transformer, network);

					IEnumerable<(string, double)> connections = transformer.Connections.Select(c => (c.NodeId, c.Voltage * 1000)); // In the format kV is given

					IEnumerable<TransformerModeData> modes =
						transformer.Modes.Select(t => new TransformerModeData(t.Source, t.Target, MapEnum<TransformerOperationType>(t.Operation), t.Ratio, t.PowerFactor, t.Bidirectional));

					Coordinate location = null;
					if (transformer.CenterLocation != null)
					{
						// Must be exactly two elements
						if (transformer.CenterLocation.Count != 2)
							throw new Exception($"Transformer '{transformer.Id}': The coordinates must contain exactly two values");
						location = new Coordinate(transformer.CenterLocation[0], transformer.CenterLocation[1]);
					}

					var addedTrans = network.AddTransformer(connections, modes, transformer.Id, location);
				}
			}

			bool usesKile = inputNetwork.Lines.Any(l => l.FaultsPerYear > 0);
			if (usesKile)
			{
				var badNode = inputNetwork.Nodes.FirstOrDefault(n => n.Type == NodeType.PowerConsumer && n.ConsumerType == API.ConsumerCategory.Undefined && n.ConsumerTypeFractions == null);
				if (badNode != null)
					throw new ArgumentException($"Node '{badNode.Id}': A consumer type or types must be specified, because a fault frequency is given for one or more lines");
			}

			return network;
		}

		/// <summary>
		/// Throws an exception if there is a problem in the node's definition
		/// </summary>
		/// <param name="node">The node to check</param>
		/// <param name="network">The network the node will be added to</param>
		private static void CheckNodeForErrors(Node node, PowerNetwork network)
		{
			if (node == null)
				throw new ArgumentException("The node list contains null");
			if (node.Id == null)
				throw new ArgumentException("The node ID may not be null");
			if (node.Id == "")
				throw new ArgumentException("The node ID may not be an empty string");
			if (network.HasBus(node.Id))
				throw new ArgumentException($"The ID '{node.Id}' is used by more than one node");

			void Fail(string msg) => throw new ArgumentException($"Node '{node.Id}': {msg}");
			string type = JsonName(node.Type);

			if (node.Type == NodeType.PowerConsumer)
			{
				var fractions = node.ConsumerTypeFractions;

				if (node.ConsumerType != API.ConsumerCategory.Undefined && fractions != null)
					Fail($"Only one of ConsumerType and ConsumerTypeFractions may be given.");

				if (fractions != null)
				{
					if (fractions.GroupBy(f => f.ConsumerType).Where(g => g.Count() > 1).FirstOrDefault()?.Key is API.ConsumerCategory duplicate)
						Fail($"A consumption fraction for consumer type {duplicate} is given more than once");
					if (fractions.Any(f => f.ConsumerType == API.ConsumerCategory.Undefined))
						Fail($"Consumer type must be specified for each consumer type fraction");
					if (fractions.Any(f => f.PowerFraction <= 0))
						Fail($"Each fraction of consumer categories must be positive");
					double sum = fractions.Sum(f => f.PowerFraction);
					if (!sum.EqualsWithTolerance(1, 1e-9))
						Fail($"Fractions of consumer categories sums to {sum} (should be 1)");
				}
			}
			else
			{
				if (node.ConsumerType != API.ConsumerCategory.Undefined || node.ConsumerTypeFractions != null)
					Fail($"A {type} node may not specify consumer type(s)");
			}

			if (node.Type == NodeType.PowerProvider)
			{
				if (node.GeneratorVoltage <= 0)
					Fail($"The generator voltage must be positive");
				if (node.MinimumVoltage != 0)
					Fail($"A {type} node may not specify minimum voltage");
				if (node.MaximumVoltage != double.PositiveInfinity)
					Fail($"A {type} node may not specify maximum voltage");
				if (node.MinimumActiveGeneration > node.MaximumActiveGeneration)
					Fail($"The maximum active generation must be larger than the minimum active generation");
				if (node.MinimumActiveGeneration < 0)
					Fail($"The minimum active generation may not be negative");
				if (node.MinimumReactiveGeneration > node.MaximumReactiveGeneration)
					Fail($"The maximum reactive generation must be larger than the minimum reactive generation");
			}
			else
			{
				if (node.GeneratorVoltage != 0)
					Fail($"A {type} node may not specify a generator voltage");
				if (node.MinimumVoltage > node.MaximumVoltage)
					Fail($"The maximum voltage must be larger than the minimum voltage");
				if (node.MinimumActiveGeneration != 0)
					Fail($"A {type} node may not specify a minimum active generation");
				if (node.MaximumActiveGeneration != double.PositiveInfinity)
					Fail($"A {type} node may not specify a maximum active generation");
				if (node.MinimumReactiveGeneration != 0)
					Fail($"A {type} node may not specify a minimum reactive generation");
				if (node.MaximumReactiveGeneration != double.PositiveInfinity)
					Fail($"A {type} node may not specify a maximum reactive generation");
			}
		}

		/// <summary>
		/// Throws an exception if there is a problem in the line's definition
		/// </summary>
		/// <param name="line">The line to check</param>
		/// <param name="network">The network the line will be added to</param>
		private static void CheckLineForErrors(API.Line line, PowerNetwork network)
		{
			if (line == null)
				throw new ArgumentException("The line list contains null");
			if (line.Id == null)
				throw new ArgumentException("The line ID may not be null");
			if (line.Id == "")
				throw new ArgumentException("The line ID may not be an empty string");
			if (network.TryGetLine(line.Id, out _))
				throw new ArgumentException($"The ID '{line.Id}' is used by more than one line");

			void Fail(string msg) => throw new ArgumentException($"Line '{line.Id}': {msg}");

			if (line.SourceNode == null)
				Fail($"The source node ID may not be null");
			if (line.TargetNode == null)
				Fail($"The target node ID may not be null");
			if (!network.HasBus(line.SourceNode))
				Fail($"There is no node with ID '{line.SourceNode}' (used as the line source)");
			if (!network.HasBus(line.TargetNode))
				Fail($"There is no node with ID '{line.TargetNode}' (used as the line target)");

			if (line.Resistance < 0)
				Fail("The resistance may not be negative");
			if (line.CurrentLimit < 0)
				Fail("The maximum current limit may not be negative");
			if (line.VoltageLimit <= 0)
				Fail("The maximum voltage limit must be positive");

			if (line.FaultsPerYear < 0)
				Fail("The fault frequency may not be negative");
			if (line.SectioningTime?.Ticks < 0)
				Fail("The sectioning time may not be negative");
			if (line.RepairTime?.Ticks < 0)
				Fail("The repair time may not be negative");
		}

		/// <summary>
		/// Throws an exception if there is a problem in the transformer's definition
		/// </summary>
		/// <param name="transformer">The transformer to check</param>
		/// <param name="network">The network the transformer will be added to</param>
		private static void CheckTransformerForError(API.Transformer transformer, PowerNetwork network)
		{
			if (transformer == null)
				throw new ArgumentException("The transformer list contains null");
			if (transformer.Id == null)
				throw new ArgumentException("The transformer ID may not be null");
			if (transformer.Id == "")
				throw new ArgumentException("The transformer ID may not be an empty string");
			if (network.PowerTransformers.Any(t => t.Name == transformer.Id))
				throw new ArgumentException($"The ID '{transformer.Id}' is used by more than one transformer");

			void Fail(string msg) => throw new Exception($"Transformer '{transformer.Id}': {msg}");
			var connections = transformer.Connections;
			var modes = transformer.Modes;

			if (connections == null)
				Fail("The list of connections is null");
			if (connections.Count == 0)
				Fail("No connections are defined");
			if (connections.Count != 2 && connections.Count != 3)
				Fail($"{connections.Count} connections is not allowed. Only transformers with two or three connections are supported");


			foreach (var connection in connections)
			{
				if (connection == null)
					Fail("The list of connections contains null");
				if (connection.NodeId == null)
					Fail($"A connection node ID is null");
				if (!network.HasBus(connection.NodeId))
					Fail($"There is no node with ID '{connection.NodeId}' (used as a connection node)");
				if (connection.Voltage <= 0)
					Fail("The voltage at each connection must be positive");
			}

			if (connections.TryFindDuplicateBy(c => c.NodeId, out var c))
				Fail($"The node '{c.NodeId}' is used in more than one connection");

			if (modes == null)
				Fail("The list of modes is null");
			if (modes.Count == 0)
				Fail("No modes are defined");

			foreach (var mode in modes)
			{
				if (mode == null)
					Fail("The list of modes contains null");
				if (mode.Source == null)
					Fail($"The source node ID of a mode is null");
				if (mode.Target == null)
					Fail($"The target node ID of a mode is null");
				if (!network.HasBus(mode.Source))
					Fail($"There is no node with ID '{mode.Source}' (used as a mode's source node)");
				if (!network.HasBus(mode.Target))
					Fail($"There is no node with ID '{mode.Target}' (used as a mode's target node)");
				if (mode.Source == mode.Target)
					Fail($"Illegal mode has node '{mode.Target}' both as source and target");

				if (mode.Operation == TransformerOperation.Automatic && mode.Ratio != null)
					Fail($"A voltage ratio may not be given for a mode with automatic operation");
				if (mode.Operation == TransformerOperation.FixedRatio)
				{
					if (mode.Ratio == null)
						Fail($"A voltage ratio is required for a mode with fixed ratio operation");
					if (mode.Ratio <= 0)
						Fail($"A mode's voltage ratio must be positive");
				}

				if (mode.PowerFactor <= 0)
					Fail($"A mode's power factor must be positive");
			}

			// Check that max one mode is defined between each pair of connections
			if (modes.TryFindDuplicateBy(m => (m.Source, m.Target), out var m))
				Fail($"More than one mode is defined with source '{m.Source}' and target '{m.Target}'");


			// At least one input bus should be a valid input bus, i.e. there are modes available for all outputs buses.
			// (this holds trivially for 2-winding transformers with n_modes > 0)
			var modesByInput = modes
				.Select(m => (m.Source, m))
				.Concat(modes
					.Where(m => m.Bidirectional)
					.Select(m => (m.Target, m)))
				.GroupBy(pair => pair.Item1);

			if (!modesByInput.Any(modes => modes.Count() >= transformer.Connections.Count - 1))
				Fail("A transformer must have at least one input terminal for which there are modes outputting to each of the other terminals");

		}

		/// <summary>
		/// Returns the name used in Json for the given enum value
		/// </summary>
		private static string JsonName(object enumValue)
		{
			string enumMemberName = enumValue.ToString();
			System.Reflection.MemberInfo enumMember = enumValue.GetType().GetMember(enumMemberName).Single();
			EnumMemberAttribute attribute = enumMember.GetCustomAttributes(typeof(EnumMemberAttribute), false)
				.Cast<EnumMemberAttribute>().Single();
			return attribute.Value;
		}

		/// <summary>
		/// Converts <paramref name="enumValue"/> (which is assumed to be an enumeration value)
		/// to the value of enumerable type <typeparamref name="TOut"/> with the same name.
		/// Fails if there is no such value.
		/// </summary>
		private static TOut MapEnum<TOut>(object enumValue)
		{
			return (TOut)Enum.Parse(typeof(TOut), enumValue.ToString());
		}

		/// <summary>
		/// Parses the demands from the Json stream for the given network.
		/// </summary>
		public static List<PeriodData> ParseDemandsFromJson(PowerNetwork network, Stream stream, bool allowUnspecifiedConsumerDemands)
		{
			return ParseDemandsfromJson(network, new StreamReader(stream, System.Text.Encoding.UTF8, true, 1024, true).ReadToEnd(),
				allowUnspecifiedConsumerDemands);
		}

		/// <summary>
		/// Parses the demands from the JSON file at the given location for the given network.
		/// </summary>
		/// <param name="network"></param>
		/// <param name="inputfile">The name of the file to parse</param>
		public static List<PeriodData> ParseDemandsFromJsonFile(PowerNetwork network, string inputfile)
		{
			// This is a bad and slow implementation
			return ParseDemandsfromJson(network, File.ReadAllText(inputfile));
		}

		/// <summary>
		/// Parses the demands into Json contracts and builds the related Power Demands object.
		/// </summary>
		/// <param name="network"></param>
		/// <param name="demandsAsJson"></param>
		/// <param name="allowUnspecifiedConsumerDemands">If true and a demand is not given for 
		///   a bus, we assume the demand is zero. However, if demands are given, they must be given for each period.
		///   If false, demands must be given for all consumers.
		/// </param>
		/// <returns></returns>
		public static List<PeriodData> ParseDemandsfromJson(PowerNetwork network, string demandsAsJson, bool allowUnspecifiedConsumerDemands = false)
		{
			var demands = JsonConvert.DeserializeObject<Demand>(demandsAsJson, new JsonSerializerSettings() { FloatParseHandling = FloatParseHandling.Decimal });

			return ParseDemands(network, demands, allowUnspecifiedConsumerDemands);
		}

		/// <summary>
		/// Converts external-format demands to PeriodData
		/// </summary>
		/// <param name="network">The network the demands are for</param>
		/// <param name="demands">The demands to parse</param>
		/// <param name="allowUnspecifiedConsumerDemands">If true and a demand is not given for 
		///   a bus, we assume the demand is zero. However, if demands are given, they must be given for each period.
		///   If false, demands must be given for all consumers.
		/// </param>
		/// <returns></returns>
		public static List<PeriodData> ParseDemands(PowerNetwork network, Demand demands, bool allowUnspecifiedConsumerDemands = false)
		{
			void Fail(string message) => throw new ArgumentException(message);

			if (demands.SchemaVersion != 1)
			{
				Fail("SchemaVersion (json: @schema_version) must be set to 1.");
			}

			// Verify and parse periods

			List<Core.Period> periods = ParsePeriods(demands.Periods);

			// Verify input: demand data

			if (demands.Loads == null)
				Fail($"The loads list is null");

			foreach (var load in demands.Loads)
			{
				if (load == null)
					Fail($"The loads list contains null");
				if (load.NodeId == null)
					Fail($"Loads are given for node ID null");
				if (!network.HasBus(load.NodeId))
					Fail($"Loads are given for unknown node '{load.NodeId}'");
				if (!network.GetBus(load.NodeId).IsConsumer)
					Fail($"Loads are given for node '{load.NodeId}' that is not a consumer");
				if (load.ActiveLoads == null)
					Fail($"Node '{load.NodeId}': The active loads is null");
				if (load.ActiveLoads.Count != demands.Periods.Count)
					Fail($"Node '{load.NodeId}': The number of active loads does not equal the number of periods");
				if (load.ReactiveLoads == null)
					Fail($"Node '{load.NodeId}': The reactive loads is null");
				if (load.ReactiveLoads.Count != demands.Periods.Count)
					Fail($"Node '{load.NodeId}': The number of reactive loads does not equal the number of periods");
				if (load.ActiveLoads.Any(l => l < 0))
					Fail($"Node '{load.NodeId}': Active loads may not be negative");
			}

			if (demands.Loads.Select(l => l.NodeId).TryFindDuplicate(out string id))
				Fail($"Loads are given more than once for node '{id}'");

			if (!allowUnspecifiedConsumerDemands)
			{
				// Check for unspecified buses
				var missingConsumers = network.Consumers.Select(b => b.Name)
					.Except(demands.Loads.Select(l => l.NodeId))
					.ToList();

				if (missingConsumers.Count > 0)
					Fail($"Loads are missing for the following consumers: {missingConsumers.ListSome()}");
			}

			// Convert data to internal structure

			var busIdToActiveDemands = demands.Loads.ToDictionary(l => l.NodeId, l => l.ActiveLoads);
			var busIdToReactiveDemands = demands.Loads.ToDictionary(l => l.NodeId, l => l.ReactiveLoads);

			var periodData = new List<PeriodData>();
			int n = demands.Periods.Count;
			for (int periodIndex = 0; periodIndex < n; ++periodIndex)
			{
				PowerDemands periodDemands = new PowerDemands(network);

				foreach (Bus bus in network.Consumers)
				{
					double activeDemandInW = 0;
					double reactiveDemandInW = 0;

					if (busIdToActiveDemands.TryGetValue(bus.Name, out var activeDemands))
						activeDemandInW = 1e6 * activeDemands[periodIndex];

					if (busIdToReactiveDemands.TryGetValue(bus.Name, out var reactiveDemands))
						reactiveDemandInW = 1e6 * reactiveDemands[periodIndex];

					periodDemands.SetPowerDemand(bus, new Complex(activeDemandInW, reactiveDemandInW));
				}

				periodData.Add(new PeriodData(network, periodDemands, periods[periodIndex]));
			}

			return periodData;
		}

		/// <summary>
		/// Converts external-format periods to internal periods.
		/// </summary>
		public static List<Core.Period> ParsePeriods(List<API.Period> periods)
		{
			void Fail(string message) => throw new ArgumentException(message);

			// Verify period data

			if (periods is null)
				Fail("The period list is null");
			if (periods.Count == 0)
				Fail("No demand periods were specified");
			if (periods.Contains(null))
				Fail("The list contains a null period");

			int index = 0;
			foreach (var period in periods)
			{
				if (string.IsNullOrEmpty(period.Id))
					Fail($"A period ID (at index {index}) is null or empty");

				if (period.EndTime <= period.StartTime)
					Fail($"Period '{period.Id}' (at index {index}) does not have a positive duration ({period.StartTime}--{period.EndTime})");

				++index;
			}

			if (periods.TryFindDuplicateBy(p => p.Id, out var p))
				Fail($"The ID '{p.Id}' is used for more than one period");

			var badPair = periods.AdjacentPairs().FirstOrDefault(pair => pair.Item1.EndTime != pair.Item2.StartTime);
			if (badPair != null)
				Fail($"Period '{badPair.Item2.Id}' does not start at the same time as period '{badPair.Item1.Id}' ends");

			// Convert periods

			return periods.Select((period, index) => 
					new Core.Period(period.StartTime, period.EndTime, index, period.Id))
				.ToList();
		}

		/// <summary>
		/// Write demands to JSON.
		/// </summary>
		/// <param name="allPeriodData"></param>
		/// <param name="stream"></param>
		/// <param name="prettify"></param>
		public static void WriteDemands(IEnumerable<PeriodData> allPeriodData, Stream stream, bool prettify = false)
		{
			Demand contract = ConvertToJson(allPeriodData);
			Serialize(contract, stream, prettify);
		}

		/// <summary>
		/// NOTE: This is discontinued. Keeping it for the time being in case we need to suport some legacy use.
		/// Parses a start configuration from the given stream. As a temporary solution, this is done with CSV.
		/// Assumes that "enabled = 1" on the csv file means that the switch is closed. CSV format:
		/// NAME; ENABLED; GLOBALID
		/// The last column is ignored
		/// </summary>
		/// <param name="network"></param>
		/// <param name="currentConfigurationStream"></param>
		/// <returns></returns>
		public static NetworkConfiguration ParseStartConfigurationFromCSV(PowerNetwork network, Stream currentConfigurationStream)
		{
			SwitchSettings switchSettings = new SwitchSettings(network);
			using (var reader = new StreamReader(currentConfigurationStream))
			{
				var header = reader.ReadLine(); // Header with sub station names/id's.
				while (!reader.EndOfStream)
				{
					var csvLine = reader.ReadLine();
					var split = csvLine.Split(';');

					if (split.Count() < 2)
						throw new Exception($"Bad configuration line; expected at least two fields: {csvLine}");

					bool enabled = Convert.ToBoolean(Convert.ToInt16(split[1]));
					string identifier = split[0];
					Core.Line swLine = network.GetLine(identifier);

					if (!swLine.IsSwitchable)
						throw new Exception($"Tried to set switch for non-switchable line {swLine}. Offending data line: {csvLine}");

					if (swLine != null)
						switchSettings.SetSwitch(swLine, !enabled);
				}
			}

			NetworkConfiguration config = new NetworkConfiguration(network, switchSettings);

			return config;
		}

		/// <summary>
		/// Parses a network configuration from the JSON string, from a multi-period configuration with only one period.
		/// </summary>
		/// <param name="network"></param>
		/// <param name="startConfigurationStream"></param>
		/// <returns></returns>
		public static NetworkConfiguration ParseConfigurationFromStream(PowerNetwork network, Stream startConfigurationStream)
		{
			StreamReader reader = new StreamReader(startConfigurationStream);
			string configuration = reader.ReadToEnd();

			var jsonConfiguration = Deserialize<SinglePeriodSettings>(configuration);
			if (jsonConfiguration == null)
				return null;

			return ParseConfiguration(network, jsonConfiguration);
		}

		/// <summary>
		/// Parses a solution info from the given JSON file.
		/// </summary>
		/// <param name="filename">File name</param>
		/// <returns></returns>
		public static SolutionInfo ParseSolutionInfoFromFile(string filename)
		{
			SolutionInfo result = null;
			using (FileStream stream = new FileStream(filename, FileMode.Open))
			{
				result = ParseSolutionInfoFromStream(stream);
			}
			return result;
		}

		/// <summary>
		///  Parses a solution info from the given JSON stream.
		/// </summary>
		/// <param name="stream">the json stream to parse.</param>
		/// <returns></returns>
		public static SolutionInfo ParseSolutionInfoFromStream(Stream stream)
		{
			StreamReader reader = new StreamReader(stream);
			string configuration = reader.ReadToEnd();
			SolutionInfo solInfo = Deserialize<SolutionInfo>(configuration);
			return solInfo;
		}

		/// <summary>
		/// Parses a single-period network configuration from the JSON Solution string, from a multi-period configuration with only one period.
		/// </summary>
		/// <param name="network"></param>
		/// <param name="jsonSolutionString"></param>
		/// <returns>A network configuration per period.</returns>
		public static NetworkConfiguration ParseSingleConfigurationFromSolution(PowerNetwork network, string jsonSolutionString)
		{
			var jsonSolution = Deserialize<Solution>(jsonSolutionString);

			return ParseConfiguration(network, jsonSolution.PeriodSettings.Single());
		}

		/// <summary>
		/// Parses an external <see cref="Solution"/> into an <see cref="PgoSolution"/>
		/// </summary>
		/// <param name="problem">The problem the solution is for</param>
		/// <param name="solution">The external solution to parse</param>
		public static PgoSolution ParseSolution(PgoProblem problem, Solution solution)
		{
			var periodConfigurations = ParseConfigurations(problem, solution);

			return new PgoSolution(problem, periodConfigurations);
		}

		/// <summary>
		/// Parses a Json solution from a Json string into an <see cref="PgoSolution"/>
		/// </summary>
		/// <param name="problem">The problem the solution is for</param>
		/// <param name="json">The solution as a Json string</param>
		private static PgoSolution ParseSolution(PgoProblem problem, string json)
		{
			var solution = JsonConvert.DeserializeObject<Solution>(json, new JsonSerializerSettings());

			return ParseSolution(problem, solution);
		}

		/// <summary>
		/// Parses a Json solution from a stream into an <see cref="PgoSolution"/>
		/// </summary>
		/// <param name="problem">The problem the solution is for</param>
		/// <param name="stream">The stream to read from</param>
		public static PgoSolution ParseSolution(PgoProblem problem, Stream stream)
		{
			string json = new StreamReader(stream, System.Text.Encoding.UTF8, true, 1024, true).ReadToEnd();

			return ParseSolution(problem, json);
		}

		/// <summary>
		/// Parses a multi-period network configuration from the solution.
		/// The set of periods should cover that of the given <see cref="PgoProblem"/>.
		/// </summary>
		private static Dictionary<Core.Period, NetworkConfiguration> ParseConfigurations(PgoProblem problem, Solution jsonSolution)
		{
			// Verify input

			void Fail(string msg) => throw new ArgumentException(msg);

			if (jsonSolution.PeriodSettings.Contains(null))
				Fail("The list of period solutions contains null");

			if (jsonSolution.PeriodSettings.Any(p => p.Period == null))
				Fail("The period ID is missing for a period solution");

			var periodIds = jsonSolution.PeriodSettings.Select(x => x.Period).ToList();
			var knownPeriods = problem.Periods.Select(p => p.Id).ToList();

			var unknownPeriods = periodIds.Except(knownPeriods).ToList();

			if (unknownPeriods.Any())
				Fail($"The solution contains unknown periods: {unknownPeriods.ListSome()}");

			if (periodIds.TryFindDuplicate(out var duplicate))
				Fail($"The solution contains period '{duplicate}' more than once");

			var missingPeriods = knownPeriods.Except(periodIds).ToList();

			if (missingPeriods.Any())
				Fail($"The solution does not cover some periods: {missingPeriods.ListSome()}");

			if (periodIds.TryFindDuplicate(out string duplicatePeriod))
				Fail($"The solution contains period '{duplicatePeriod}' more than once");

			// Convert input

			Dictionary<Core.Period, NetworkConfiguration> result = new Dictionary<Core.Period, NetworkConfiguration>();

			foreach (var networkConfiguration in jsonSolution.PeriodSettings)
			{
				Core.Period period = problem.GetPeriodById(networkConfiguration.Period);
				result[period] = ParseConfiguration(problem.Network, networkConfiguration, $"Period '{period.Id}': ");
			}

			return result;
		}

		/// <summary>
		/// Converts a <see cref="SinglePeriodSettings"/> to a <see cref="NetworkConfiguration"/>.
		/// </summary>
		/// <param name="network"></param>
		/// <param name="settings"></param>
		/// <param name="errorPrefix">A string added to the start of any exception message</param>
		/// <returns>The NetworkConfiguration.</returns>
		public static NetworkConfiguration ParseConfiguration(PowerNetwork network, SinglePeriodSettings settings, string errorPrefix = "")
		{
			// Verify input

			void Fail(string msg) => throw new ArgumentException($"{errorPrefix}{msg}");

			if (settings.SwitchSettings is null)
				Fail("The list of switch settings is null");
			if (settings.SwitchSettings.Contains(null))
				Fail("The list of switch settings contains null");
			if (settings.SwitchSettings.Any(s => s.Id == null))
				Fail("Settings are given for line ID null");

			var unknownSwitches = settings.SwitchSettings.Select(s => s.Id)
				.Except(network.Lines.Select(l => l.Name));

			if (unknownSwitches.Any())
				Fail($"Settings are given for unknown line IDs: {unknownSwitches.ListSome()}");

			var notSwitches = settings.SwitchSettings.Select(s => s.Id)
				.Except(network.SwitchableLines.Select(l => l.Name));

			if (notSwitches.Any())
				Fail($"Settings are given for lines that are not switchable: {notSwitches.ListSome()}");

			var missingSwitches = network.SwitchableLines.Select(l => l.Name)
				.Except(settings.SwitchSettings.Select(s => s.Id));

			if (missingSwitches.Any())
				Fail($"Settings are missing for one or more switchable lines: {missingSwitches.ListSome()}");

			if (settings.SwitchSettings.Select(s => s.Id).TryFindDuplicate(out string duplicateId))
				Fail($"Setting is given more than once for line '{duplicateId}'");

			// Convert data

			SwitchSettings switchSettings = new SwitchSettings(network);
			foreach (SwitchState state in settings.SwitchSettings)
			{
				string id = state.Id;
				bool open = state.Open;

				Core.Line swLine = network.GetLine(id);
				switchSettings.SetSwitch(swLine, open);
			}

			return new NetworkConfiguration(network, switchSettings);
		}

		/// <summary>
		/// Parses a multi-period network configuration from the JSON stream. 
		/// The set of periods should cover that of the given <see cref="PgoProblem"/>.
		/// </summary>
		/// <param name="problem"></param>
		/// <param name="JSONConfigurationSettings"></param>
		/// <returns>A network configuration per period</returns>
		public static Dictionary<Core.Period, NetworkConfiguration> ParseMultiPeriodJSONConfiguration(PgoProblem problem, Stream JSONConfigurationSettings)
		{
			StreamReader reader = new StreamReader(JSONConfigurationSettings);
			string configuration = reader.ReadToEnd();
			var jsonSolution = Deserialize<Solution>(configuration);

			return ParseConfigurations(problem, jsonSolution);
		}

		/// <summary>
		/// Write a multi-period solution (i.e. its configurations) to JSON.
		/// If a flow provider is supplied, its flows are also written.
		/// </summary>
		/// <param name="solution">The solution</param>
		/// <param name="stream"></param>
		/// <param name="flowProvider">The flow provider to report flows for. If null,
		///   no flows are reported</param>
		/// <param name="prettify"></param>
		public static void WriteJSONConfigurations(PgoSolution solution, Stream stream, IFlowProvider flowProvider = null, bool prettify = false)
		{
			Solution jsonSolution = ConvertToJson(solution, flowProvider);
			Serialize(jsonSolution, stream, prettify);
		}

		/// <summary>
		/// Returns a JSON-formatted string representing the given solution.
		/// </summary>
		/// <param name="solution">The solution to convert</param>
		/// <param name="flowProvider">The flow provider to report flows for. If null,
		///   no flows are reported</param>
		/// <param name="prettify">Whether to pretty-print the json</param>
		public static string ConvertToJsonString(PgoSolution solution, IFlowProvider flowProvider, bool prettify)
		{
			Solution jsonSolution = ConvertToJson(solution, flowProvider);
			return Serialize(jsonSolution, prettify);
		}

		private static API.FlowStatus FlowStatusContractFromFlow(Core.FlowStatus s)
		{
			return s switch
			{
				Core.FlowStatus.None => throw new Exception("FlowStatus.None is not a valid status to return through the API"),
				Core.FlowStatus.Failed => API.FlowStatus.Failed,
				Core.FlowStatus.Approximate => API.FlowStatus.Approximate,
				Core.FlowStatus.Exact => API.FlowStatus.Exact,
				_ => throw new ArgumentException()
			};
		}

		private static Core.FlowStatus FlowStatusFromFlowContract(API.FlowStatus s)
		{
			return s switch
			{
				API.FlowStatus.Failed => Core.FlowStatus.Failed,
				API.FlowStatus.Approximate => Core.FlowStatus.Approximate,
				API.FlowStatus.Exact => Core.FlowStatus.Exact,
				_ => throw new ArgumentException()
			};
		}

		/// <summary>
		/// Creates a power flow representation from the data contract (JSON) representation.
		/// </summary>
		/// <param name="period"></param>
		/// <param name="flowContract"></param>
		/// <returns></returns>
		public static IPowerFlow ParseFlow(PeriodSolution period, PowerFlow flowContract)
		{
			var flow = new ExternalFlow
			{
				Status = FlowStatusFromFlowContract(flowContract.Status),
				StatusDetails = flowContract.StatusDetails,
				NetworkConfig = period.NetConfig,
				Demands = period.PeriodData.Demands,
			};

			foreach (var kv in flowContract.Voltages)
			{
				var bus = period.Network.GetBus(kv.Key);
				flow.Voltages[bus] = 1e3 * kv.Value;
			}

			foreach (var kv in flowContract.Currents)
			{
				var bus1 = period.Network.GetBus(kv.Key);
				foreach (var outgoingCurrent in kv.Value)
				{
					var bus2 = period.Network.GetBus(outgoingCurrent.Target);
					var line = period.Network.GetLine(outgoingCurrent.LineId);
					Debug.Assert((line.Node1 == bus1 && line.Node2 == bus2) || (line.Node1 == bus2 && line.Node2 == bus1));

					var positiveDirection = line.Node1 == bus1;
					var factor = positiveDirection ? 1 : -1;
					flow.Currents[line] = 1e3 * factor * outgoingCurrent.Current;
				}
			}

			foreach (var kv in flowContract.Powers)
			{
				var bus1 = period.Network.GetBus(kv.Key);
				foreach (var outgoingPower in kv.Value)
				{
					var bus2 = period.Network.GetBus(outgoingPower.Target);
					var line = period.Network.GetLine(outgoingPower.LineId);
					Debug.Assert((line.Node1 == bus1 && line.Node2 == bus2) || (line.Node1 == bus2 && line.Node2 == bus1));

					var value = new Complex(outgoingPower.ActivePower, outgoingPower.ReactivePower);
					flow.PowerFlows[(bus1, line)] = 1e6 * value;
					flow.PowerFlows[(bus2, line)] = 1e6 * -value;
				}

				// Note that the OutgoingPower object also contains the power loss, but we don't need to
				// store the power loss in an IPowerFlow object since it can be calculated from the 
				// current and resistance. So we ignore this value when parsing.
			}

			return flow;
		}

		/// <summary>
		/// Creates a single period flows contract based on the given flow provider for the given period solution.
		/// Returns null if the flow cannot be computed.
		/// </summary>
		/// <param name="flowProvider"></param>
		/// <param name="periodSolution"></param>
		/// <returns></returns>
		private static PowerFlow CreateSinglePeriodFlowsContract(IFlowProvider flowProvider, PeriodSolution periodSolution)
		{
			var computedFlow = periodSolution.Flow(flowProvider);
			if (computedFlow == null)
				return null;

			var powerFlow = new PowerFlow(periodSolution.Period.Id)
			{
				Status = FlowStatusContractFromFlow(computedFlow.Status),
				StatusDetails = computedFlow.StatusDetails,
			};

			foreach (var bus in computedFlow.NetworkConfig.Network.Buses.Where(b => !b.IsTransformer))
			{
				powerFlow.Voltages[bus.Name] = computedFlow.Voltage(bus).Magnitude / 1e3; //Externally the API uses kV
			}

			foreach (var line in periodSolution.PresentLines)
			{
				var (upstream, downstream) = (line.Node1, line.Node2);

				// Compute power and current
				var power = computedFlow.PowerFlow(upstream, line);
				var current = computedFlow.Current(line);
				var loss = computedFlow.PowerLoss(line);

				// Prefer presenting the line in upstream->downstream direction,
				if (periodSolution.NetConfig.UpstreamBus(upstream) == downstream)
				{
					(upstream, downstream) = (downstream, upstream);
					power = -power;
					current = -current;
				}

				// but most important: Choose upstream node to get positive real power
				if (power.Real < 0)
				{
					(upstream, downstream) = (downstream, upstream);
					power = -power;
					current = -current;
				}

				powerFlow.Currents.AddOrNew(upstream.Name, new OutgoingCurrent
				{
					LineId = line.Name,
					Target = downstream.Name,
					Current = current.Magnitude / 1e3 // kA
				});

				powerFlow.Powers.AddOrNew(upstream.Name, new OutgoingPower
				{
					Target = downstream.Name,
					LineId = line.Name,
					ActivePower = power.Real / 1e6, // MW
					ReactivePower = power.Imaginary / 1e6, // MVAr
					PowerLoss = loss / 1e6, // MW
				});
			}

			return powerFlow;
		}

		/// <summary>
		/// Creates a single period settings contract based on the given <paramref name="switchSet"/>, for the given <paramref name="period"/>.
		/// </summary>
		/// <param name="period"></param>
		/// <param name="switchSet"></param>
		/// <returns></returns>
		internal static SinglePeriodSettings CreateSinglePeriodSettingsContract(Core.Period period, SwitchSettings switchSet)
		{
			return CreateSinglePeriodSettingsContract(period.Id, switchSet);
		}

		/// <summary>
		/// Creates a single period settings contract based on the given <paramref name="switchSet"/>, for the given <paramref name="periodId"/>.
		/// </summary>
		/// <param name="periodId"></param>
		/// <param name="switchSet"></param>
		/// <returns></returns>
		public static SinglePeriodSettings CreateSinglePeriodSettingsContract(string periodId, SwitchSettings switchSet)
		{
			SinglePeriodSettings singlePeriodSettingsContract =
				new SinglePeriodSettings() { Period = periodId }; //TODO use of period. Should we write the whole period information?

			Debug.Assert(switchSet.ClosedSwitches.All(l => l.IsSwitchable && switchSet.IsClosed(l)));
			Debug.Assert(switchSet.OpenSwitches.All(l => l.IsSwitchable && switchSet.IsOpen(l)));

			AddSwitchConstracts(false, switchSet.ClosedSwitches);
			AddSwitchConstracts(true, switchSet.OpenSwitches);

			void AddSwitchConstracts(bool open, IEnumerable<Core.Line> switches)
			{
				foreach (Core.Line sw in switches)
				{
					SwitchState ssc = new SwitchState()
					{
						Id = sw.Name,
						Open = open
					};

					singlePeriodSettingsContract.SwitchSettings.Add(ssc);
				}
			}
			return singlePeriodSettingsContract;
		}

		/// <summary>
		/// Writes a <see cref="PowerNetwork"/> to the given stream. Voltage limits will be turned into per-node voltage limits.
		/// 
		/// Note: Does not close the stream.
		/// </summary>
		public static void WriteNetwork(PowerNetwork network, Stream stream, bool prettify = false)
		{
			PowerGrid powerGrid = ConvertToJson(network);
			Serialize(powerGrid, stream, prettify);

		}


		/// <summary>
		/// Writes an IO.SolutionInfo constructed from the given solution, to the given stream.
		/// 
		/// Note: Does not close the stream.
		/// </summary>
		public static void WriteSolutionInfo(PgoSolution solution, Stream stream, bool prettify = false)
		{
			API.SolutionInfo solInfo = solution.Summarize(solution.Problem.CriteriaSet);
			Serialize(solInfo, stream, prettify);
		}


		/// <summary>
		/// Converts a <see cref="PowerNetwork"/> to the equvialent Json representation
		/// </summary>
		public static PowerGrid ConvertToJson(PowerNetwork network)
		{
			// 1. Build node information
			List<Node> nodes = new List<Node>();
			foreach (var node in network.Buses.Where(b => !b.IsTransformer))
			{
				var nodeType =
					(node.Type == BusTypes.Connection) ? NodeType.Connection :
					(node.Type == BusTypes.PowerConsumer) ? NodeType.PowerConsumer :
					(node.Type == BusTypes.PowerProvider) ? NodeType.PowerProvider :
					throw new Exception($"Unsupported node type {node.Type}");

				Node nodeContract = new Node
				{
					Id = node.Name,
					Type = nodeType,
					MinimumVoltage = (!node.IsProvider) ? node.VMin / 1e3 : 0.0,
					MaximumVoltage = (!node.IsProvider) ? node.VMax / 1e3 : double.PositiveInfinity,
					MaximumActiveGeneration = (node.IsProvider) ? node.ActiveGenerationCapacity / 1e6 : double.PositiveInfinity,
					MinimumActiveGeneration = (node.IsProvider) ? node.ActiveGenerationLowerBound / 1e6 : 0.0,
					MaximumReactiveGeneration = (node.IsProvider) ? node.ReactiveGenerationCapacity / 1e6 : double.PositiveInfinity,
					MinimumReactiveGeneration = (node.IsProvider) ? node.ReactiveGenerationLowerBound / 1e6 : 0.0,
					GeneratorVoltage = (node.IsProvider) ? node.GeneratorVoltage / 1e3 : 0.0
				};

				if (node.IsConsumer)
				{
					ConsumerCategoryProvider cp = network.CategoryProvider;
					if (cp.HasDataFor(node))
					{
						if (network.CategoryProvider.Categories(node).CountIs(1))
							nodeContract.ConsumerType = MapConsumerType(cp.Categories(node).Single());
						else
						{
							nodeContract.ConsumerTypeFractions = cp.Categories(node).Select(c => new ConsumerTypeFracton
							{
								ConsumerType = MapConsumerType(c),
								PowerFraction = cp.ConsumptionFraction(node, c)
							}).ToList();
						}
					}
				}

				// We'll add the location if it is there
				if (node.Location != null)
				{
					nodeContract.Location = new List<double> { node.Location.X, node.Location.Y };
				}

				nodes.Add(nodeContract);
			}

			// 2. Add line information
			List<API.Line> lines = new();
			foreach (var line in network.Lines.Where(l => !l.IsTransformerConnection))
			{
				API.Line lineContract = new API.Line
				{
					Id = line.Name,
					SourceNode = line.Node1.Name,
					TargetNode = line.Node2.Name,
					Resistance = line.Resistance,
					Reactance = line.Reactance,
					CurrentLimit = line.IMax,
					VoltageLimit = line.VMax / 1.0e3,
					IsSwitchable = line.IsSwitchable,
					SwitchingCost = (line.IsSwitchable) ? line.SwitchingCost : 0.0,
					IsBreaker = line.IsBreaker,
					FaultsPerYear = line.IsBreaker ? (double?)null : network.PropertiesProvider.FaultsPerYear(line),
					SectioningTime = (line.IsBreaker) ? TimeSpan.Zero : network.PropertiesProvider.SectioningTime(line),
					RepairTime = (line.IsBreaker) ? TimeSpan.Zero : network.PropertiesProvider.RepairTime(line)
				};
				lines.Add(lineContract);
			}

			// 3. Add transformer information
			List<API.Transformer> transformers = new();
			foreach (Core.Transformer trans in network.PowerTransformers)
			{
				List<TransformerMode> modes = ExportTransformerModes(trans);

				API.Transformer transContract = new API.Transformer
				{
					Id = trans.Name,
					Connections = new List<TransformerConnection>(trans.TerminalVoltages.Select(tup => new TransformerConnection { NodeId = tup.terminal.Name, Voltage = tup.voltage / 1000.0 })),
					Modes = modes,
					CenterLocation = (trans.Bus.Location != null) ? new List<double> { trans.Bus.Location.X, trans.Bus.Location.Y } : null,
				};
				transformers.Add(transContract);
			}

			return new PowerGrid
			{
				Name = network.Name,
				SchemaVersion = 1,
				Nodes = nodes,
				Lines = lines,
				Transformers = transformers
			};
		}

		private static List<TransformerMode> ExportTransformerModes(Core.Transformer trans)
		{
			var modes = trans.Modes.Where(m => !m.DerivedFromBidirectionalMode).Select(mode =>
				new TransformerMode
				{
					Source = mode.InputBus.Name,
					Target = mode.OutputBus.Name,
					Operation = MapEnum<TransformerOperation>(mode.Operation),
					Ratio = mode.Operation == TransformerOperationType.FixedRatio ? mode.Ratio : 1,
					PowerFactor = mode.PowerFactor,
					Bidirectional = false,
				}).ToList();

			// Include the derived inverse modes by marking the forward modes as bidirectional.
			foreach (var inverseMode in trans.Modes.Where(m => m.DerivedFromBidirectionalMode))
			{
				var forwardMode = modes.Single(m => m.Source == inverseMode.OutputBus.Name && m.Target == inverseMode.InputBus.Name);
				forwardMode.Bidirectional = true;
			}

			return modes;
		}

		/// <summary>
		/// Converts a collection of <see cref="PeriodData"/> to the equvialent Json representation
		/// </summary>
		public static Demand ConvertToJson(IEnumerable<PeriodData> allPeriodData)
		{
			List<API.Period> periods = new();
			Dictionary<string, List<double>> activeLoadsPerBus = new Dictionary<string, List<double>>();
			Dictionary<string, List<double>> reactiveLoadsPerBus = new Dictionary<string, List<double>>();

			foreach (var periodData in allPeriodData)
			{
				var demands = periodData.Demands;

				periods.Add(new API.Period
				{
					Id = periodData.Name,
					StartTime = periodData.Period.StartTime,
					EndTime = periodData.Period.EndTime
				});

				foreach (var bus in periodData.Network.Consumers)
				{
					if (!activeLoadsPerBus.ContainsKey(bus.Name))
						activeLoadsPerBus[bus.Name] = new List<double>();
					if (!reactiveLoadsPerBus.ContainsKey(bus.Name))
						reactiveLoadsPerBus[bus.Name] = new List<double>();

					activeLoadsPerBus[bus.Name].Add(demands.ActivePowerDemand(bus) / 1e6);
					reactiveLoadsPerBus[bus.Name].Add(demands.ReactivePowerDemand(bus) / 1e6);
				}
			}

			List<LoadSeries> loads = new List<LoadSeries>();
			foreach (var busName in activeLoadsPerBus.Keys)
			{
				loads.Add(new LoadSeries
				{
					NodeId = busName,
					ActiveLoads = activeLoadsPerBus[busName],
					ReactiveLoads = reactiveLoadsPerBus[busName]
				});
			}

			return new Demand
			{
				SchemaVersion = 1,
				Periods = periods,
				Loads = loads
			};
		}

		/// <summary>
		/// Converts a <see cref="PgoSolution"/> to Json objects.
		/// </summary>
		/// <param name="solution">The solution to convert</param>
		/// <param name="flowProvider">The flow provider to report flows for. If null,
		///   no flows are reported</param>
		public static Solution ConvertToJson(PgoSolution solution, IFlowProvider flowProvider)
		{
			Solution jsonSolution = new Solution();
			foreach ((Core.Period period, SwitchSettings switchSet) tup in solution.SwitchSettingsPerPeriod)
			{
				SinglePeriodSettings singlePeriodSettingsContract = CreateSinglePeriodSettingsContract(tup.period, tup.switchSet);
				jsonSolution.PeriodSettings.Add(singlePeriodSettingsContract);
			}

			if (flowProvider != null)
			{
				foreach (var periodSolution in solution.SinglePeriodSolutions)
				{
					PowerFlow powerFlow = CreateSinglePeriodFlowsContract(flowProvider, periodSolution);

					if (powerFlow != null)
					{
						jsonSolution.Flows.Add(powerFlow);

						var calculator = new KileCostCalculator(periodSolution.Period,
							periodSolution.Network,
							new ExpectedConsumptionFromDemand(new[] { periodSolution.PeriodData }),
							periodSolution.Network.PropertiesProvider,
							periodSolution.Network.CategoryProvider);

						var expectedKileCostPerBus = new Dictionary<string, double>();

						foreach (var bus in periodSolution.Network.Buses.Where(b => b.IsConsumer))
						{
							var cost = calculator.IndicatorsForOutagesAt(periodSolution.NetConfig, bus).ExpectedKileCost;
							expectedKileCostPerBus[bus.Name] = cost;
						}

						jsonSolution.KileCosts.Add(new KileCosts(periodSolution.Period.Id)
						{
							ExpectedCosts = expectedKileCostPerBus,
						});
					}
				}
			}

			return jsonSolution;
		}

		/// <summary>
		/// Write a network connectivity status <see cref="NetworkConnectivityStatus"/> as a human-readable string.
		/// Returns null if there are connectivity problems to report.
		/// </summary>
		/// <param name="connectivity"></param>
		/// <returns></returns>
		public static string FormatNetworkConnectivityStatus(NetworkConnectivityStatus connectivity)
		{
			if (connectivity.Ok) return null;

			// Format the NetworkConnectivityStatus as a human-readable string.
			string message = "";
			if (connectivity.UnbreakableCycle != null)
			{
				message += "The network has no radial configuration, since it contains one of more cycles that cannot be broken.\n" +
						  "(A path between two providers also counts as a cycle.)\n" +
						  "One example of an unbreakable cycle is:\n" +
						  String.Join(", ", connectivity.UnbreakableCycle) + "\n";
			}

			if (connectivity.UnconnectableNodes != null)
			{
				message += $"\nThe following {connectivity.UnconnectableNodes.Count} buses cannot be connected to any provider:\n";
				message += $"{String.Join(", ", connectivity.UnconnectableNodes)}\n";
			}

			if (connectivity.UnbreakableCycle == null && connectivity.UnconnectableNodes == null && connectivity.InvalidTransformers != null)
			{
				message += "It was not possible to find a network configuration where all transformers\n" +
							"are used consistently with their specified input/output directions on terminals.\n" +
							"The following is an example of a set of transformers that could not be/ used consistently with their modes while \n" +
							"keeping a radial configuration, and without misconfiguring other transformers.\n" +
							"Each transformer is given as a list of the names of the buses it connects.\n" +
							String.Join(",\n", connectivity.InvalidTransformers.Select(ts => String.Join("/", ts))) + "\n";
			}

			if (string.IsNullOrWhiteSpace(message))
				message = "Unexpected connectivity error.\n";

			return message;
		}

		/// <summary>
		/// Maps <see cref="Core.ConsumerCategory"/> to <see cref="API.ConsumerCategory"/>.
		/// </summary>
		private static API.ConsumerCategory MapConsumerType(Core.ConsumerCategory category)
		{
			return category switch
			{
				Core.ConsumerCategory.Domestic => API.ConsumerCategory.Household,
				Core.ConsumerCategory.Agriculture => API.ConsumerCategory.Agriculture,
				Core.ConsumerCategory.ElectricIndustry => API.ConsumerCategory.ElectricIndustry,
				Core.ConsumerCategory.Industry => API.ConsumerCategory.Industry,
				Core.ConsumerCategory.Public => API.ConsumerCategory.Public,
				Core.ConsumerCategory.Trade => API.ConsumerCategory.Services,
				_ => throw new Exception($"Unsupported category type {category}")
			};
		}

		/// <summary>
		/// Maps <see cref="API.ConsumerCategory"/> to <see cref="Core.ConsumerCategory"/>.
		/// </summary>
		private static Core.ConsumerCategory MapConsumerType(API.ConsumerCategory category)
		{
			return category switch
			{
				API.ConsumerCategory.Household => Core.ConsumerCategory.Domestic,
				API.ConsumerCategory.Agriculture => Core.ConsumerCategory.Agriculture,
				API.ConsumerCategory.ElectricIndustry => Core.ConsumerCategory.ElectricIndustry,
				API.ConsumerCategory.Industry => Core.ConsumerCategory.Industry,
				API.ConsumerCategory.Public => Core.ConsumerCategory.Public,
				API.ConsumerCategory.Services => Core.ConsumerCategory.Trade,
				_ => throw new Exception($"Unsupported category type {category}")
			};
		}

		/// <summary>
		/// Deserialize the given string contaning JSON into an object of type T.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="jsonString"></param>
		/// <returns></returns>
		public static T Deserialize<T>(string jsonString)
		{
			JsonSerializerSettings serializerSettings = new JsonSerializerSettings();
			serializerSettings.DefaultValueHandling = DefaultValueHandling.Populate;
			T result = JsonConvert.DeserializeObject<T>(jsonString, serializerSettings);
			return result;
		}

		/// <summary>
		/// Serializes the specified object to JSON by writing to the given stream.
		/// </summary>
		/// <param name="jsonObject"></param>
		/// <param name="stream"></param>
		/// <param name="prettify"></param>
		public static void Serialize(object jsonObject, Stream stream, bool prettify)
		{
			string jsonString = Serialize(jsonObject, prettify);

			using (StreamWriter writer = new StreamWriter(stream, encoding: System.Text.Encoding.UTF8, bufferSize: 512, leaveOpen: true))
			{
				writer.Write(jsonString);
				writer.Flush();
			}
		}

		/// <summary>
		/// Serializes the specified object to a JSON string.
		/// </summary>
		/// <param name="jsonObject"></param>
		/// <param name="prettify"></param>
		/// <returns></returns>
		public static string Serialize(object jsonObject, bool prettify)
		{
			JsonSerializerSettings serializerSettings = new JsonSerializerSettings();
			serializerSettings.DefaultValueHandling = DefaultValueHandling.Include;
			string jsonString = JsonConvert.SerializeObject(jsonObject, formatting: prettify ? Formatting.Indented : Formatting.None, settings: serializerSettings);
			return jsonString;
		}
	}
}
