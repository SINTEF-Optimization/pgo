using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Sintef.Pgo.Core
{

	/// <summary>
	/// A network configuration is defined by a <see cref="PowerNetwork"/> and a 
	/// <see cref="Core.SwitchSettings"/>, which holds an open/closed setting for each switchable line.
	/// The <see cref="NetworkConfiguration"/> represents the
	/// sub-network one gets by disregarding all open lines, and provides topological 
	/// information on this sub-network.
	/// 
	/// A radial network configuration is one where each bus is connected to a provider through
	/// exactly one path of lines. Thus, it has the topology of a forest, where
	/// each tree contains exactly one provider. It is connected and without cycles,
	/// in the sense defined by <see cref="IsConnected"/> and <see cref="HasCycles"/>.
	/// 
	/// In a radial configuration, the direction toward a provider is called the 'upstream' direction,
	/// while the direction away from a provider is called 'downstream'. Thus, each bus (except providers)
	/// is connected to exactly one upstream line, but can have zero to many downstream lines.
	/// 
	/// Most functions in this class are designed for radial configurations.
	/// If the configuration is not radial, this class makes a best effort to provide useful
	/// answers where possible, as follows:
	/// 
	/// If there are one or more buses that are not connected to a provider, the configuration is 
	/// not connected. For a disconnected bus or line, there are no upstream or downstream directions, and
	/// most functions in this class will throw an exception or give a wrong answer if invoked on them.
	/// Still, all functions work for the connected part.
	/// 
	/// If the configuration has one or more cycles, the <see cref="NetworkConfiguration"/> will 
	/// select one arbitrary line from each cycle. These selected lines are called bridges.
	/// Removing the bridges would break all cycles and re-establish up/downstream directions, and
	/// the configuration's functions produce results as if this has been done.
	/// This way, most functions make sense also in the presence of cycles, except that a bridge itself
	/// does not have up/downstream directions.
	/// A line does not have to be switchable in order to be selected as a bridge.
	/// 
	/// The configuration caches certain information, such as the upstream line for each bus.
	/// This information is maintained lazily by recording changes to switch settings and updating
	/// on the first request for information after one or more switches have been changed.
	/// </summary>
	public class NetworkConfiguration
	{
		#region Public properties

		/// <summary>
		/// The power network this is a configuration for.
		/// </summary>
		public PowerNetwork Network { get; private set; }

		/// <summary>
		/// The switch settings.
		/// </summary>
		public SwitchSettings SwitchSettings { get; private set; }

		/// <summary>
		/// True if the network configuration is radial (per provider), that is, 
		/// each bus has exactly one path to a provider.
		/// </summary>
		public bool IsRadial => !HasCycles && IsConnected;

		/// <summary>
		/// True if the network configuration is radial and each transformer has a corresponding mode for its upstream line.
		/// </summary>
		public bool AllowsRadialFlow(bool requireConnected)
		{
			if (requireConnected) return IsRadial && !HasTransformersUsingMissingModes;
			else return !HasCycles && !HasTransformersUsingMissingModes;
		}

		/// <summary>
		/// True if the network configuration is connected (per provider), that is,
		/// each bus is connected to a provider.
		/// </summary>
		public bool IsConnected
		{
			get
			{
				UpdateInterBusRelations();
				return _isConnected;
			}
		}

		/// <summary>
		/// True if the network configuration has cycles, that is,
		/// if two providers are connected or a bus has more than one path to a provider.
		/// Cycles among buses that are not connected to a provider do not count.
		/// </summary>
		public bool HasCycles => CycleBridges.Any();

		/// <summary>
		/// True if the network configuration has any transformers that are using missing modes (see <see cref="TransformersUsingMissingModes"/>).
		/// </summary>
		public bool HasTransformersUsingMissingModes => TransformersUsingMissingModes.Any();

		/// <summary>
		/// Enumerates the lines in the underlying network that are present,
		/// i.e. either not switchable or switchable but closed.
		/// </summary>
		public IEnumerable<Line> PresentLines => Network.Lines.Where(l => !l.IsSwitchable || SwitchSettings.IsClosed(l));

		/// <summary>
		/// Enumerates the lines in the underlying network that are present and connected to a provider.
		/// </summary>
		public IEnumerable<Line> PresentConnectedLines => Network.Lines.Where(l => (!l.IsSwitchable || SwitchSettings.IsClosed(l)) && LineIsConnected(l));

		/// <summary>
		/// Enumerates the open lines in the configuration.
		/// </summary>
		public IEnumerable<Line> OpenLines => Network.Lines.Where(l => l.IsSwitchable && SwitchSettings.IsOpen(l));

		/// <summary>
		/// Enumerates buses that are consumers, and are not connected to a provider in this configuration.
		/// </summary>
		public IEnumerable<Bus> UnconnectedConsumers => Network.Consumers.Where(b => !BusIsConnected(b));

		/// <summary>
		/// Enumerates all buses that are part of a cycle (by the defintion of <see cref="HasCycles"/>)
		/// </summary>
		public IEnumerable<Bus> BusesInCycles
		{
			get
			{
				UpdateInterBusRelations();

				return _cycleBridges.SelectMany(line => CycleWith(line))
					.SelectMany(l => l.Line.Endpoints)
					.Distinct();
			}
		}

		/// <summary>
		/// Enumerates all buses that are not connected to a provider.
		/// </summary>
		public IEnumerable<Bus> DisconnectedBuses
		{
			get
			{
				UpdateInterBusRelations();

				return Network.Buses.Where(b => _providerForBus[b.Index] == null);
			}
		}

		/// <summary>
		/// Enumerates all open switches that are between a bus that is connected to a provider
		/// and one that is not
		/// </summary>
		public IEnumerable<Line> ConnectedComponentBoundary
		{
			get
			{
				return OpenLines.Where(line => BusIsConnected(line.Node1) != BusIsConnected(line.Node2));
			}
		}

		/// <summary>
		/// Returns one (closed) switch from each cycle in the configuration. The same switch
		/// may be selected for more than one cycle.
		/// The result contains a null if there is a cycle with no switch.
		/// 
		/// The switch is selected in order to make the configuration as balanced as possible
		/// (both ends should be equally distant from a generator)
		/// </summary>
		public List<Line> BestSwitchFromEachCycle
		{
			get
			{
				return CycleBridges
					.Select(BestSwitchInCycle)
					.Distinct()
					.ToList();


				Line BestSwitchInCycle(Line bridge)
				{
					var linesInCycle = FindCycleWithBridge(bridge)
						.Select(dl => dl.Line);

					// We want to find a switch that is halfway between the two providers if the cycle is between providers,
					// or farthest from the single provider otherwise
					int bestDepth = (DistanceToProvider(bridge.Node1) + DistanceToProvider(bridge.Node2)) / 2;

					return linesInCycle
						.Where(line => line.IsSwitchable)
						.ArgMin(line => Math.Abs(bestDepth - DistanceToProvider(line.Node1)));
				}
			}
		}

		/// <summary>
		/// Returns true if there is a path betweeen the bus and a provider
		/// </summary>
		public bool BusIsConnected(Bus bus)
		{
			UpdateInterBusRelations();

			return _providerForBus[bus.Index] != null;
		}

		/// <summary>
		/// Returns true if the line is not an open switch, and there is a path from its endpoints to a provider. 
		/// </summary>
		public bool LineIsConnected(Line l)
		{
			if (l.IsSwitchable && SwitchSettings.IsOpen(l)) return false;
			var allConnected = l.Endpoints.All(b => BusIsConnected(b));
			var allDisconnected = l.Endpoints.All(b => !BusIsConnected(b));
			if (allConnected) return true;
			if (allDisconnected) return false;
			throw new Exception("Line is present but its endpoint have inconsistent connectedness");
		}

		/// <summary>
		/// Returns true if the given <paramref name="line"/> is part of the path from 
		/// the given <paramref name="bus"/> to its provider (and the bus is connected)
		/// </summary>
		public bool LineIsUpstreamFromBus(Line line, Bus bus)
		{
			while (!bus.IsProvider)
			{
				if (line == UpstreamLine(bus))
					return true;
				bus = UpstreamBus(bus);
			}

			return false;
		}

		/// <summary>
		/// Enumerates the lines incident to the given <paramref name="bus"/> that are present,
		/// i.e. either not switchable or switchable but closed in the configuration.
		/// </summary>
		public IEnumerable<Line> PresentLinesAt(Bus bus) => bus.IncidentLines.Where(l => !l.IsSwitchable || SwitchSettings.IsClosed(l));

		/// <summary>
		/// Enumerates all buses in the subtree with root at the given <paramref name="bus"/>.
		/// This includes the bus itself and all its downstream buses, to any level.
		/// </summary>
		public IEnumerable<Bus> BusesInSubtree(Bus bus)
		{
			List<Bus> buses = new List<Bus> { bus };

			while (buses.Any())
			{
				foreach (var b in buses)
					yield return b;

				buses = buses
					.SelectMany(b => DownstreamBuses(b))
					.ToList();
			}
		}

		/// <summary>
		/// Enumerates the lines in the subtree with root at the given <paramref name="bus"/>.
		/// </summary>
		/// <param name="bus">Root</param>
		/// <param name="stopAt">If this function is supplied, enumeration of the subtree 
		///   does not include or continue below any line for which the function returns true.
		/// </param>
		public IEnumerable<Line> LinesInSubtree(Bus bus, Func<Line, bool> stopAt = null)
		{
			stopAt = stopAt ?? ((Line l) => false);

			IEnumerable<Line> downstreamLines = DownstreamLines(bus).Where(l => !stopAt(l));

			return downstreamLines
				.SelectMany(l => LinesInSubtree(DownstreamEnd(l), stopAt))
				.Concat(downstreamLines);
		}

		/// <summary>
		/// Enumerates the connected lines in the vicinity of the given bus.
		/// Follows lines both upstream and downstream, and also works in disconnected
		/// components of the network.
		/// </summary>
		/// <param name="startBus">The bus to start at</param>
		/// <param name="assumeRadial">If true, the algorithm is faster. However, it may
		///   go into an eternal loop if the network configuration is not radial.</param>
		/// <param name="stopAt">If not null, defines a boundary at which the exploration stops. If
		///   stopAt(line, bus) returns true, the exploration will not follow 'line' in the direction
		///   from 'bus', and 'line' will not be included in the result.
		/// </param>
		/// <param name="stopAfter">If not null, defines a boundary outside which the exploration stops. If
		///   stopAt(line, bus) returns true, 'line' will be included in the result, but
		///   the exploration will not follow further lines beyond it when encountering 'line' in the direction
		///   from 'bus'.
		/// </param>
		public List<Line> LinesAround(Bus startBus, bool assumeRadial, Func<Line, Bus, bool> stopAt = null,
			Func<Line, Bus, bool> stopAfter = null)
		{
			stopAt = stopAt ?? ((Line l, Bus b) => false);
			stopAfter = stopAfter ?? ((Line l, Bus b) => false);

			UpdateInterBusRelations();

			HashSet<Line> seen = null;
			if (!assumeRadial)
				seen = new HashSet<Line>();

			List<Line> result = new List<Line>();
			Stack<(Bus, Line)> stack = new Stack<(Bus, Line)>();
			stack.Push((startBus, null));

			while (stack.Count > 0)
			{
				var (bus, from) = stack.Pop();

				foreach (var line in bus.IncidentLinesArray)
				{
					if (ReferenceEquals(line, from))
						continue;
					if (!assumeRadial && seen.Contains(line))
						continue;
					if (line.IsSwitchable && SwitchSettings.IsOpen(line))
						continue;
					if (stopAt(line, bus))
						continue;

					result.Add(line);
					seen?.Add(line);

					if (!stopAfter(line, bus))
						stack.Push((line.OtherEnd(bus), line));
				}
			}

			return result;
		}


		/// <summary>
		/// Enumerates one line from each cycle in the network
		/// </summary>
		public IEnumerable<Line> CycleBridges
		{
			get
			{
				UpdateInterBusRelations();

				return _cycleBridges;
			}
		}

		/// <summary>
		/// Enumerates transformers whose upstream line does not have a corresponding mode.
		/// </summary>
		public IEnumerable<Transformer> TransformersUsingMissingModes
		{
			get
			{
				UpdateInterBusRelations();
				return _invalidTransformerModes;
			}
		}

		#endregion

		#region Private members

		#region Change management

		/// <summary>
		/// False until relations have been calculated. Used for late computation of relations.
		/// </summary>
		private bool _relationsAreUpToDate = false;

		/// <summary>
		/// These switchable lines were opened since last update, and if relevant the cycle bridge
		/// that was created when the cycle was found that the switch opening will break.
		/// </summary>
		private List<(Line switchLine, Line cycleBridge)> _switchesThatWereOpened;

		/// <summary>
		/// These switchable lines were opened since last update
		/// </summary>
		private List<Line> _switchesThatWereClosed;

		#endregion

		/// <summary>
		/// True if the configuration is connected
		/// </summary>
		private bool _isConnected;



		/// <summary>
		/// For each connected bus, the bus that connects it to a provider.
		/// Null for disconnected buses and providers.
		/// </summary>
		private Bus[] _upstreamBus;

		/// <summary>
		/// For each connected bus, the line that connects it to a provider.
		/// Null for disconnected buses and providers.
		/// </summary>
		private Line[] _upstreamLine;

		/// <summary>
		/// For each bus, the other buses that this bus connects to a provider. 
		/// Empty for diconnected buses.
		/// </summary>
		private List<Bus>[] _downstreamBuses;

		/// <summary>
		/// For each bus, the lines connecting this bus to other buses farther from a provider. 
		/// Empty for diconnected buses.
		/// </summary>
		private List<Line>[] _downstreamLines;

		/// <summary>
		/// For each connected bus, the provider it's connected to. A provider bus is its own provider.
		/// Null for disconnected buses.
		/// </summary>
		private Bus[] _providerForBus;

		/// <summary>
		/// For each connected bus, the nominal voltage of the generator or transformer it is connected to.
		/// Generators and transformer output buses have the same voltage as they generate.
		/// </summary>
		private double?[] _nominalVoltage;

		/// <summary>
		/// For each connected bus, the number of lines that must be traversed to reach the provider
		/// 0 for disconnected buses.
		/// </summary>
		private int[] _distanceToProvider;

		/// <summary>
		/// One line from each cycle in the network. Neither endpoint is an upstream or
		/// downstream bus for the other endpoint. I.e., the bridges do not take part in the inter-bus (hierarchy)
		/// relations that we construct. You can go upstream from both of its endpoints and
		/// find either a common ancestor or reach two providers.
		/// </summary>
		private List<Line> _cycleBridges;

		/// <summary>
		/// List of transformers which, in the current configuration, do not have modes
		/// corresponding to its upstream and downstream lines.
		/// </summary>
		private List<Transformer> _invalidTransformerModes = new List<Transformer>();


		#endregion

		#region Contruction

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="network">The power network with lines and buses etc.</param>
		/// <param name="switchSettings">The switch settings that modify the network.</param>
		public NetworkConfiguration(PowerNetwork network, SwitchSettings switchSettings)
		{
			SwitchSettings = switchSettings;
			Network = network;
			ClearInterBusRelations();
		}

		/// <summary>
		/// Copy constructor. Switch settings are cloned, while
		/// the network is only referred to (as it is assumed to never change).
		/// </summary>
		public NetworkConfiguration(NetworkConfiguration other)
			: this(other.Network, other.SwitchSettings.Clone())
		{
		}

		/// <summary>
		/// Creates a configuration for the given network where all switches are open
		/// </summary>
		public static NetworkConfiguration AllOpen(PowerNetwork network)
		{
			var allOpen = new SwitchSettings(network, (Line l) => true);

			return new NetworkConfiguration(network, allOpen);
		}

		/// <summary>
		/// Creates a configuration for the given network where all switches are closed
		/// </summary>
		public static NetworkConfiguration AllClosed(PowerNetwork network)
		{
			return new NetworkConfiguration(network, new SwitchSettings(network));
		}

		#endregion

		#region Public methods

		/// <summary>
		/// Sets the switch state of line by calling the similar function in the contained <see cref="SwitchSettings"/>.
		/// Flags the need to compute new inter-bus-relationships, and notes that the switch was opened or closed.
		/// </summary>
		/// <param name="line">Line to switch</param>
		/// <param name="open">True if line should be open, false if line should be closed</param>
		/// <returns>True if the switch setting changed, false if it was already at the indicated value</returns>
		public bool SetSwitch(Line line, bool open)
		{
			bool switchChanged = SwitchSettings.SetSwitch(line, open);

			if (switchChanged)
			{
				if (open)
				{
					if (_switchesThatWereClosed.Contains(line))
						_switchesThatWereClosed.Remove(line);
					else
						_switchesThatWereOpened.Add((line, null));
				}
				else
				{
					if (_switchesThatWereOpened.Any(l => l.switchLine == line))
						_switchesThatWereOpened.RemoveAll(l => l.switchLine == line);
					else
						_switchesThatWereClosed.Add(line);
				}
				_relationsAreUpToDate = false;
			}

			return switchChanged;
		}

		/// <summary>
		/// Finds a set of path of lines which violates radiality with the current switch settings.
		/// In order for the solution to be radial at least one of these lines must
		/// be opened. 
		/// 
		/// Which set of lines is returned is not guaranteed.
		/// </summary>
		/// <returns>The path unumerable, or null if there are no conflicts, along with a cycle bridge that is on that path (also null if no cycle).</returns>
		internal (IEnumerable<Line>, Line) FindRadialityConflict()
		{
			IEnumerable<Line> cycleBridges = CycleBridges;
			if (cycleBridges.Any())
			{
				Line cycleBridge = cycleBridges.First();
				IEnumerable<Line> path = FindCycleWithBridge(cycleBridge).Select(l => l.Line);
#if DEBUG
				if (path.All(l => !l.IsSwitchable))
					throw new System.Exception("PgoSolution.FindRadialityConflict: A conflict cycle was found, in which no edges are switchable: " + path.Select(l => l.ToString()));
#endif
				return (path, cycleBridge);
			}
			else
				return (null, null);
		}

		/// <summary>
		/// Opens a switch with the intent of breaking a cycle associated with the given <paramref name="cycleBridge"/>.
		/// Calling SetSwitch in in the contained <see cref="SwitchSettings"/>.
		/// Flags the need to compute new inter-bus-relationships, and notes which switch was opened.
		/// </summary>
		/// <param name="line">Line to switch</param>
		/// <param name="cycleBridge">The cycle bridge.</param>
		public void OpenSwitchForBreakingCycleWithBridge(Line line, Line cycleBridge)
		{
			if (SwitchSettings.SetSwitch(line, true))
			{
				_switchesThatWereOpened.Add((line, cycleBridge));
				_relationsAreUpToDate = false;
			}
		}

		/// <summary>
		/// Returns the buses that are immediate neighbours of the given bus,
		/// based on the current switch settings.
		/// </summary>
		internal IEnumerable<Bus> SwitchedNeighbours(Bus bus)
		{
			Bus parent = UpstreamBus(bus);
			if (parent != null)
				return DownstreamBuses(bus).Concat(new List<Bus> { parent });
			else
				return DownstreamBuses(bus);
		}

		/// <summary>
		/// Returns the end of <paramref name="line"/> that is either equal to 
		/// <paramref name="upstreamBus"/> or has <paramref name="upstreamBus"/> in its upstream
		/// path.
		/// 
		/// This method only works correctly when <paramref name="line"/> is open or a bridge, and
		/// <paramref name="upstreamBus"/> does not lie upstream of both ends.
		/// </summary>
		internal Bus EndAtOrDownstreamOf(Line line, Bus upstreamBus)
		{
			if (line.Node1 == upstreamBus || UpstreamBuses(line.Node1).Contains(upstreamBus))
				return line.Node1;
			else
				return line.Node2;
		}

		/// <summary>
		/// Enumerates the buses that are on the path from the given bus to 
		/// the supplying provider. This includes the provider, but excludes 
		/// <paramref name="bus"/> itself.
		/// </summary>
		private IEnumerable<Bus> UpstreamBuses(Bus bus)
		{
			UpdateInterBusRelations();

			while (true)
			{
				if (bus.IsProvider)
					yield break;

				bus = _upstreamBus[bus.Index];
				yield return bus;
			}
		}

		/// <summary>
		/// Returns true if the <paramref name="line"/> is open, false if not.
		/// </summary>
		public bool IsOpen(Line line) => SwitchSettings.IsOpen(line);

		/// <summary>
		/// Enumerates the downstream buses of the given <paramref name="bus"/>
		/// </summary>
		public IEnumerable<Bus> DownstreamBuses(Bus bus)
		{
			UpdateInterBusRelations();

			return _downstreamBuses[bus.Index];
		}

		/// <summary>
		/// Enumerates the consumer buses in the subtree below <paramref name="line"/>.
		/// </summary>
		/// <param name="line"></param>
		/// <param name="stopAt">If not null, and in the subtree below <paramref name="line"/>,
		///   the enumeration excludes any bus in the subtree below this line</param>
		/// <param name="stopAt2">If not null, and in the subtree below <paramref name="line"/>,
		///   the enumeration excludes any bus in the subtree below this line</param>
		internal List<Bus> ConsumersBelow(Line line, Line stopAt = null, Line stopAt2 = null)
		{
			List<Bus> result = new List<Bus>();
			Stack<Bus> stack = new Stack<Bus>();

			stack.Push(DownstreamEnd(line));

			while (stack.Count > 0)
			{
				var bus = stack.Pop();
				if (bus.IsConsumer)
					result.Add(bus);

				foreach (var downstreamBus in _downstreamBuses[bus.Index])
				{
					var downstreamLine = _upstreamLine[downstreamBus.Index];

					if (ReferenceEquals(downstreamLine, stopAt))
						continue;
					if (ReferenceEquals(downstreamLine, stopAt2))
						continue;

					stack.Push(downstreamBus);
				}
			}

			return result;
		}

		/// <summary>
		/// Enumerates the lines leading downstream from the given <paramref name="bus"/>
		/// </summary>
		/// <remarks>
		/// This member is exposed as a List rather than IEnumerable, in order to avoid a memory
		/// allocation when it's used in a foreach(), since List has a struct enumerator.
		/// </remarks>
		public List<Line> DownstreamLines(Bus bus)
		{
			UpdateInterBusRelations();

			return _downstreamLines[bus.Index];
		}

		/// <summary>
		/// Returns the bus upstream of the given <paramref name="bus"/>, or null
		/// if <paramref name="bus"/> is a provider or is disconnected.
		/// </summary>
		public Bus UpstreamBus(Bus bus)
		{
			UpdateInterBusRelations();

			return _upstreamBus[bus.Index];
		}

		/// <summary>
		/// Returns the end of the given bus that is closer to a provider.
		/// </summary>
		public Bus UpstreamEnd(Line line)
		{
			UpdateInterBusRelations();

			if (line == _upstreamLine[line.Node1.Index])
				return line.Node2;

			if (line == _upstreamLine[line.Node2.Index])
				return line.Node1;

			throw new ArgumentException("The given line has no upstream end");
		}

		/// <summary>
		/// Returns the end of the given bus that is farther from a provider.
		/// </summary>
		public Bus DownstreamEnd(Line line)
		{
			UpdateInterBusRelations();

			if (line == _upstreamLine[line.Node1.Index])
				return line.Node1;

			if (line == _upstreamLine[line.Node2.Index])
				return line.Node2;

			throw new ArgumentException("The given line has no downstream end");
		}

		/// <summary>
		/// Returns the provider to which <paramref name="bus"/> is connected, or
		/// null if it is disconnected.
		/// </summary>
		public Bus ProviderForBus(Bus bus)
		{
			UpdateInterBusRelations();

			return _providerForBus[bus.Index];
		}

		/// <summary>
		/// The nominal voltage of the bus in the current configuration. This is the generator voltage of the provider that the bus is connected to.
		/// </summary>
		/// <param name="bus"></param>
		/// <returns></returns>
		public double? NominalVoltage(Bus bus)
		{
			UpdateInterBusRelations();

			return _nominalVoltage[bus.Index];
		}

		/// <summary>
		/// Returns the transformer mode that applies to the given line, or null if the line is not a 
		/// transformer output line in the configuration or there is no corresponding mode.
		/// </summary>
		public Transformer.Mode TransformerModeForOutputLine(Line line)
		{
			if (!line.IsTransformerConnection)
				return null;

			var mode = line.Transformer.Modes.SingleOrDefault(
					m => m.InputBus == _upstreamBus[line.Transformer.Bus.Index]
						&& m.OutputBus == line.OtherEnd(line.Transformer.Bus));
			return mode;
		}

		/// <summary>
		/// Returns the number of lines in the path between the <paramref name="bus"/> and its
		/// connected provider.
		/// If the bus is disconnected, returns <see cref="int.MaxValue"/>.
		/// </summary>
		public int DistanceToProvider(Bus bus)
		{
			if (ProviderForBus(bus) == null)
				return int.MaxValue;

			return _distanceToProvider[bus.Index];
		}

		/// <summary>
		/// Returns the number of lines in the path between the <paramref name="line"/> and its
		/// connected provider.
		/// If the line is disconnected, returns <see cref="int.MaxValue"/>.
		/// </summary>
		public int DistanceToProvider(Line line) => Math.Min(DistanceToProvider(line.Node1), DistanceToProvider(line.Node2));

		/// <summary>
		/// Returns the upstream line for the given <paramref name="bus"/>, i.e. the one
		/// connecting it to a provider, or null if the bus is a provider
		/// </summary>
		public Line UpstreamLine(Bus bus)
		{
			UpdateInterBusRelations();

			return _upstreamLine[bus.Index];
		}

		/// <summary>
		/// Returns the upstream line for the given <paramref name="bus"/>, i.e. the one
		/// connecting it to a provider, in the direction toward the provider.
		/// </summary>
		public DirectedLine UpstreamDirectedLine(Bus bus)
		{
			var line = UpstreamLine(bus);

			if (line.Node1 == bus)
				return new DirectedLine(line, LineDirection.Forward);
			else
				return new DirectedLine(line, LineDirection.Reverse);
		}

		/// <summary>
		/// Returns the line that is adjacent to and downstream of <paramref name="bus"/>, 
		/// and upstream of <paramref name="otherBus"/>.
		/// </summary>
		public Line DownstreamLineToward(Bus bus, Bus otherBus)
		{
			return DownstreamLines(bus).Single(line => IsAncestor(line, otherBus));
		}

		/// <summary>
		/// Returns the directed path to the <paramref name="bus"/> from its connected
		/// provider
		/// </summary>
		public IEnumerable<DirectedLine> PathFromProvider(Bus bus)
		{
			return PathToProvider(bus).ReverseDirectedPath();
		}

		/// <summary>
		/// Returns the directed path from the <paramref name="bus"/> to its connected
		/// provider
		/// </summary>
		public IEnumerable<DirectedLine> PathToProvider(Bus bus)
		{
			while (!bus.IsProvider)
			{
				yield return UpstreamDirectedLine(bus);
				bus = UpstreamBus(bus);
			}
		}

		/// <summary>
		/// Enumerates the lines that connect <paramref name="bus"/> to its connected
		/// provider
		/// </summary>
		public IEnumerable<Line> LinesToProvider(Bus bus) => PathToProvider(bus).Select(dl => dl.Line);

		/// <summary>
		/// The number of buses in the tree of the given provider bus.
		/// Assumes the tree is radial.
		/// </summary>
		/// <param name="bus"></param>
		/// <returns></returns>
		public int GetNumberOfBusesInTree(Bus bus)
		{
			if (!bus.IsProvider)
				throw new ArgumentException("Must be a provider");

			if (!IsRadial)
				throw new Exception("GetNumberOfBusesInTree: The network is not radial");

			return _providerForBus.Count(b => b == bus);
		}

		/// <summary>
		/// Sets each switch to open or closed with 50% probability
		/// </summary>
		/// <param name="random">The random generator to use</param>
		public void Randomize(Random random)
		{
			foreach (var line in Network.SwitchableLines)
				SetSwitch(line, random.Next() % 2 == 1);
		}

		/// <summary>
		/// Opens/closes switches as necessary to make transformer modes valid.
		/// </summary>
		public void MakeTransformersUseValidModes(bool throwOnFail = true, StopCriterion stopCriterion = null)
		{
			if (!IsRadial)
				throw new Exception("Internal error");

			while (HasTransformersUsingMissingModes)
			{
				if (stopCriterion != null && stopCriterion.IsTriggered)
					return;

				// Find an invalid transformer that does not have any other invalid transformers as parents.
				var misconfiguredTransformer = FindUndominatedInvalidTransformer();
				if (misconfiguredTransformer == null)
				{
					// There were no *connected* invalid transformers so we have an unexpected inconsistency.
					throw new Exception("Internal error");
				}

				// Find the closest switch upstream from the misconfigured transformer.
				var switchToOpen = FindSwitchToOpenToMakeTransformerModeValid(misconfiguredTransformer.Bus);
				if (switchToOpen == null)
				{
					if (!throwOnFail) return;
					throw new Exception($"Transformer {misconfiguredTransformer.Bus} cannot be connected with a valid mode.");
				}

				// Find a switch that we can close to connect this transformer with a correct mode.
				var switchToClose = FindSwitchToCloseToMakeTransformerModeValid(misconfiguredTransformer.Bus, DownstreamEnd(switchToOpen));
				if (switchToClose == null)
				{
					if (!throwOnFail) return;
					throw new Exception($"Transformer {misconfiguredTransformer.Bus} cannot be connected with a valid mode.");
				}

				SetSwitch(switchToOpen, open: true);
				SetSwitch(switchToClose, open: false);

				if (!IsRadial)
					throw new Exception("Internal error");
			}
		}

		// Find a closed switchable line s.t.
		//  * the line is upstream from the given `bus`.
		//  * all transformers on the path from the `bus` to the closed switch must have valid input modes when
		//    the transformer's downstream line on that path is instead used as upstream line.
		// or return null if no such line exists.
		private Line FindSwitchToOpenToMakeTransformerModeValid(Bus bus)
		{
			while (!bus.IsProvider)
			{
				var line = UpstreamLine(bus);
				if (line.IsSwitchable)
				{
					return line;
				}

				bus = UpstreamBus(bus);

				// Any transformers on the way from the start of the search to the switch to open, must be 
				// valid to turn around (change its mode).
				if (bus.IsTransformer && !bus.Transformer.HasValidModesForInputLine(line))
				{
					return null;
				}
			}
			return null;
		}

		// Find an open switchable line s.t.:
		//  * the line is downstream from the given `startBus` bus
		//  * the line is not downstream from the given `forbiddenAncestor`
		//  * all transformers on the path from the `startBus` to the open switchable line must have valid input modes when that switch is closed.
		// or return null if no such line exists.
		private Line FindSwitchToCloseToMakeTransformerModeValid(Bus startBus, Bus forbiddenAncestor)
		{
			// Search downstream from this bus.
			var stack = new Stack<Bus>(new[] { startBus });

			// Find an open (unconnected) switch which is not under the switch to be closed (closestSwitch).
			while (stack.Count > 0)
			{
				var bus = stack.Pop();

				foreach (var incidentLine in bus.IncidentLines)
				{
					var isOpen = incidentLine.IsSwitchable && IsOpen(incidentLine);

					if (!isOpen && UpstreamEnd(incidentLine) == bus)
					{
						var downstreamLine = incidentLine;
						// Any transformers on the way from the start of the search to the switch to close, must be 
						// valid to turn around (change its mode).
						if (bus.IsTransformer && !bus.Transformer.HasValidModesForInputLine(downstreamLine))
						{
							continue;
						}

						stack.Push(incidentLine.OtherEnd(bus));
					}

					// The line we are searching for is an open switch.
					if (isOpen)
					{
						// We cannot connect to a tree that is hanging from below the switch we are opening.

						if (!IsAncestorOrSame(forbiddenAncestor, incidentLine.OtherEnd(bus)))
						{
							return incidentLine;
						}
					}
				}
			}

			return null;
		}

		// Find an invalid transformer that does not have any other invalid transformers as parents.
		private Transformer FindUndominatedInvalidTransformer()
		{
			if (!HasTransformersUsingMissingModes) throw new Exception("No invalid transformers.");
			var stack = new Stack<Bus>(Network.Providers);
			while (stack.Count > 0)
			{
				var bus = stack.Pop();
				foreach (var downstreamLine in DownstreamLines(bus))
				{
					if (bus.IsTransformer)
					{
						if (TransformerModeForOutputLine(downstreamLine) == null)
						{
							return bus.Transformer;
						}
					}

					stack.Push(downstreamLine.OtherEnd(bus));
				}
			}

			return null;
		}

		/// <summary>
		/// Open/close switches as necessary to make the network radial and make transformer modes valid.
		/// </summary>
		/// <param name="r"></param>
		/// <param name="throwOnFail"></param>
		/// <param name="stopCriterion"></param>
		public void MakeRadialFlowPossible(Random r = null, bool throwOnFail = true, StopCriterion stopCriterion = null)
		{
			MakeRadial(r, throwOnFail, stopCriterion);
			if (IsRadial)
			{
				MakeTransformersUseValidModes(throwOnFail, stopCriterion);
			}
		}

		/// <summary>
		/// Opens/closes switches as necessary to make the network radial.
		/// Throws an exception if the network cannot be made radial.
		/// The algorithm is randomized to avoid getting stuck in a cycle.
		/// </summary>
		/// <param name="r">The random generator to use. If null, one is created.</param>
		/// <param name="throwOnFail">If true, the algorithm exits instead of throwing an exception
		///   if it detects that the network cannot be made radial.
		///   If on exit, <see cref="HasCycles"/> is true, the configuration contains an unremovable cycle.
		///   Otherwise, the buses that are not connected to a provider in the configuration, cannot be connected.
		///   </param>
		/// <param name="stopCriterion">The alrgorithm stops if this criterion is triggered</param>
		public void MakeRadial(Random r = null, bool throwOnFail = true, StopCriterion stopCriterion = null)
		{
			r = r ?? RandomCreator.GetRandomGenerator();

			while (!IsRadial)
			{
				if (stopCriterion != null && stopCriterion.IsTriggered)
					return;

				// Find all switches between one connected and one
				// unconnected bus

				var switchesToClose = ConnectedComponentBoundary.TakeEvery(1 + r.Next(5)).ToList();

				// Close some

				foreach (var s in switchesToClose)
					SetSwitch(s, false);

				// Find one switch from each cycle

				var switchesToOpen = BestSwitchFromEachCycle.TakeEvery(1 + r.Next(5)).ToList();

				// Open some

				foreach (var s in switchesToOpen)
				{
					if (s is null)
					{
						if (throwOnFail)
							throw new Exception("The network has a cycle with no switchable line");
						else
							return;
					}
					SetSwitch(s, true);
				}

				if (switchesToClose.Count + switchesToOpen.Count == 0)
				{
					if (throwOnFail)
						throw new InvalidOperationException("Failed to make the configuration radial");
					else
						return;
				}
			}
		}

		/// <summary>
		/// Returns a new <see cref="NetworkConfiguration"/> based on this one. Switch settings are cloned, while
		/// the network is only referred to (as it is assumed to never change).
		/// </summary>
		/// <returns></returns>
		internal NetworkConfiguration Clone()
		{
			return new NetworkConfiguration(this);
		}

		/// <summary>
		/// Identifies the cycle that will arise if the given switch is closed. The result is a connected
		/// sequence of directed links.
		/// 
		/// If the cycle involves two providers, it starts at one provider and ends at the other.
		/// If not, it starts and ends at the bus in the cycle that is closest to a provider.
		/// </summary>
		/// <param name="switchToClose">The switch to close</param>
		public IEnumerable<DirectedLine> FindCycleWith(Line switchToClose)
		{
			UpdateInterBusRelations();

			if (!SwitchSettings.IsOpen(switchToClose))
				throw new ArgumentException("The switch is not open");

			return CycleWith(switchToClose);
		}

		/// <summary>
		/// Identifies the cycle that contains the given line, which must
		/// be one of <see cref="CycleBridges"/>.
		/// 
		/// The result is as for <see cref="FindCycleWith"/>.
		/// </summary>
		public IEnumerable<DirectedLine> FindCycleWithBridge(Line bridge)
		{
			UpdateInterBusRelations();

			if (!_cycleBridges.Contains(bridge))
				throw new ArgumentException("The line is not a bridge");

			return CycleWith(bridge);
		}

		/// <summary>
		/// Returns the directed path from <paramref name="node1"/> to <paramref name="node2"/>.
		/// One node must be an upstream node of the other (or they must be the same node).
		/// </summary>
		public IEnumerable<DirectedLine> PathBetween(Bus node1, Bus node2)
		{
			if (DistanceToProvider(node1) < DistanceToProvider(node2))
				// node1 is upstream of node2
				return PathBetween(node2, node1).ReverseDirectedPath();

			// node2 is upstream of node1

			List<DirectedLine> path = new List<DirectedLine>();
			while (node1 != node2)
			{
				path.Add(UpstreamDirectedLine(node1));
				node1 = UpstreamBus(node1);

				if (node1 == null)
					throw new ArgumentException("Neither node is upstream of the other");
			}

			return path;
		}

		/// <summary>
		/// Returns the node that is upstream of, or equal to, both <paramref name="node1"/> and <paramref name="node2"/>, 
		/// and that is closest to them among such nodes.
		/// </summary>
		public Bus CommonAncestor(Bus node1, Bus node2)
		{
			UpdateInterBusRelations();

			while (_distanceToProvider[node1.Index] > _distanceToProvider[node2.Index])
				node1 = _upstreamBus[node1.Index];

			while (_distanceToProvider[node2.Index] > _distanceToProvider[node1.Index])
				node2 = _upstreamBus[node2.Index];

			// Now the nodes have the same distance to the provider

			while (node1 != node2)
			{
				node1 = _upstreamBus[node1.Index];
				node2 = _upstreamBus[node2.Index];
			}

			return node1;
		}

		/// <summary>
		/// Returns true if <paramref name="ancestorCandidate"/> is an ancestor of
		/// <paramref name="child"/>, or if they are the same.
		/// </summary>
		public bool IsAncestorOrSame(Bus ancestorCandidate, Bus child) => CommonAncestor(ancestorCandidate, child) == ancestorCandidate;

		/// <summary>
		/// Returns true if <paramref name="ancestorCandidate"/> is an ancestor of
		/// <paramref name="child"/>. 
		/// </summary>
		public bool IsAncestor(Line ancestorCandidate, Line child) => IsAncestorOrSame(DownstreamEnd(ancestorCandidate), UpstreamEnd(child));

		/// <summary>
		/// Returns true if <paramref name="ancestorCandidate"/> is an ancestor of
		/// <paramref name="child"/>. A line is an ancestor of its downstream bus, but not of its
		/// upstream bus.
		/// </summary>
		public bool IsAncestor(Line ancestorCandidate, Bus child) => IsAncestorOrSame(DownstreamEnd(ancestorCandidate), child);

		/// <summary>
		/// Returns true if <paramref name="ancestorCandidate"/> is an ancestor of one end
		/// of <paramref name="line"/>, but not of the other end.
		/// Equivalently, returns true if <paramref name="line"/> links the subtree supplied through <paramref name="ancestorCandidate"/>
		/// with a part of the network not supplied through <paramref name="ancestorCandidate"/>.
		/// </summary>
		public bool IsAncestorOfOneEnd(Line ancestorCandidate, Line line)
			=> IsAncestor(ancestorCandidate, line.Node1) != IsAncestor(ancestorCandidate, line.Node2);

		/// <summary>
		/// Updates interbus relations for the given <paramref name="bus"/> and below.
		/// Assumes a sub tree under the <paramref name="bus"/>.
		/// </summary>
		/// <param name="bus"></param>
		/// <param name="upstreamLine"></param>
		/// <param name="provider"></param>
		/// <param name="distanceToProvider"></param>
		public void UpdateRelationsInBranchOf(Bus bus, Line upstreamLine, Bus provider, int distanceToProvider)
		{
			// Data structure for iteration, instead of recursion.
			// (bus, upstream line, distance to provider)
			Stack<(Bus, Line, int)> busesToProcess = new Stack<(Bus, Line, int)>();

			busesToProcess.Push((bus, upstreamLine, distanceToProvider));

			while (busesToProcess.Count > 0)
			{
				(bus, upstreamLine, distanceToProvider) = busesToProcess.Pop();

				if (_providerForBus[bus.Index] != null || (bus.IsProvider && bus != provider))
				{
					// We have reached a bus that already has an assigned upstream bus, or is a provider that is
					// different from the one we started from.
					// This means that the line is part of a cycle. 
					// Make it a bridge, and pretend it's not there

					if (!_cycleBridges.Contains(upstreamLine))
						_cycleBridges.Add(upstreamLine);

					continue;
				}

				// Set data for this bus
				SetParentChildRelationship(bus, upstreamLine, provider, distanceToProvider);

				// Push neighbor buses to the stack
				foreach (var line in PresentLinesAt(bus))
				{
					var downstreamBus = line.OtherEnd(bus);

					// Don't go along the upstream line
					if (ReferenceEquals(line, upstreamLine))
						continue;

					if (bus.IsTransformer && TransformerModeForOutputLine(line) == null)
					{
						// Missing transformer mode.
						_invalidTransformerModes.Add(bus.Transformer);
					}

					busesToProcess.Push((downstreamBus, line, distanceToProvider + 1));
				}
			}
		}

		/// <summary>
		/// Sets the parent child relationship between the given buses.
		/// </summary>
		/// <param name="provider">The provider for the child</param>
		/// <param name="child">The child</param>
		/// <param name="upstreamLine">The line from the parent node. Set to null for providers.</param>
		/// <param name="distToChild">The distance from <paramref name="provider"/> to <paramref name="child"/> in the relationship graph.</param>
		private void SetParentChildRelationship(Bus child, Line upstreamLine, Bus provider, int distToChild)
		{
			_providerForBus[child.Index] = provider;
			_distanceToProvider[child.Index] = distToChild;
			_upstreamLine[child.Index] = upstreamLine;
			var parent = upstreamLine?.OtherEnd(child);
			_upstreamBus[child.Index] = parent;

			if (parent != null)
			{
				_downstreamBuses[parent.Index].Add(child);
				_downstreamLines[parent.Index].Add(upstreamLine);

				// Nominal voltage modified by transformer
				if (upstreamLine != null && _nominalVoltage[parent.Index] != null
						&& TransformerModeForOutputLine(upstreamLine) is Transformer.Mode mode)
				{
					_nominalVoltage[child.Index] = mode.OutputVoltage(new Complex(_nominalVoltage[parent.Index].Value, 0)).Real;
				}
				else
				{
					_nominalVoltage[child.Index] = _nominalVoltage[parent.Index];
				}
			}
			else
			{
				if (child.IsProvider)
				{
					_nominalVoltage[child.Index] = child.GeneratorVoltage;
				}
			}
		}

		/// <summary>
		/// Visits each bus in a subtree of the configuration and applies one or two actions
		/// to each bus (including the <paramref name="startNode"/>)
		/// </summary>
		/// <param name="startNode">The root of the subtree to visit</param>
		/// <param name="topDownAction">If not null, this action is applied to each bus BEFORE being applied to its children</param>
		/// <param name="bottomUpAction">If not null, this action is applied to each bus AFTER being applied to its children</param>
		public void Traverse(Bus startNode, Action<Bus> topDownAction = null, Action<Bus> bottomUpAction = null)
		{
			// Initial action stack: Go down from start node, then return up to start node
			Stack<(Bus, bool)> stack = new Stack<(Bus, bool)>();
			stack.Push((startNode, false));
			stack.Push((startNode, true));

			while (stack.Count > 0)
			{
				// Execute the top action on the stack
				var (node, goingDown) = stack.Pop();

				if (goingDown)
					topDownAction?.Invoke(node);
				else
					bottomUpAction?.Invoke(node);

				if (goingDown)
				{
					// Before returning back up, process the subtree under this node
					foreach (var child in DownstreamBuses(node))
					{
						if (bottomUpAction != null)
							stack.Push((child, false));
						stack.Push((child, true));
					}
				}
			}
		}

		#endregion

		#region Private methods

		/// <summary>
		/// Returns a path that consists of just the given line in the forward direction
		/// </summary>
		private IEnumerable<DirectedLine> SingletonPath(Line line)
		{
			yield return new DirectedLine(line, LineDirection.Forward);
		}

		/// <summary>
		/// Computes the upstream/downstream relationships between buses. If this has already been computed,
		/// the function does nothing. If only minor changes have been made, e.g. by applying a move, or opening a switch,
		/// a minimum update is made. Otherwise, all relations are computed from scratch.
		/// The network does not have to be radial, but if not then not all relationships are properly 
		/// calculated, but the function identifies a "bridge" line (not necessarily switchable) in each cycle.
		/// </summary>
		private void UpdateInterBusRelations()
		{
			if (!_relationsAreUpToDate)
			{
				//If no relations were ever computed, we do this from scratch
				if (Network.Providers.All(p => _downstreamLines[p.Index].Count() == 0))
					ComputeInterBusRelations();
				else
				{
					//Update as economically as we can

					//Setting the flag to true during the update, to avoid recursive updating when we call relation-related properties and functions.
					_relationsAreUpToDate = true;

					//Determine what happened:
					if (_switchesThatWereClosed.CountIs(1) && !_switchesThatWereOpened.Any())
					{
						//throw new NotImplementedException("Not uimplemented net config update for <One closed switch>");
						ComputeInterBusRelations();
					}
					else if (_switchesThatWereOpened.CountIs(1) && !_switchesThatWereClosed.Any())
					{
						//This is the situation encountered for each new node in the branch and bound construction search.
						(Line sw, Line bridge) = _switchesThatWereOpened.Single();

						//If no bridge was given, for whatever reason, we must update the tree from both ends of the opened
						//switch (the precence of a known bridge would have let us update from a lower point in the tree.
						if (bridge == null)
						{
							//throw new NotImplementedException("We don't support opening a single switch without supplying a bridge for the cycle that the opening is supposed to break");
							//							UpdateRelationsForSingleOpenedSwitch(sw, stopOnCycleDetection);
							ComputeInterBusRelations();
						}
						else
						{
							//We treat the bridge as an edge that was closed
							//Remove the bridge from the list of bridges
							_cycleBridges.Remove(bridge);

							//In the special case where the bridge is the same line as the switch, opening it 
							//will not change any relations, and we don't do anything.
							if (sw != bridge)
							{
								//Updates the relations tree
								UpdateRelationForOpenedAndClosedSwitchPair(sw, bridge);
							}
						}
					}
					else if (_switchesThatWereClosed.CountIs(1) && _switchesThatWereOpened.CountIs(1))
					{
						//This is the situation encountered when applying a switch swap move.
						//But it can also happen in other situations.
						(Line openedSwitch, Line bridge) = _switchesThatWereOpened.Single();
						Line closedSwitch = _switchesThatWereClosed.Single();

						//Updates the relations tree
						UpdateRelationForOpenedAndClosedSwitchPair(openedSwitch, closedSwitch);
					}
					else
					{
						//For all other cases we update the entire tree from top.
						ComputeInterBusRelations();
					}
				}

				if (Network.Buses.Any(b => _providerForBus[b.Index] == null))
					_isConnected = false;

				//Update complete
				_relationsAreUpToDate = true;
				_switchesThatWereClosed.Clear();
				_switchesThatWereOpened.Clear();

			}
		}

		/// <summary>
		/// Common worker for <see cref="FindCycleWith"/> and
		/// <see cref="FindCycleWithBridge"/>. Does not update inter-bus
		/// relations.
		/// Note that the given line is assumed to be a bridge, and so that
		/// the end points of the line has nor relatio (in terms of interbus relations).
		/// </summary>
		private IEnumerable<DirectedLine> CycleWith(Line bridge)
		{
			var node1 = bridge.Node1;
			var node2 = bridge.Node2;

			if (_providerForBus[node1.Index] != _providerForBus[node2.Index])
			{
				// Cycle between two providers
				return PathFromProvider(node1)
					.Concat(SingletonPath(bridge))
					.Concat(PathToProvider(node2));
			}

			var commonParent = CommonAncestor(node1, node2);

			return PathBetween(commonParent, node1)
					.Concat(SingletonPath(bridge))
					.Concat(PathBetween(node2, commonParent));
		}

		/// <summary>
		/// Recalculates upstream/downstream relations, provider and distance to it for every bus that
		/// is connected to a provider.
		/// Also identifies a line (not necessarily switchable) in each cycle.
		/// </summary>
		private void ComputeInterBusRelations()
		{
			// Initialize data structures
			ClearInterBusRelations();

			// Explore network from each provider
			foreach (var provider in Network.Providers)
			{
				UpdateRelationsInBranchOf(provider, null, provider, 0);
			}

			if (Network.Buses.Any(b => _providerForBus[b.Index] == null))
				_isConnected = false;

			return;
		}

		/// <summary>
		/// Updates the tree for the situation where one switch has been opened, and another one closed (or a previous 
		/// cycle bridge that is no longer a bridge, and therefore included as a closed arc).
		/// in the relations.
		/// Assumes that no other switch changes were made.
		/// </summary>
		/// <param name="openedSwitch">The switch that was opened</param>
		/// <param name="includedEdge">The switch that was closed, or the cycle bridge that is no longer a bridge (does not need to be switchable).</param>
		private void UpdateRelationForOpenedAndClosedSwitchPair(Line openedSwitch, Line includedEdge)
		{
			//The parent node of the switch that was opened
			Bus switchParent = UpstreamEnd(openedSwitch);
			Bus switchChild = openedSwitch.OtherEnd(switchParent);

			//Clear relations below the switch
			ClearInterBusRelationsBelow(switchChild);

			// Unregister lines that no longer are bridges
			_cycleBridges = _cycleBridges.Where(b => _providerForBus[b.Node1.Index] != null && _providerForBus[b.Node2.Index] != null).ToList();

			// Remove invalid transformer modes in the tree.
			_invalidTransformerModes = _invalidTransformerModes.Where(tr => _providerForBus[tr.Bus.Index] != null).ToList();

			// Now, exactly one of the end nodes of the included edge should still have a registered parent
			// (or be a provider)
			Bus parentOnIncEdge = includedEdge.Endpoints.Single(b => b.IsProvider || _upstreamLine[b.Index] != null);
			Bus childOnIncEdge = includedEdge.OtherEnd(parentOnIncEdge);

			//Update tree below childOnBridge, clearing previous relations as we go.
			UpdateRelationsInBranchOf(childOnIncEdge, includedEdge, _providerForBus[parentOnIncEdge.Index], _distanceToProvider[parentOnIncEdge.Index] + 1);
		}

		/// <summary>
		/// Clears all interbus relations in the subtree hanging from the given bus.
		/// The bus and all downstream descendants are rendered orphans and child-less.
		/// 
		/// Does not clear cycle bridges connected to the subtree.
		/// </summary>
		private void ClearInterBusRelationsBelow(Bus bus)
		{
			// Stack of buses to make orphan
			Stack<Bus> busesToProcess = new Stack<Bus>();
			busesToProcess.Push(bus);

			while (busesToProcess.Any())
			{
				//Pop a bus, and remove its parent and child relations
				bus = busesToProcess.Pop();

				_distanceToProvider[bus.Index] = 0;
				_downstreamBuses[_upstreamBus[bus.Index].Index].Remove(bus);
				_downstreamLines[_upstreamBus[bus.Index].Index].Remove(_upstreamLine[bus.Index]);
				_upstreamBus[bus.Index] = null;
				_upstreamLine[bus.Index] = null;
				_providerForBus[bus.Index] = null;
				_nominalVoltage[bus.Index] = null;

				//Push any children onto the stack
				_downstreamBuses[bus.Index].Do(c =>
				{
					busesToProcess.Push(c);
				});
			}
		}

		/// <summary>
		/// Clears all interbus relations.
		/// </summary>
		private void ClearInterBusRelations()
		{
			_upstreamBus = new Bus[Network.BusIndexBound];
			_upstreamLine = new Line[Network.BusIndexBound];
			_providerForBus = new Bus[Network.BusIndexBound];
			_nominalVoltage = new double?[Network.BusIndexBound];
			_distanceToProvider = new int[Network.BusIndexBound];
			_cycleBridges = new List<Line>();
			_invalidTransformerModes = new List<Transformer>();
			_downstreamBuses = Enumerable.Range(0, Network.BusIndexBound).Select(_ => new List<Bus>()).ToArray();
			_downstreamLines = Enumerable.Range(0, Network.BusIndexBound).Select(_ => new List<Line>()).ToArray();
			_isConnected = true;
			_relationsAreUpToDate = !Network.Buses.Any();
			_switchesThatWereOpened = new List<(Line switchLine, Line cycleBridge)>();
			_switchesThatWereClosed = new List<Line>();
		}

		/// <summary>
		/// Returns a text description of each cycle, as a sequence of nodes. Assumes that topological relations are up 
		/// to date.
		/// </summary>
		/// <returns>A string with the desciption of all cycles, empty if there are no cycles.</returns>
		internal string GetCyclesDescriptionByNodeIDs()
		{
			Debug.Assert(_relationsAreUpToDate, "Topological realtions not up to date before calling GetCyclesDescriptionByNodeIDs.");

			StringWriter cycleDescription = new StringWriter();
			int cc = -1;

			foreach (var cycleBridge in CycleBridges)
			{
				//Choose a random bus for the first line
				//Behaviour depends on wether this is a cycle, or a path between two different providers
				bool isCycle = true;
				StringWriter switchDescription = new StringWriter();
				Bus stopBus = null;
				Bus currentBus = null;
				List<Line> cycleLines = FindCycleWithBridge(cycleBridge).Select(d => d.Line).ToList();
				if (cycleLines.First().Node1.IsProvider || cycleLines.First().Node2.IsProvider)
				{
					Debug.Assert(cycleLines.Last().Node2.IsProvider || cycleLines.Last().Node1.IsProvider);

					Bus firstProvider = cycleLines.First().Node1.IsProvider ? cycleLines.First().Node1 : cycleLines.First().Node2;
					Bus lastProvider = cycleLines.Last().Node1.IsProvider ? cycleLines.Last().Node1 : cycleLines.Last().Node2;
					if (firstProvider != lastProvider) //It can be a cycle in which the same provider occurs.
					{
						currentBus = firstProvider;
						stopBus = lastProvider;
						cycleDescription.WriteLine($"{++cc}: A path was found from provider bus {firstProvider.Name} to another provider bus {lastProvider.Name}, through the buses: {firstProvider.Name}, ");
						isCycle = false;
					}
				}
				if (isCycle)
				{
					stopBus = cycleLines.First().OtherEnd(cycleLines.First().CommonBusWith(cycleLines.Skip(1).First()));
					currentBus = stopBus;
					cycleDescription.WriteLine($"{++cc}: A cycle was found, through buses: {stopBus.Name}, ");
				}
				foreach (Line directedLine in cycleLines)
				{
					if (directedLine.IsSwitchable)
					{
						if (!SwitchSettings.IsClosed(directedLine))
							throw new Exception($"Cycle wrongly reported for configuration, including open switch {directedLine}");
						switchDescription.Write($"{directedLine.Name},");
					}
					Debug.Assert(directedLine.Node1 == currentBus || directedLine.Node2 == currentBus);
					Bus otherBus = directedLine.OtherEnd(currentBus);
					cycleDescription.Write($"{otherBus.Name}");
					if (otherBus == stopBus)
					{
						cycleDescription.WriteLine(".");
						switchDescription.WriteLine(".");
						break;
					}
					else
					{
						cycleDescription.Write(", ");
						currentBus = otherBus;
					}
				}
				cycleDescription.WriteLine($"\tThis involved the following closed switches: {switchDescription.ToString()}");
				cycleDescription.WriteLine();
			}
			return cycleDescription.ToString();
		}

		/// <summary>
		/// Returns a text description of each bus that is not connected with a provider. Assumes that topological relations are up 
		/// to date.
		/// </summary>
		/// <returns>A string with the desciption of all provider-less buses, empty if there are no such buses.</returns>
		internal string GetProviderLessBusesDescription()
		{
			Debug.Assert(_relationsAreUpToDate, "Topologal realtions not up to date before calling GetProviderLessBusesDescription.");
			string result = string.Empty;

			List<Bus> orphans = Network.Buses.Where(b => !b.IsProvider && _providerForBus[b.Index] == null).ToList();
			if (orphans.Any())
			{
				result += orphans.First().Name;
				orphans.Skip(1).Do(b => result += ", " + b.Name);
			}
			return result;
		}

		/// <summary>
		/// Copies the switch settings from the given configuration.
		/// </summary>
		/// <param name="config"></param>
		/// <param name="aggregator">The aggregation that gives the mapping between <see cref="Network"/>
		///   and the network of <paramref name="config"/></param>
		/// <param name="missingLineValue">Value to set for swtiches that are not present in the source network.</param>
		internal void CopySwitchSettingsFrom(NetworkConfiguration config, NetworkAggregation aggregator, Func<Line, bool> missingLineValue)
		{
			SwitchSettings.CopyFrom(config.SwitchSettings, Network, aggregator, missingLineValue);
			_relationsAreUpToDate = false;
		}


		/// <summary>
		/// Given a `switchToClose` and a `switchToOpen`, check if performing that change is legal wrt. 
		/// transformer modes.
		/// </summary>
		/// <param name="switchToClose"></param>
		/// <param name="switchToOpen"></param>
		/// <returns></returns>
		public bool SwappingSwitchesUsesValidTransformerModes(Line switchToClose, Line switchToOpen)
		{
			var startBus = switchToClose.Endpoints.First(node => IsAncestor(switchToOpen, node));
			var endBus = DownstreamEnd(switchToOpen);

			var bus = startBus;
			while (!bus.IsProvider && bus != endBus)
			{
				var line = UpstreamLine(bus);
				bus = UpstreamBus(bus);
				if (bus.IsTransformer && !bus.Transformer.HasValidModesForInputLine(line))
				{
					return false;
				}
			}

			return true;
		}

		#endregion
	}
}
