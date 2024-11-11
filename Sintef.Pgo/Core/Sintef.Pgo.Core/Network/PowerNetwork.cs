using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Sintef.Scoop.Utilities;
using Sintef.Scoop.Utilities.GeoCoding;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Represents a transmission network, and
	/// computes how the energy flows in the network.
	/// </summary>
	public class PowerNetwork
	{
		#region Public properties

		/// <summary>
		/// Name of the network.
		/// </summary>
		public string Name { get; private set; }

		/// <summary>
		/// All buses in the network.
		/// </summary>
		public IEnumerable<Bus> Buses => _buses.Values;

		/// <summary>
		/// The power providers (substations, transformers...) in the network.
		/// </summary>
		public IEnumerable<Bus> Providers => _powerProviders;

		/// <summary>
		/// The power consumers (end consumers, transformers...) in the network.
		/// </summary>
		public IEnumerable<Bus> Consumers => _powerConsumers;

		/// <summary>
		/// The nodes in the network that are just connection points for lines, i.e. no effect is created or demanded.
		/// </summary>
		public IEnumerable<Bus> Connections => Buses.Except(Providers).Except(Consumers);

		/// <summary>
		/// The number of lines in the network
		/// </summary>
		public int LineCount => _lines.Count;

		/// <summary>
		/// All lines in the network
		/// </summary>
		public IEnumerable<Line> Lines => _lines.Values;

		/// <summary>
		/// The lines which can be switched in the network.
		/// </summary>
		public IEnumerable<Line> SwitchableLines => _switchableLines;

		/// <summary>
		/// The lines which can be switched in the network.
		/// </summary>
		public IEnumerable<Line> Breakers => Lines.Where(l => l.IsBreaker);

		/// <summary>
		/// An integer larger than the Index of all Buses created in this network so far
		/// </summary>
		public int BusIndexBound => _busIndexCounter;

		/// <summary>
		/// An integer larger than the Index of all Lines created in this network so far
		/// </summary>
		public int LineIndexBound => _lineIndexCounter;

		/// <summary>
		/// Provides consumer category.
		/// </summary>
		public ConsumerCategoryProvider CategoryProvider { get; }

		/// <summary>
		/// Provides line fault properties.
		/// </summary>
		public LineFaultPropertiesProvider PropertiesProvider { get; }

		/// <summary>
		/// The connectivity of the network
		/// </summary>
		public ConnectivityType Connectivity
		{
			get
			{
				NetworkConfiguration config = NetworkConfiguration.AllClosed(this);
				config.MakeRadialFlowPossible(throwOnFail: false);

				ConnectivityType result = ConnectivityType.Ok;

				if (config.HasCycles)
					result |= ConnectivityType.HasUnbreakableCycle;

				if (!config.IsConnected)
					result |= ConnectivityType.HasDisconnectedComponent;

				if (config.HasTransformersUsingMissingModes)
					result |= ConnectivityType.HasInconsistentTransformerModes;

				return result;
			}
		}

		/// <summary>
		/// Returns an enumerable of sets (lists) of lines that are parallel between the same pair of buses.
		/// </summary>
		public IEnumerable<IEnumerable<Line>> ParallelNonSwitchableLines
		{
			get
			{
				//All node pairs of lines, sorted internally in the pair by node id.
				IEnumerable<(Bus, Bus, Line)> nodePairsOfLines = Lines
					.Where(l => !(l.IsSwitchable || l.IsBreaker))
					.Select(l => (l.Node1.Index < l.Node2.Index ? (l.Node1, l.Node2, l) : (l.Node2, l.Node1, l)));

				//Group those that have the same pairs.
				IEnumerable<IGrouping<(Bus, Bus), Line>> grouped = nodePairsOfLines.GroupBy(t => (t.Item1, t.Item2), t => t.Item3);
				foreach (var group in grouped.Where(g => !g.CountIs(1)))
				{
					yield return group;
				}
			}
		}

		/// <summary>
		/// The highest generator voltage in the network.
		/// </summary>
		public double MaxGeneratorVoltage => Providers.Select(p => p.GeneratorVoltage).DefaultIfEmpty().Max();

		/// <summary>
		/// The highest possible transformer output voltage in the network.
		/// </summary>
		public double MaxTransformerOutputVoltage =>
			PowerTransformers.SelectMany(tr => tr.Terminals.Select(t => tr.ExpectedVoltageFor(t))).DefaultIfEmpty().Max();

		/// <summary>
		/// The highest possible voltage in the network.
		/// </summary>
		public double MaxNetworkVoltage => Math.Max(MaxGeneratorVoltage, MaxTransformerOutputVoltage);

		/// <summary>
		/// All transformers in the network.
		/// </summary>
		public List<Transformer> PowerTransformers { get; private set; } = new List<Transformer>();

		/// <summary>
		/// Enumerates the directed lines in (one of) the shortest unbreakable cycle(s) in the network.
		/// Assumes that the network has cycles.
		/// </summary>
		public IEnumerable<DirectedLine> ShortestCycle
		{
			get
			{
				NetworkConfiguration config = NetworkConfiguration.AllClosed(this);
				config.MakeRadial(throwOnFail: false);

				return config.CycleBridges.Select(b => config.FindCycleWithBridge(b))
					.ArgMin(cycle => cycle.Count());
			}
		}

		/// <summary>
		/// Enumerates the directed lines in (one of) the shortest unbreakable cycle(s) without switches, or null if no such cycle exists.
		/// </summary>
		public IEnumerable<DirectedLine> ShortestCycleWithoutSwitches
		{
			get
			{
				NetworkConfiguration config = NetworkConfiguration.AllClosed(this);
				config.MakeRadial(throwOnFail: false);

				return config.CycleBridges
					.Select(b => config.FindCycleWithBridge(b))
					.Where(cycle => cycle.All(l => !l.Line.IsSwitchable))
					.OrderBy(cycle => cycle.Count())
					.FirstOrDefault();
			}
		}

		#endregion

		#region Private data members

		/// <summary>
		/// The index to assign to the next bus
		/// </summary>
		private int _busIndexCounter;

		/// <summary>
		/// The index to assign to the next line
		/// </summary>
		private int _lineIndexCounter;

		/// <summary>
		/// The buses in the network, by <see cref="Bus.Index"/>
		/// </summary>
		private Dictionary<int, Bus> _buses = new Dictionary<int, Bus>();

		/// <summary>
		/// The lines in the network, by <see cref="Line.Index"/>
		/// </summary>
		private Dictionary<int, Line> _lines = new Dictionary<int, Line>();

		/// <summary>
		/// The buses that are providers
		/// </summary>
		private List<Bus> _powerProviders = new List<Bus>();

		/// <summary>
		/// The buses that are consumers
		/// </summary>
		private List<Bus> _powerConsumers = new List<Bus>();

		/// <summary>
		/// The lines that are switchable
		/// </summary>
		private List<Line> _switchableLines = new List<Line>();

		/// <summary>
		/// Converts index to name, and vice versa, for buses.
		/// </summary>
		private NameIndexConverter _busNameIndexConverter = new NameIndexConverter();

		/// <summary>
		/// Converts index to name, and vice versa, for lines.
		/// </summary>
		private NameIndexConverter _lineNameIndexConverter = new NameIndexConverter();

		#endregion

		#region Construction

		/// <summary>
		/// Creates an empty PowerNetwork.
		/// </summary>
		/// <param name="name">Human-readable name. If none given, "PowerNetwork" is used.</param>
		public PowerNetwork(string name = "PowerNetwork")
		{
			Name = name;
			_busIndexCounter = 0;
			_lineIndexCounter = 0;
			CategoryProvider = new ConsumerCategoryProvider(this);
			PropertiesProvider = new LineFaultPropertiesProvider(this);
		}

		/// <summary>
		/// Copy constructor
		/// </summary>
		/// <param name="other">Network to copy</param>
		/// <param name="name">Human-readable name. If none given, "Copy_[other.Name]" is used.</param>
		public PowerNetwork(PowerNetwork other, string name = null)
		{
			Name = name ?? "Copy_" + other.Name;
			_busIndexCounter = other._busIndexCounter;
			_lineIndexCounter = other._lineIndexCounter;

			// Clone lines and nodes

			AddBusesLinesTransformersFrom(other, "", null);

			CategoryProvider = other.CategoryProvider.CloneFor(this);
			PropertiesProvider = other.PropertiesProvider.CloneFor(this);
		}

		/// <summary>
		/// Returns a clone of the given network.
		/// </summary>
		/// <returns></returns>
		public PowerNetwork Clone() => new PowerNetwork(this);

		#endregion

		#region Graph analysis and aggregation

		/// <summary>
		/// If the network has configuration independent cycles,
		/// some of these are reported in the returned string, in human readable form.
		/// The function will attempt to create a radial configuration, which will also be returned (whether it is radial or not).
		/// </summary>
		/// <returns>The string description of problems if any (otherwise null), and the configuration that we attempted to make radial.</returns>
		public (string, NetworkConfiguration radialConfig) ReportCycles()
		{
			NetworkConfiguration config = NetworkConfiguration.AllOpen(this);
			config.MakeRadial(throwOnFail: false);

			if (config.HasCycles)
				return ("Network contains cycles or inter-substation paths. For example: \r\n" + config.GetCyclesDescriptionByNodeIDs(), config);

			return (null, config);
		}

		/// <summary>
		/// If the network has configuration independent cycles or disconnected components,
		/// some of these are reported in the returned string, in human readable form.
		/// The function will attempt to create a radial configuration, which will also be returned (whether it is radial or not).
		/// </summary>
		/// <returns>The string description of problems if any (otherwise null), and the configuration that we attempted to make radial.</returns>
		public (string, NetworkConfiguration radialConfig) ReportCyclesOrDisconnectedComponents()
		{
			(string problems, NetworkConfiguration config) = ReportCycles();

			if (config.HasCycles)
				return ("Network contains cycles or inter-substation paths: \r\n" + problems, config);

			if (!config.IsConnected)
				return ("Network contains disconnected components. At least the following buses are un-reachable from any provider: \r\n" + config.GetProviderLessBusesDescription(), config);

			return (null, config);
		}


		/// <summary>
		/// Analyses the network and returns a brief report about various network properties.
		/// Used in debugging, for example.
		/// </summary>
		/// <returns></returns>
		public string AnalyseNetwork(bool verbose)
		{
			string result = AnalyseNetworkProperties(verbose);

			// The following is for PGO development use, showing the *potential* for aggregation
			// (that aggregation is not yet implemented).
			if (verbose)
			{
				//Find out how many nodes and lines that could be aggregated away by aggregating "branch tips" to the closest switch
				(string aggDesc, _) = AnalyseLeafAggregatability();
				result += aggDesc + "\r\n";
			}

			return result;
		}

		/// <summary>
		/// Returns a description of overall properties of the given network.
		/// </summary>
		/// <returns></returns>
		public string AnalyseNetworkProperties(bool verbose)
		{
			string result = "Network properties:\r\n";
			result += $"\t Number of nodes = {Buses.Count()}\r\n";
			result += $"\t\t {Percentage(Consumers.Count(), Buses.Count())} are Consumers\r\n";
			result += $"\t\t {Percentage(Providers.Count(), Buses.Count())} are Providers\r\n";

			result += $"\t Number of lines = {Lines.Count()}\r\n";
			result += $"\t Number of switches = {SwitchableLines.Count()}\r\n";
			result += $"\t Number of breakers = {Breakers.Count()}\r\n";
			result += $"\t Number of switches that are breakers = {SwitchableLines.Count(l => l.IsBreaker)}\r\n";

			int numLinesWithZeroImax = Lines.Count(l => l.IMax == 0);
			if (numLinesWithZeroImax > 0)
				result += $"\t Number of switches that have IMax == 0: {numLinesWithZeroImax}\r\n";
			//int numLinesWithZeroVmax = Lines.Count(l => l.VMax == 0);
			//if (numLinesWithZeroVmax > 0)
			//	result += $"\t Number of switches that have IMax == 0: {numLinesWithZeroVmax}\r\n";

			// These two lines are for PGO development purposes, so disable them by default
			if (verbose)
			{

				//Find out how many nodes that have exactly two input lines, not connected to a switch (indicating potential for aggregation)
				IEnumerable<Bus> simpleBuses = Buses.Where(b => b.IncidentLines.CountIs(2) && b.IncidentLines.All(l => !l.IsSwitchable));
				int numSimpleNode = simpleBuses.Count();
				int numSimpleConsumer = simpleBuses.Count(b => b.IsConsumer);
				result += $"\t Number of 'sequence' nodes that are not connected to any switch: {Percentage(numSimpleNode, Buses.Count())}\r\n";
				result += $"\t\t {Percentage(numSimpleConsumer, Buses.Count())} are Consumers\r\n";

				//Find out how many nodes that have exactly two input lines that are both switches (indicating potential for switch aggregation)
				IEnumerable<Bus> busesBestweenSwitches = Buses.Where(b => b.IncidentLines.CountIs(2) && b.IncidentLines.All(l => l.IsSwitchable));
				int numBusesBetwSw = busesBestweenSwitches.Count();
				int numSimpleConsumerCount = busesBestweenSwitches.Count(b => b.IsConsumer);
				result += $"\t Number of 'sequence' nodes that are between two switches: {Percentage(numBusesBetwSw, Buses.Count())}\r\n";
				result += $"\t\t {Percentage(numSimpleConsumerCount, Buses.Count())} are Consumers\r\n";

			}

			//Number of "leaf nodes" that are not consumers
			IEnumerable<Bus> strangeLeaves = Buses.Where(b => !b.IsConsumer && !b.IsProvider && b.IncidentLines.CountIs(1));
			if (strangeLeaves.Any())
			{
				result += $"\t There are {strangeLeaves.Count()} buses that are neither consumers or providers, but still have only one attached line:\r\n";
				foreach (var bus in strangeLeaves.Take(5))
					result += $"\t\t {bus.Name}\r\n";
			}

			//Check for deviations between given expected input/output voltages, and the give power factor, for transformers
			double limitFraction = 0.05;
			IEnumerable<Transformer> strangeTrafos = PowerTransformers.Where(t =>
			t.Modes.Any(m => m.Operation == TransformerOperationType.FixedRatio &&
			((t.ExpectedVoltageFor(m.InputBus) / t.ExpectedVoltageFor(m.OutputBus)) - m.Ratio) / m.Ratio > limitFraction));
			if (strangeTrafos.Any())
			{
				result += $"\t There are {strangeTrafos.Count()} transformers that have at least one mode where the given fixed conversion ratio deviates with more than {limitFraction * 100} % from the ratio between the expected output and input voltages. These are:\r\n";
				result += $"{strangeTrafos.First().Bus.Name}";
				strangeTrafos.Skip(1).Do(t => result += $", {t.Bus.Name}");
				result += "\r\n";
			}

			return result;
		}

		/// <summary>
		/// Analysises to what degree leafs can be aggregated.
		/// </summary>
		/// <param name="radialConfig">A radial, non-cyclic and connected, configuration. Optional, if not given, then the function
		/// will itself attempt to make a radial configuration.</param>
		/// <returns>A textual description of the analysis results, and a radial configuration if one was either given as input or found by the function. 
		/// If no radial configuration was found (non-cyclic and connected), the returned configuration is null.</returns>
		public (string, NetworkConfiguration) AnalyseLeafAggregatability(NetworkConfiguration radialConfig = null)
		{

			PowerNetwork powerNetwork = this; //It is written like this because I was not sure if the function should be in this class.

			NetworkConfiguration config = radialConfig;
			if (radialConfig == null)
			{
				(string description, NetworkConfiguration confResult) = ReportCyclesOrDisconnectedComponents();
				if (confResult.HasCycles) //Triggers an update of inter-bus relations
					return ("\t OBS: Could not make radial, the configuration still has cycles. No leaf aggregation analysis is possible.\r\n" + description, null);
				else if (!confResult.IsConnected) //Triggers an update of inter-bus relations
					return ("\t OBS: Could not make connected. No leaf aggregation analysis is possible.\r\n" + description, null);
				else
					config = confResult;
			}

			if (config.HasCycles)
				return ("The given configuration must not have cycles.", null);
			if (!config.IsConnected)
				return ("The given configuration must be connected.", null);

			string result = "\r\nPotential for aggregation:\r\n";

			//Start in the leaf nodes that are not next to a switch, and aggregate until all leaf nodes hangs on one or more switches
			Dictionary<Bus, bool> canBeAggregated = powerNetwork.Buses.ToDictionary(b => b, b => false);
			Dictionary<Bus, bool> linkedToSwitchAsOnlyConnectionToAnyProvider = powerNetwork.Buses.ToDictionary(b => b, b => false);
			Dictionary<Bus, int> numberOfAggregatesHangingOnProvider = powerNetwork.Providers.ToDictionary(b => b, b => 0);
			foreach (Bus provider in powerNetwork.Providers)
			{
				config.Traverse(provider, bottomUpAction: AnalyseAgregatability);
			}

			Dictionary<Bus, (int numNodes, int numSwitches, List<Bus> includedChildren)> leafComponents = new Dictionary<Bus, (int, int, List<Bus>)>();
			// Call this function (for each bus in post-order traversal of a radial configuration) to count
			// the size of subtrees of the network graph which have no degrees of freedom in switch configuration, 
			// and therefore can be aggregated.
			void LeafComponents(Bus bus)
			{
				var isLeafNode = config.DownstreamBuses(bus).Count() == 0;

				// Aggregate all child nodes which are complete subtrees with only one parent.
				var childLeafComponents = config.DownstreamBuses(bus)
					.Where(b => leafComponents.ContainsKey(b) &&
							leafComponents[b].includedChildren.Count + 1 == b.IncidentLines.Count()).ToList();

				if (childLeafComponents.Count > 0 || isLeafNode)
				{
					var childAggSize = childLeafComponents.Select(b => leafComponents[b].numNodes).Sum();
					var childNumSwitches = childLeafComponents.Select(b => leafComponents[b].numSwitches).Sum();
					var switches = childLeafComponents.Select(b => config.DownstreamLineToward(bus, b))
						.Where(l => l.IsSwitchable);
					foreach (var child in childLeafComponents)
						leafComponents.Remove(child);
					leafComponents.Add(bus, (childAggSize + childLeafComponents.Count,
						childNumSwitches + switches.Count(),
						childLeafComponents));
				}
			}
			foreach (Bus provider in powerNetwork.Providers)
			{
				config.Traverse(provider, bottomUpAction: LeafComponents);
			}
			leafComponents = leafComponents.Where(v => v.Value.numNodes > 0).ToDictionary(v => v.Key, v => v.Value);

			// For each leaf components, how many nodes/consumers does it contain?
			var leafComponentsNumNodes = leafComponents.Select(l =>
				l.Value.includedChildren.Select(b => config.BusesInSubtree(b).Count()).Sum()).ToList();
			var leafComponentsNumConsumers = leafComponents.Select(l =>
				l.Value.includedChildren.Select(b => config.BusesInSubtree(b).Where(c => c.IsConsumer).Count()).Sum()).ToList();
			var leafComponentsNumSwitches = leafComponents.Select(l => l.Value.numSwitches).ToList();

			Debug.Assert(leafComponentsNumNodes.Sum() == leafComponents.Select(x => x.Value.numNodes).Sum());

			result += $"\t Leaf components (subtrees with no alternative switch settings):\r\n";
			if (leafComponentsNumNodes.Any())
			{
				result += $"\t\t Number of buses in leaf components (sum/avg/max): ";
				result += $"{leafComponentsNumNodes.Sum()} / {leafComponentsNumNodes.Average()} / {leafComponentsNumNodes.Max()}\r\n";
			}
			if (leafComponentsNumConsumers.Any())
			{
				result += $"\t\t Number of consumer buses in leaf components (sum/avg/max): ";
				result += $"{leafComponentsNumConsumers.Sum()} / {leafComponentsNumConsumers.Average()} / {leafComponentsNumConsumers.Max()}\r\n";
			}
			if (leafComponentsNumSwitches.Any())
			{
				result += $"\t\t Number of switches in leaf components that must be closed (sum/avg/max): ";
				result += $"{leafComponentsNumSwitches.Sum()} / {leafComponentsNumSwitches.Average()} / {leafComponentsNumSwitches.Max()}\r\n";
			}

			//Now, how many switches connect to a single node "leaf" component 
			int numAggregatesHangingOnProviders = numberOfAggregatesHangingOnProvider.Sum(b => b.Value);
			//candidates.Count(b => !config.DownstreamBuses(b).Any());
			result += $"\t The number of aggregates hanging directly on a provider (even if all switches are open): {numAggregatesHangingOnProviders}\r\n";


			//Now, find out how many nodes are left that have exactly two input lines (indicating potential for aggregation
			IEnumerable<Bus> simpleBuses = powerNetwork.Buses.Where(b => b.IncidentLines.CountIs(2) && b.IncidentLines.All(l => !l.IsSwitchable));
			int numSimpleNode = simpleBuses.Count(b => !canBeAggregated[b]);
			int numSimpleConsumer = simpleBuses.Count(b => !canBeAggregated[b] && b.IsConsumer);
			result += $"\t After aggregation of leaves:\r\n";
			result += $"\t\t The number of 'sequence' nodes (not connected to a switch): {Percentage(numSimpleNode, Buses.Count())}\r\n";
			result += $"\t\t\t {Percentage(numSimpleConsumer, Buses.Count())} are Consumers\r\n";
			result += $"\t\t The number of remaining 'internal' consumers: {powerNetwork.Consumers.Count(b => !linkedToSwitchAsOnlyConnectionToAnyProvider[b] && !canBeAggregated[b])}\r\n";
			return (result, config);

			//Determines if the given bus can be  aggregated, or if it is next to a switch.
			void AnalyseAgregatability(Bus bus)
			{
				if (config.DownstreamBuses(bus).All(c => canBeAggregated[c]))
				{
					if (!bus.IsProvider && bus.IncidentLines.Any(l => l.IsSwitchable))
					{
						//Check if this is a single last aggregated node hanging from a switch that is the only connection point to the rest of the network.
						if (config.UpstreamLine(bus).IsSwitchable && bus.IncidentLines.Count(l => l.IsSwitchable) == 1) //So, only the upstream line is a switch
						{
							linkedToSwitchAsOnlyConnectionToAnyProvider[bus] = true;
						}
					}
					else
					{
						if (bus.IsProvider)
							numberOfAggregatesHangingOnProvider[bus] = numberOfAggregatesHangingOnProvider[bus] + 1;
						else
							canBeAggregated[bus] = true;
					}
				}
			}
		}

		#endregion

		#region Network construction

		/// <summary>
		/// Add a connection node to the graph.
		/// </summary>
		/// <param name="name">Human-readable name. If null, the id is used.</param>
		/// <param name="vMax">Max voltage in bus, in V.</param>
		/// <param name="vMin">Min voltage in bus, in V.</param>
		/// <param name="location">Optional location of the transition.</param>
		/// <returns>The added bus.</returns>
		public Bus AddTransition(double vMin, double vMax, string name = null, Coordinate location = null)
		{
			Bus bus = CreateBus(vMin, vMax, BusTypes.Connection, name: name, location: location);
			RegisterBus(bus);
			return bus;
		}

		/// <summary>
		/// Add a consumer Bus to the graph.
		/// </summary>
		/// <param name="vMax">Max voltage in bus, in V.</param>
		/// <param name="vMin">Min voltage in bus, in V.</param>
		/// <param name="name">Human-readable name. If null, the id is used.</param>
		/// <param name="location">Optional location of the consumer.</param>
		/// <returns>A reference to the added Bus.</returns>
		public Bus AddConsumer(double vMin, double vMax, string name = null, Coordinate location = null)
		{
			Bus bus = CreateBus(vMin, vMax, BusTypes.PowerConsumer, name: name, location: location);
			_powerConsumers.Add(bus);
			RegisterBus(bus);
			return bus;
		}

		/// <summary>
		/// Adds a provider to the graph.
		/// </summary>
		/// <param name="generatorVoltage">The voltage at the generator, in V.</param>
		/// <param name="generationMin">Minimum power generation, in VA.</param> //TODO specify how this is to be understood
		/// <param name="generationMax">Maximum power generation, in VA.</param>
		/// <param name="name">Human-readable name. If null, the id is used.</param>
		/// <param name="location">Optional location of the provider.</param>
		/// <returns>The added bus.</returns>
		public Bus AddProvider(double generatorVoltage, Complex generationMax, Complex generationMin, string name = null, Coordinate location = null)
		{
		// Todo Right now the substations cannot fail in our model?
			Bus bus = CreateProviderBus(generatorVoltage, generationMax, generationMin, name: name, location: location);
			_powerProviders.Add(bus);
			RegisterBus(bus);
			return bus;
		}

		/// <summary>
		/// Adds a line between two nodes in the graph.
		/// </summary>
		/// <param name="id1Name">One endpoint.</param>
		/// <param name="id2Name">The other endpoint.</param>
		/// <param name="impedance">Line impedance, in Ω.</param>
		/// <param name="imax">Line current capacity, in A.</param>
		/// <param name="vmax">Line max voltage, in V.</param>
		/// <param name="switchable">Whether the line is switchable or not.</param>
		/// <param name="name">Human-readable name. If null, the id is used.</param>
		/// <param name="switchingCost">The cost of changing a switch setting. Optional, default is zero.</param>
		/// <param name="isBreaker">True if the line is a breaker switch. Optional, the default is false.</param>
		/// <returns></returns>
		/// 
		public Line AddLine(string id1Name, string id2Name, Complex impedance, double imax, double vmax,
			bool switchable = false, double switchingCost = 0, bool isBreaker = false, string name = null)
		{
			Bus bus1 = _buses[_busNameIndexConverter.GetIndex(id1Name)];
			Bus bus2 = _buses[_busNameIndexConverter.GetIndex(id2Name)];

			Line line = CreateLine(bus1, bus2, impedance, imax, vmax, switchable: switchable, switchingCost: switchingCost,
				isBreaker: isBreaker, name: name);
			bus1.RegisterLine(line);
			bus2.RegisterLine(line);

			_lines.Add(line.Index, line);
			_lineNameIndexConverter.Associate(line.Index, line.Name);

			if (switchable)
			{
				_switchableLines.Add(line);
			}
			return line;
		}

		/// <summary>
		/// Adds a transformer and connecting lines to the network.
		/// </summary>
		/// <param name="terminalsWithVoltages">The bus id's of all terminals, along with the corresponding voltages.</param>
		/// <param name="modes">Enumerable with data describing each mode that the transformer may operate in.
		///   If null, modes must be added later.</param>
		/// <param name="name">The name that will be used for the returned transformer bus.</param>
		/// <param name="location"></param>
		/// <param name="lineNames">If not null, these names are used for the transformer internal lines</param>
		/// <returns></returns>
		public Transformer AddTransformer(IEnumerable<(string busId, double v)> terminalsWithVoltages,
			IEnumerable<TransformerModeData> modes = null,
			string name = null, Coordinate location = null,
			IEnumerable<string> lineNames = null)
		{
			Transformer transformer = new Transformer(terminalsWithVoltages.Select(tup => (GetBus(tup.busId), tup.v)), name);

			AddTransformer(transformer, name, location, lineNames);

			if (modes != null)
			{
				foreach (var m in modes)
				{
					if (!m.VoltageRatio.HasValue && m.Operation == TransformerOperationType.FixedRatio)
						throw new ArgumentException("A transformer was added with fixed-ratio operation, but without a given voltage ratio.");

					// Adds an optionally-symmetric set of modes
					double vr = m.VoltageRatio ?? 1;
					transformer.AddMode(m.InputBusName, m.OutputBusName, m.Operation, vr, m.PowerFactor, m.Bidirectional);
				}
			}

			return transformer;
		}

		/// <summary>
		/// Adds a transformer with the same data as the given one.
		/// </summary>
		/// <param name="original"></param>
		/// <param name="name">The name for the returned transformer bus.Optional. If not given, the name for the transformer
		/// bus of the given transformer is used.</param>
		internal Transformer AddTransformerLike(Transformer original, string name = null)
		{
			var lineNames = original.Terminals.Select(terminal => terminal.IncidentLines.Single(l => l.IsBetween(terminal, original.Bus)).Name);
			IEnumerable<(string, double voltage)> terminalsWithVoltages = original.TerminalVoltages.Select(tup => (tup.terminal.Name, tup.voltage));
			var transformer = AddTransformer(terminalsWithVoltages, null, name ?? original.Bus.Name, original.Bus.Location, lineNames: lineNames);
			transformer.CopyModesFrom(original, GetBus);
			return transformer;
		}

		/// <summary>
		/// Adds a new bus based on the data in the given bus, according to its type.
		/// If it already exists, nothing happens.
		/// </summary>
		/// <param name="bus">Returns the new bus.</param>
		/// <param name="newName">The name that the bus will be given in this network.</param>
		internal Bus AddBusLike(Bus bus, string newName)
		{
			switch (bus.Type)
			{
				case BusTypes.Connection:
					return AddTransition(bus.VMin, bus.VMax, newName, bus.Location);
				case BusTypes.PowerProvider:
					return AddProvider(bus.GeneratorVoltage, bus.GenerationCapacity, bus.GenerationLowerBound, newName, bus.Location);
				case BusTypes.PowerConsumer:
					return AddConsumer(bus.VMin, bus.VMax, newName, bus.Location);
				default:
					throw new Exception("Unknown bus type");
			}
		}

		/// <summary>
		/// Joins a copy of the given network to this one, with a set of randomly generated switchable lines.
		/// </summary>
		/// <param name="netToAdd">The network from which we copy before adding.</param>
		/// <param name="numberOfJoints">The number of switchable lines connecting the new with the old.</param>
		/// <param name="namePostFix">Postfix to be added to all bus and line names, to keep the new from the old.</param>
		/// <param name="rand"></param>
		public void JoinWithRandomSwitches(PowerNetwork netToAdd, int numberOfJoints, string namePostFix, Random rand)
		{
			//Choose some random old buses for connections
			List<Bus> oldBuses = Buses.RandomElements(numberOfJoints).ToList();

			//Add the buses and lines
			AddBusesLinesTransformersFrom(netToAdd, namePostFix, rand);

			//Add switches between the old and the new.
			List<Bus> busesInNetToAdd = netToAdd.Buses.RandomElements(numberOfJoints).ToList();

			for (int i = 0; i < numberOfJoints; i++)
			{
				//Get switching cost from some random other line
				double swCost = SwitchableLines.RandomElement(rand).SwitchingCost;
				AddLine(oldBuses[i].Name, busesInNetToAdd[i].Name + namePostFix, Complex.Zero, double.PositiveInfinity, double.PositiveInfinity, true, swCost, name: $"NetCoupling[{oldBuses[i].Name} <--> {busesInNetToAdd[i].Name + namePostFix}]");
			}
		}

		#endregion

		#region Network query

		/// <summary>
		/// Checks if a bus with the given name is included in the network (it may have been lost in translation).
		/// </summary>
		/// <param name="name"></param>
		/// <returns></returns>
		public bool HasBus(string name) => _busNameIndexConverter.HasName(name);

		/// <summary>
		/// If the network has a bus with the given name, returns true and sets <paramref name="bus"/> to the result on exit.
		/// Otherwise, returns false.
		/// </summary>
		public bool TryGetBus(string name, out Bus bus)
		{
			if (_busNameIndexConverter.HasName(name))
			{
				bus = GetBus(name);
				return true;
			}
			else
			{
				bus = null;
				return false;
			}
		}

		/// <summary>
		/// Finds a bus with a given name. 
		/// </summary>
		/// <param name="name">Name of the bus.</param>
		/// <returns>The matching bus.</returns>
		public Bus GetBus(string name) => _buses[_busNameIndexConverter.GetIndex(name)];

		/// <summary>
		/// Finds line between two buses.
		/// </summary>
		/// <returns>The line between the two arguments, or null if none exists.</returns>
		public Line GetLine(Bus b1, Bus b2)
		{
			foreach (var l in b1.IncidentLines)
			{
				if (l.OtherEnd(b1) == b2)
				{
					return l;
				}
			}
			return null;
		}

		/// <summary>
		/// Enumerates the lines that are registered between the two given buses.
		/// </summary>
		public IEnumerable<Line> LinesBetween(Bus bus1, Bus bus2)
		{
			return _lines.Values.Where(l => l.IsBetween(bus1, bus2));
		}

		/// <summary>
		/// Finds line between two buses given by name.
		/// </summary>
		/// <param name="name1">Name of one endpoint.</param>
		/// <param name="name2">Name of the other endpoint.</param>
		/// <returns>The line between the two buses, or null if none exists.</returns>
		public Line GetLine(string name1, string name2) => GetLine(GetBus(name1), GetBus(name2));

		/// <summary>
		/// Returns the line with the given name
		/// </summary>
		public Line GetLine(string name) => _lines[_lineNameIndexConverter.GetIndex(name)];

		/// <summary>
		/// If a line with the given <paramref name="name"/> exists, returns true and assigns
		/// it to <paramref name="line"/>.
		/// Otherwise returns false.
		/// </summary>
		public bool TryGetLine(string name, out Line line)
		{
			if (_lineNameIndexConverter.HasName(name))
			{
				line = _lines[_lineNameIndexConverter.GetIndex(name)];
				return true;
			}

			line = default;
			return false;
		}

		/// <summary>
		/// Enumerates the set of switches closest to the given line.
		/// When these switches are opened, the line is
		/// isolated from (most of) the rest of the network. The line itself,
		/// if it is a switch, does not count.
		/// Throws an exception if there is a path between the line and a generator
		/// with no switch on it.
		/// </summary>
		public IEnumerable<Line> ClosestSwitches(Line line)
		{
			HashSet<Line> explored = new HashSet<Line>();
			List<Line> result = new List<Line>();

			explored.Add(line);
			ExploreLinesFrom(line.Node1);
			ExploreLinesFrom(line.Node2);

			return result;


			void ExploreLinesFrom(Bus node)
			{
				if (node.IsProvider)
					throw new Exception($"There is no switch between line {line.Name} and provider {node.Name}");

				foreach (var line2 in node.IncidentLines.Except(explored))
				{
					explored.Add(line2);

					if (line2.IsSwitchable)
						result.Add(line2);
					else
						ExploreLinesFrom(line2.OtherEnd(node));
				}
			}
		}

		#endregion

		#region Private methods

		/// <summary>
		/// Adds copies of the buses, lines and transformers of the given network to this one. 
		/// Introduces random modifications of switching costs for the new switches, unless <paramref name="rand"/> == null in which case
		/// the original switching costs are used.
		/// </summary>
		/// <param name="other">The network we copy from.</param>
		/// <param name="namePostfix">Postfix to be added to all bus, line and transformer names, to keep the new from the old.</param>
		/// <param name="rand"></param>
		private void AddBusesLinesTransformersFrom(PowerNetwork other, string namePostfix, Random rand)
		{
			other.Buses.Do(b => { if (!b.IsTransformer) AddBusLike(b, b.Name + namePostfix); });
			other.PowerTransformers.Do(t => AddTransformerLike(t, t.Bus.Name + namePostfix));
			other.Lines.Do(l =>
			{
				if (!l.IsTransformerConnection)
				{
					double switchCost = l.IsSwitchable ? GetRandomModifiedSwitchingCost(l.SwitchingCost, rand) : 0;
					AddLine(l.Node1.Name + namePostfix, l.Node2.Name + namePostfix, l.Impedance, l.IMax, l.VMax, l.IsSwitchable, switchCost, name: l.Name + namePostfix);
				}
			});
		}

		/// <summary>
		/// Returns a smaller, random modification to the input switching cost.
		/// </summary>
		/// <param name="switchingCost"></param>
		/// <param name="rand"></param>
		/// <returns></returns>
		private double GetRandomModifiedSwitchingCost(double switchingCost, Random rand)
		{
			return (rand != null && switchingCost != 0) ?
			Math.Max(0, switchingCost * (((double)rand.Next(1, 61)) - 30.0) / 100.0) :
			switchingCost;
		}

		/// <summary>
		/// Adds a transformer and connecting lines to the network.
		/// </summary>
		/// <param name="transformer"></param>
		/// <param name="name">The name that will be used for the returned transformer bus.</param>
		/// <param name="location">The coordinate of the transformer bus</param>
		/// <param name="lineNames">If not null, these names are used for the transformer internal lines</param>
		/// <returns></returns>
		private void AddTransformer(Transformer transformer, string name = null, Coordinate location = null,
			IEnumerable<string> lineNames = null)
		{
			Bus transformerBus = CreateTransformerBus(transformer, name: name, location: location);
			transformer.Bus = transformerBus;
			PowerTransformers.Add(transformer);
			RegisterBus(transformerBus);

			// Now we add connecting lines
			if (lineNames == null)
			{
				foreach (Bus adjacentBus in transformer.Terminals)
					AddTransformerConnectingLine(adjacentBus, transformerBus);
			}
			else
			{
				foreach ((Bus adjacentBus, string lineName) in transformer.Terminals.Zip(lineNames))
					AddTransformerConnectingLine(adjacentBus, transformerBus, lineName);
			}
		}

		private void AddTransformerConnectingLine(Bus entry, Bus transformerBus, string name = null)
		{
			if (transformerBus.Type != BusTypes.PowerTransformer)
				throw new ArgumentException("TransformerBus must be a transformer");

			Line line = CreateLine(entry, transformerBus, 0.0, double.PositiveInfinity, double.PositiveInfinity, name: name); //This line is fake
			entry.RegisterLine(line);
			transformerBus.RegisterLine(line);

			_lines.Add(line.Index, line);
			_lineNameIndexConverter.Associate(line.Index, line.Name);
		}

		/// <summary>
		/// Constructs a line with given parameters. Use this instead of calling the <see cref="Line"/> constructor directly.
		/// </summary>
		/// <param name="bus1">One endpoint.</param>
		/// <param name="bus2">The other endpoint.</param>
		/// <param name="impedance">Line impedance, in Ω.</param>
		/// <param name="imax">Line current capacity, in A.</param>
		/// <param name="vmax">Maximum line voltage, in V.</param>
		/// <param name="switchable">Whether the line is switchable or not.</param>
		///<param name="switchingCost">The cost of switching the line, if it is switchable. For non-switchable lines, any value here is ignored. Optional, the default value is zero.</param>
		/// <param name="isBreaker">Is the line a breaker (in the KILE sense)</param>
		/// <param name="name">Human-readable name</param>
		private Line CreateLine(Bus bus1, Bus bus2, Complex impedance, double imax, double vmax, bool switchable = false, double switchingCost = 0,
			bool isBreaker = false, string name = null)
		{
			return new Line(NextLineIndex(), bus1, bus2, impedance, imax, vmax,
				switchable: switchable, switchingCost: switchingCost, isBreaker: isBreaker, name: name);
		}

		/// <summary>
		/// Creates a bus that is not a provider. Use this instead of calling any Bus constructor directly.
		/// For poviderds, use <see cref="CreateProviderBus"/> instead.
		/// </summary>
		/// <param name="vMax">Max voltage in bus, in V.</param>
		/// <param name="type">The bus' type.</param>
		/// <param name="vMin">Min voltage in bus, in V.</param>
		/// <param name="name">Optional user-supplied name. If none is given, a string representation of the ID is used.</param>
		/// <param name="location">Optional location of the bus.</param>
		private Bus CreateBus(double vMin, double vMax, BusTypes type, string name = null, Coordinate location = null)
		{
			Bus bus = new Bus(GetNextBusId(), vMin, vMax, type, name: name, location: location);
			return bus;
		}

		/// <summary>
		/// Creates a provider bus. Use this instead of calling any Bus constructor directly.
		/// For other kinds of buses, use <see cref="CreateBus"/>.
		/// </summary>
		/// <param name="voltage">The generator voltage, in V</param>
		/// <param name="minPower">Minimum power generation, in VA.</param> //TODO specify how these are to be understood
		/// <param name="maxPower">Maximum power generation, in VA.</param>
		/// <param name="name">Optional user-supplied name. If none is given, a string representation of the ID is used.</param>
		/// <param name="location">Optional location of the provider.</param>
		private Bus CreateProviderBus(double voltage, Complex maxPower, Complex minPower, string name = null, Coordinate location = null)
		{
			Bus bus = new Bus(GetNextBusId(), voltage, maxPower, minPower, name: name, location: location);
			return bus;
		}

		/// <summary>
		/// Creates a transformer bus. Use this instead of calling any Bus constructor directly.
		/// For other kinds of buses, use <see cref="CreateBus"/>.
		/// </summary>
		/// <param name="transformer">The transformer object to be associated with the new bus.</param>
		/// <param name="name">Optional user-supplied name. If none is given, a string representation of the ID is used.</param>
		/// <param name="location">Optional location of the provider. If not given, and if the transformer's connections all have
		/// coorinates, the tranformerbus will get coordinates corresponding to the geographical center point of those.</param>
		private Bus CreateTransformerBus(Transformer transformer, string name = null, Coordinate location = null)
		{
			Coordinate center = location;
			if (location == null && transformer.Terminals.All(t => t.Location != null))
			{
				center = Coordinate.CenterPoint(transformer.Terminals.Select(t => t.Location));
			}

			Bus bus = new Bus(GetNextBusId(), transformer, name: name, location: center);
			return bus;
		}

		/// <summary>
		/// Formats a count, with info on how many percent it is of the total
		/// </summary>
		/// <param name="count">The count to format</param>
		/// <param name="totalCount">The total</param>
		private string Percentage(int count, int totalCount)
		{
			if (count == 0)
				return "0";
			double ratio = (double)count / totalCount;

			return $"{count} ({ratio:P2} of {totalCount})";
		}

		/// <summary>
		/// Registers bus index to name correspondence.
		/// </summary>
		/// <param name="bus"></param>
		private void RegisterBus(Bus bus)
		{
			_buses[bus.Index] = bus;
			_busNameIndexConverter.Associate(bus.Index, bus.Name);
		}

		/// <summary>
		/// Returns the index for the next bus to create.
		/// </summary>
		private int GetNextBusId()
		{
			int nextId = _busIndexCounter;
			_busIndexCounter++;
			return nextId;
		}

		/// <summary>
		/// Returns the index for the next line to create.
		/// </summary>
		private int NextLineIndex()
		{
			int nextId = _lineIndexCounter;
			_lineIndexCounter++;
			return nextId;
		}

		#endregion


		private class NameIndexConverter
		{
			private readonly Dictionary<int, string> _indexToName = new Dictionary<int, string>();
			private readonly Dictionary<string, int> _nameToIndex = new Dictionary<string, int>();

			/// <summary>
			/// Internally registers association of index with name.
			/// </summary>
			public void Associate(int index, string name)
			{
				if (_indexToName.ContainsKey(index))
					throw new ArgumentException($"Index {index} already exists in converter!");
				if (_nameToIndex.ContainsKey(name))
					throw new ArgumentException($"Name {name} already exists in converter!");

				_indexToName[index] = name;
				_nameToIndex[name] = index;
			}

			/// <summary>
			/// Get index for given name.
			/// </summary>
			public int GetIndex(string name) => _nameToIndex[name];

			/// <summary>
			/// Get name for given index.
			/// </summary>
			public string GetNameFor(int index) => _indexToName[index];

			/// <summary>
			/// Checks if an item with the given name has been registered.
			/// </summary>
			internal bool HasName(string name) => _nameToIndex.ContainsKey(name);
		}

		/// <summary>
		/// A type of connectivity for a power network
		/// </summary>
		[Flags]
		public enum ConnectivityType
		{
			/// <summary>
			/// The network can be made radial by opening a suitable set of switches
			/// </summary>
			Ok = 0,

			/// <summary>
			/// The network has buses that cannot be connected to a generator
			/// </summary>
			HasDisconnectedComponent = 1,

			/// <summary>
			/// The network has a cycle (or path between two generators) with no switches
			/// </summary>
			HasUnbreakableCycle = 2,

			/// <summary>
			/// There is no radial solution where all transformers have a valid input bus that has corresponding modes for all output buses.
			/// </summary>
			HasInconsistentTransformerModes = 4,
		}
	}

	/// <summary>
	/// Data describing a mode that a transformer may operate in
	/// </summary>
	public record TransformerModeData
	{
		/// <summary>
		/// Name of input side connection bus
		/// </summary>
		public string InputBusName;

		/// <summary>
		/// Name of output side connection bus
		/// </summary>
		public string OutputBusName;

		/// <summary>
		/// The type of operation (auto or fixed ratio)
		/// </summary>
		public TransformerOperationType Operation;

		/// <summary>
		/// The voltage ratio (input voltage/output voltage). Optional. Only applicable if <see cref="Operation"/> is <see cref="TransformerOperationType.FixedRatio"/>.
		/// </summary>
		public double? VoltageRatio;

		/// <summary>
		/// The power factor, determining loss. Output power = intput power * powerFactor
		/// </summary>
		public double PowerFactor;

		/// <summary>
		/// Whether this mode is valid to use with output and input swapped.
		/// </summary>
		public bool Bidirectional;

		/// <summary>
		/// Initializes the mode data
		/// </summary>
		public TransformerModeData(string inputBusName, string outputBusName, TransformerOperationType operationType,
			double? voltageRatio, double powerFactor, bool bidirectional)
		{
			InputBusName = inputBusName;
			OutputBusName = outputBusName;
			Operation = operationType;
			VoltageRatio = voltageRatio;
			PowerFactor = powerFactor;
			Bidirectional = bidirectional;
		}
	}
}
