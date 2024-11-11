using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Xml.Xsl;
using Newtonsoft.Json.Linq;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// An aggregation of a power network. Provides references to the original and aggregated
	/// networks and translates between them.
	/// Also performs the actual aggregation.
	/// </summary>
	public class NetworkAggregation
	{
		/// <summary>
		/// The original network, before aggregation
		/// </summary>
		public PowerNetwork OriginalNetwork { get; }

		/// <summary>
		/// The network after aggregation
		/// </summary>
		public PowerNetwork AggregateNetwork { get; private set; }

		/// <summary>
		/// Enumerates all buses in the original network that are not included in the aggregate
		/// because they cannot be connected to a provider
		/// </summary>
		public IReadOnlyCollection<Bus> UnconnectedBuses { get; private set; }

		/// <summary>
		/// The options used for the current aggregation
		/// </summary>
		private Options _Options;

		/// <summary>
		/// Enumerates all lines in the original network that are not included in the aggregate
		/// because they cannot connect a provider to a consumer
		/// </summary>
		public IEnumerable<Line> DanglingLines => _danglingLines;

		/// <summary>
		/// For each line in the aggregate network that is the result of
		/// aggregation, information about how it was created
		/// </summary>
		private Dictionary<string, MergedLine> _merges = new Dictionary<string, MergedLine>();

		/// <summary>
		/// The lines in the original network that are not included in the aggregate
		/// because they cannot connect a provider to a consumer
		/// </summary>
		private List<Line> _danglingLines = new List<Line>();

		/// <summary>
		/// Creates and returns an aggregation that aggregates simple parallel and serial lines
		/// and eliminates disconnected components
		/// </summary>
		/// <param name="original">The network to aggregate</param>
		/// <param name="options">Options for aggregation. If not given, default options are used</param>
		public static NetworkAggregation MakeAcyclicAndConnected(PowerNetwork original, Options options = null)
		{
			NetworkAggregation aggregation = new NetworkAggregation(original);
			aggregation.MakeAcyclicAndConnected(options ?? new Options());

			return aggregation;
		}

		/// <summary>
		/// Creates and returns an aggregation that changes nothing: the aggregated
		/// network is a copy of the original
		/// </summary>
		/// <param name="original">The original network</param>
		public static NetworkAggregation MakeCopy(PowerNetwork original)
		{
			NetworkAggregation aggregation = new NetworkAggregation(original);
			aggregation.MakeCopy();

			return aggregation;
		}

		/// <summary>
		/// Returns the aggregated network if given the original network, and vice versa.
		/// </summary>
		public PowerNetwork OtherNetwork(PowerNetwork network)
		{
			if (network == AggregateNetwork)
				return OriginalNetwork;

			if (network == OriginalNetwork)
				return AggregateNetwork;

			throw new ArgumentException("Not one of my networks");
		}

		/// <summary>
		/// Returns information about how the given line, from the aggregate
		/// network, was derived from the original network
		/// </summary>
		public MergedLine MergeInfoFor(Line line)
		{
			if (_merges.TryGetValue(line.Name, out var merge))
				return merge;

			var originalLine = OriginalNetwork.GetLine(line.Name);

			return new MergedLine(originalLine.Forward());
		}

		/// <summary>
		/// Returns information about how the given line, from the aggregate
		/// network, was derived from the original network
		/// </summary>
		public DirectedMergedLine MergeInfoFor(DirectedLine line)
		{
			return new DirectedMergedLine(MergeInfoFor(line.Line), line.Direction);
		}

		/// <summary>
		/// Disaggregates a path of directed lines in the aggregated network into the
		/// equivalent path of lines in the original network.
		/// When there is parallel aggregation, the result contains one of the parallel
		/// lines (arbitrarily, by line names).
		/// </summary>
		public IEnumerable<DirectedLine> OneDisaggregatedPath(IEnumerable<DirectedLine> lines)
		{
			return lines.SelectMany(line => MergeInfoFor(line).OneDirectedPath);
		}

		/// <summary>
		/// Initializes an aggregation
		/// </summary>
		/// <param name="originalNetwork">The network to aggregate</param>
		private NetworkAggregation(PowerNetwork originalNetwork)
		{
			OriginalNetwork = originalNetwork;
		}

		#region Private methods

		/// <summary>
		/// Performs an aggregation that aggregates simple parallel and serial lines
		/// and eliminates disconnected components and dangling lines
		/// </summary>
		/// <param name="options">Options for the aggregation</param>
		private void MakeAcyclicAndConnected(Options options)
		{
			_Options = options;

			var (connectedNetwork, unConnectedBuses) = RemoveIsolatedComponents(OriginalNetwork);
			UnconnectedBuses = unConnectedBuses;

			var aggregateNetwork = connectedNetwork;
			int networkSize = aggregateNetwork.LineCount;

			// For each algorithm, and store the network size after the
			// algorithm was last run
			var lastSize = new Dictionary<Func<PowerNetwork, PowerNetwork>, int>();

			lastSize.Add(RemoveDanglingLines, networkSize + 1);
			lastSize.Add(AggregateParallelLines, networkSize + 1);
			lastSize.Add(AggregateSerialLines, networkSize + 1);

			// Apply each aggregation algorithm until no more aggregation happens.
			while (true)
			{
				// Find the algorithm that was run the longest ago
				var (algo, sizeWhenLastRun) = lastSize.ArgMax(kv => kv.Value);
				if (sizeWhenLastRun == networkSize)
					// All algorithms have nothing to do. Done.
					break;

				aggregateNetwork = algo.Invoke(aggregateNetwork);
				networkSize = aggregateNetwork.LineCount;
				lastSize[algo] = networkSize;
			}

			AggregateNetwork = aggregateNetwork;

			AggregateKileProperties();
			
			return;
		}

		/// <summary>
		/// Performs an aggregation that changes nothing: the aggregated
		/// network is a copy of the original
		/// </summary>
		private void MakeCopy()
		{
			PowerNetwork networkCopy = new PowerNetwork("CopyOf_" + OriginalNetwork.Name);

			UnconnectedBuses = new List<Bus>();
				
			// Copy all buses
			OriginalNetwork.Buses.Where(b => !b.IsTransformer).Do(b => AddBusLike(b, networkCopy));

			// Copy all lines
			OriginalNetwork.Lines.Where(l => !l.IsTransformerConnection).Do(l => AddLineLike(l, networkCopy));

			// Copy all transformers
			OriginalNetwork.PowerTransformers.Do(t => networkCopy.AddTransformerLike(t));

			AggregateNetwork = networkCopy;
		}

		/// <summary>
		/// Looks for dangling lines in the given network, and makes an aggregate network in which these
		/// are removed.
		/// A dangling line is one that ends in a connection bus with no other connected lines, or in
		/// a connection bus with only one other line, which is dangling.
		/// </summary>
		/// <param name="network">The original network</param>
		/// <returns>The new network</returns>
		private PowerNetwork RemoveDanglingLines(PowerNetwork network)
		{
			HashSet<Bus> busesToRemove = new HashSet<Bus>();
			HashSet<Line> linesToRemove = new HashSet<Line>();

			// Start from each bus connected to just a single line
			foreach (var startBus in network.Buses.Where(b => b.IncidentLines.CountIs(1)))
			{
				var bus = startBus;
				var line = bus.IncidentLines.Single();

				while (true)
				{
					if (! bus.IsConnection)
						// Cannot remove this line
						break;

					if (line.IsTransformerConnection)
						// Do not remove transformers, even if they're dangling
						break;

					// Record the bus and line to be removed

					busesToRemove.Add(bus);
					linesToRemove.Add(line);

					// Move up the chain

					bus = line.OtherEnd(bus);

					if (bus.IncidentLinesCount != 2)
						// End of dangling chain
						break;

					line = bus.IncidentLines.Except(line).Single();
				}
			}

			if (!busesToRemove.Any())
				return network;

			// Record removed lines

			_danglingLines.AddRange(linesToRemove.SelectMany(l => MergeInfoFor(l).Lines));

			// Create the reduced network

			return TrimNetwork(network, network.Name,
				keepLine: l => !linesToRemove.Contains(l),
				keepBus: b => !busesToRemove.Contains(b),
				keepTransformer: t => true
			);
		}

		/// <summary>
		/// Looks for parallel lines in the given network, and makes an aggregate network in which these
		/// are aggregated.
		/// </summary>
		/// <param name="network">The original network</param>
		/// <returns>The new network</returns>
		private PowerNetwork AggregateParallelLines(PowerNetwork network)
		{
			PowerNetwork aggregatedNetwork = new PowerNetwork("ParAggOf_" + network.Name);

			// Copy buses
			network.Buses.Where(b => !b.IsTransformer).Do(b => AddBusLike(b, aggregatedNetwork));

			HashSet<Line> replacedLines = new();

			foreach (var parallelLines in network.ParallelNonSwitchableLines.Where(kvp => !kvp.Any(l => l.IsTransformerConnection)))
			{
				var lines = parallelLines.OrderBy(l => l.Name).ToList();

				Bus startBus = lines.First().Node1;
				Bus endBus = lines.First().Node2;

				Complex impedance;
				double iMax;
				if (lines.All(l => l.Impedance != 0))
				{
					impedance = 1 / lines.ComplexSum(l => l.Admittance);
					iMax = lines.Min(l => l.IMax * (l.Impedance.Magnitude)) / impedance.Magnitude;
				}
				else
				{
					// One or more parallel lines have zero impedance. Then so does the aggregate
					impedance = 0;
					iMax = lines.Where(l => l.Impedance == 0).Sum(l => l.IMax);
				}
				double vMax = lines.Min(l => l.VMax); // TODO is this correct?
				string name = "[" + lines.Select(l => l.Name).Concatenate(" || ") + "]";

				// Add aggregated line
				var newLine = aggregatedNetwork.AddLine(startBus.Name, endBus.Name, impedance, iMax, vMax, false, 0, false, name: name);

				lines.Do(l => replacedLines.Add(l));

				RecordMerge(newLine, AggregateType.Parallel, lines.Select(l => l.InDirectionFrom(startBus)).ToList(), impedance, iMax);
			}

			// Copy all lines that were not replaced
			network.Lines.Where(l => !l.IsTransformerConnection && !replacedLines.Contains(l)).Do(l => AddLineLike(l, aggregatedNetwork));

			// Copy all transformers
			network.PowerTransformers.Do(t => aggregatedNetwork.AddTransformerLike(t));

			return aggregatedNetwork;
		}

		/// <summary>
		/// Create a new PowerNetwork from an existing PowerNetwork by copying all 
		/// elements, except for buses and lines which are not connected to any provider.
		/// </summary>
		/// <param name="originalNetwork"></param>
		/// <returns></returns>
		private (PowerNetwork, HashSet<Bus>) RemoveIsolatedComponents(PowerNetwork originalNetwork)
		{
			NetworkConfiguration config = NetworkConfiguration.AllClosed(originalNetwork);
			var unconnectedBuses = originalNetwork.Buses
				.Where(b => !config.BusIsConnected(b))
				.ToHashSetScoop();

			PowerNetwork aggregatedNetwork = TrimNetwork(
				originalNetwork,
				$"Removed isolated components of {originalNetwork.Name}",
				keepLine: l => l.Endpoints.All(config.BusIsConnected),
				keepBus: config.BusIsConnected,
				keepTransformer: t => config.BusIsConnected(t.Bus)
			) ;

			return (aggregatedNetwork, unconnectedBuses);
		}

		/// <summary>
		/// Creates a copy containing some of the elements of the input network
		/// </summary>
		/// <param name="originalNetwork">The network to copy from</param>
		/// <param name="name">The name of the new network</param>
		/// <param name="keepLine">Indicator function for whether to keep a (non-transformer) line</param>
		/// <param name="keepBus">Indicator function for whether to keep a (non-transformer) bus</param>
		/// <param name="keepTransformer">Indicator function for whether to keep a transformer</param>
		/// <returns>The trimmed network</returns>
		private PowerNetwork TrimNetwork(PowerNetwork originalNetwork, string name, 
			Func<Line, bool> keepLine, Func<Bus, bool> keepBus, Func<Transformer, bool> keepTransformer)
		{
			// Create the new network
			PowerNetwork newNetwork = new PowerNetwork(name);

			// Copy all buses that will not be eliminated
			foreach (var bus in originalNetwork.Buses.Where(b => !b.IsTransformer))
			{
				if (keepBus(bus))
				{
					AddBusLike(bus, newNetwork);
				}
			}

			// Copy all lines that will not be eliminated
			foreach (var line in originalNetwork.Lines.Where(l => !l.IsTransformerConnection && keepLine(l)))
			{
				AddLineLike(line, newNetwork);
			}

			// Copy all transformers that will not be eliminated
			foreach (var transformer in originalNetwork.PowerTransformers)
			{
				if (keepTransformer(transformer))
				{
					newNetwork.AddTransformerLike(transformer);
				}
			}

			return newNetwork;
		}

		/// <summary>
		/// Aggregates serial lines to produce an aggregated, simplified, PowerNetwork that retains all the physical properties of this network
		/// I.e. the aggregated network can be configured in the same ways, to give the same power flows and the same objective values,
		/// as the original network.
		/// </summary>
		/// <returns>The new network</returns>
		private PowerNetwork AggregateSerialLines(PowerNetwork originalNetwork)
		{
			HashSet<Line> hasBeenHandled = new HashSet<Line>();
			List<LinesToMerge> lineSetsToMerge = new List<LinesToMerge>();

			// Treat each bus that will be eliminated
			foreach (var bus in originalNetwork.Buses.Where(b => CanBeEliminated(b)))
			{
				if (hasBeenHandled.Contains(bus.IncidentLines.First()))
					// This bus has already been seen (starting from another bus)
					// Skip bus
					continue;

				// Find the sequence of lines to aggregate around the bus
				var (linesToMerge, startBus, endBus) = LineSequenceToAggregate(bus, CanBeEliminated);

				// Prevent these lines from being treated again
				foreach (var l in linesToMerge)
					hasBeenHandled.Add(l.Line);

				if (_Options.KeepOneUnmergedSwitch)
				{
					var beforeSwitch = linesToMerge.TakeWhile(l => !l.Line.IsSwitchable).ToList();
					var afterSwitch = linesToMerge.Skip(beforeSwitch.Count + 1).ToList();

					// All lines can be merged and contain no switches
					if (beforeSwitch.Count > 1)
						lineSetsToMerge.Add(new LinesToMerge(beforeSwitch, false));

					if (afterSwitch.Count > 1)
						// These lines may contain a switch, but we ignore it, since we have
						// preserved one switch along the sequence
						lineSetsToMerge.Add(new LinesToMerge(afterSwitch, false));
				}
				else
				{
					bool hasSwitch = linesToMerge.Any(l => l.Line.IsSwitchable);
					lineSetsToMerge.Add(new LinesToMerge(linesToMerge, hasSwitch));
				}
			}

			// Do the merge of these line sequences, and copy all the rest

			return MergeSerialLines(originalNetwork, lineSetsToMerge);



			// Returns true if the given bus can be eliminated because it connects only two lines
			// that can be aggregated serially
			bool CanBeEliminated(Bus bus) =>
				!bus.IsTransformer &&
				!bus.IsConsumer &&
				!bus.IsProvider &&
				bus.IncidentLines.CountIs(2) &&
				bus.IncidentLines.All(l =>
					(!l.IsSwitchable || _Options.AggregateSerialSwitches) &&
					!l.IsBreaker &&
					!l.IsTransformerConnection);
		}

		/// <summary>
		/// Returns the longest sequence of lines that can be aggregated (serially) around the given bus
		/// </summary>
		/// <param name="bus"></param>
		/// <param name="canBeEliminated">Returns true for buses that may be eliminated</param>
		private (List<DirectedLine> Lines, Bus StartBus, Bus EndBus) LineSequenceToAggregate(Bus bus, Func<Bus, bool> canBeEliminated)
		{
			// Start with just the two lines incident to the bus
			List<DirectedLine> linesToMerge = new List<DirectedLine> {
				bus.IncidentLines.First().InDirectionTo(bus),
				bus.IncidentLines.Last().InDirectionFrom(bus)
			};

			Bus startBus, endBus;

			while (true)
			{
				startBus = linesToMerge.First().StartNode;
				endBus = linesToMerge.Last().EndNode;

				if (startBus == endBus && canBeEliminated(startBus))
					throw new Exception("The network has a cycle of lines that can be serially aggregated");

				if (canBeEliminated(startBus))
				{
					// Extend sequence with the line before the start
					var line = startBus.IncidentLines.Except(linesToMerge.First().Line).Single();
					linesToMerge.Insert(0, line.InDirectionTo(startBus));
					continue;
				}

				if (canBeEliminated(endBus))
				{
					// Extend sequence with the line after the end
					var line = endBus.IncidentLines.Except(linesToMerge.Last().Line).Single();
					linesToMerge.Add(line.InDirectionFrom(endBus));
					continue;
				}

				// Sequence cannot be extended
				return (linesToMerge, startBus, endBus);
			}
		}

		private PowerNetwork MergeSerialLines(PowerNetwork originalNetwork, List<LinesToMerge> lineSequencesToMerge)
		{
			// Create the aggregate network
			PowerNetwork aggregatedNetwork = new PowerNetwork($"Aggregate of {originalNetwork.Name}");

			// Find the internal buses in each line sequence
			var eliminatedBuses = lineSequencesToMerge.SelectMany(lineset =>
				lineset.Lines.Skip(1).Select(l => l.StartNode)); 

			// Copy all other buses
			foreach (var bus in originalNetwork.Buses.Except(eliminatedBuses).Where(b => !b.IsTransformer))
				AddBusLike(bus, aggregatedNetwork);

			// Create a new line for each sequence of lines to merge
			foreach (var linesToMerge in lineSequencesToMerge)
				Merge(linesToMerge);

			// Copy all lines that were not merged
			var mergedLines = lineSequencesToMerge.SelectMany(x => x.Lines.Select(l => l.Line));

			foreach (var line in originalNetwork.Lines.Except(mergedLines).Where(l => !l.IsTransformerConnection))
				AddLineLike(line, aggregatedNetwork);

			// Copy all transformers
			foreach (var transformer in originalNetwork.PowerTransformers)
			{
				aggregatedNetwork.AddTransformerLike(transformer);
			}

			return aggregatedNetwork;



			void Merge(LinesToMerge linesToMerge)
			{
				var lines = linesToMerge.Lines;

				var startBus = lines.First().StartNode;
				var endBus = lines.Last().EndNode;
				if (startBus == endBus)
				{
					// The lines form a loop.
					// We're not allowed to merge them all, so keep the first one unmerged.
					// The next round of parallel merge will complete the merge.
					var firstLine = lines[0];
					startBus = firstLine.EndNode;

					AddBusLike(startBus, aggregatedNetwork);
					AddLineLike(firstLine.Line, aggregatedNetwork);

					lines = lines.Skip(1).ToList();
				}

				// Find the aggregated line properties
				Complex impedance = lines.ComplexSum(l => l.Line.Impedance);
				double iMax = lines.Min(l => l.Line.IMax);
				double vMax = lines.Min(l => l.Line.VMax);
				string name = lines.Select(l => l.Line.Name).Concatenate("+");

				// Add aggregated line
				var newLine = aggregatedNetwork.AddLine(startBus.Name, endBus.Name, impedance, iMax, vMax, linesToMerge.IsSwitch, 0, false, name: name);

				RecordMerge(newLine, AggregateType.Serial, lines, impedance, iMax);
			}
		}

		/// <summary>
		/// Records a line merge in _merges
		/// </summary>
		/// <param name="newLine">The new line</param>
		/// <param name="type">The type of aggregation</param>
		/// <param name="lines">The lines being merged</param>
		/// <param name="impedance">The merged line's impedance</param>
		/// <param name="iMax">The maximal current allowed in the merged line</param>
		private MergedLine RecordMerge(Line newLine, AggregateType type, List<DirectedLine> lines, Complex impedance, double iMax)
		{
			// Find info for the lines
			var parts = lines.Select(l => MergeInfoFor(l)).ToList();

			// Record
			MergedLine mergedLine = new MergedLine(type, parts, impedance, iMax);
			_merges.Add(newLine.Name, mergedLine);

			return mergedLine;
		}

		/// <summary>
		/// Adds a new bus to the given <paramref name="network"/>, based on the data in the given original <paramref name="bus"/>.
		/// </summary>
		/// <param name="bus"></param>
		/// <param name="network"></param>
		private void AddBusLike(Bus bus, PowerNetwork network) => network.AddBusLike(bus, bus.Name);

		/// <summary>
		/// Adds a new bus to the aggregated network, based on the data in the given original bus.
		/// </summary>
		/// <param name="line"></param>
		/// <param name="network"></param>
		private void AddLineLike(Line line, PowerNetwork network)
		{
			double switchCost = line.IsSwitchable ? line.SwitchingCost : 0;
			network.AddLine(line.Node1.Name, line.Node2.Name, line.Impedance, line.IMax, line.VMax, line.IsSwitchable, switchCost, line.IsBreaker, line.Name);
		}

		/// <summary>
		/// Updates the <see cref="AggregateNetwork"/>'s <see cref="PowerNetwork.PropertiesProvider"/> and 
		/// <see cref="PowerNetwork.CategoryProvider"/> with aggregated data from the original network.
		/// Sectioning and repair times are cannot be aggregated exactly, but are set to average values,
		/// weighted by fault frequency.
		/// </summary>
		private void AggregateKileProperties()
		{
			// Aggregate fault properties for each line

			var properties = AggregateNetwork.PropertiesProvider;
			var originalProperties = OriginalNetwork.PropertiesProvider;

			foreach (var line in AggregateNetwork.Lines)
			{
				var originalLines = MergeInfoFor(line).Lines;

				// Total faults are summed
				var totalFaults = originalLines.Sum(l => originalProperties.FaultsPerYear(l));
				if (totalFaults == 0)
					continue;

				// Sectioning and repair times are averaged
				var averageSectioningTime =
					originalLines.Sum(l => originalProperties.SectioningTime(l).Times(originalProperties.FaultsPerYear(l)))
					.Times(1 / totalFaults);

				var averageRepairTime =
					originalLines.Sum(l => originalProperties.RepairTime(l).Times(originalProperties.FaultsPerYear(l)))
					.Times(1 / totalFaults);

				properties.Add(line, totalFaults, averageSectioningTime, averageRepairTime);
			}

			// Copy consumer categories

			var categories = AggregateNetwork.CategoryProvider;
			var originalCategories = OriginalNetwork.CategoryProvider;

			foreach (var consumer in AggregateNetwork.Consumers)
			{
				var originalConsumer = OriginalNetwork.GetBus(consumer.Name);
				if (!originalCategories.HasDataFor(originalConsumer))
					continue;

				foreach (var category in originalCategories.Categories(originalConsumer))
					categories.Set(consumer, category, originalCategories.ConsumptionFraction(originalConsumer, category));
			}
		}

		#endregion

		/// <summary>
		/// A type of line aggregation
		/// </summary>
		public enum AggregateType
		{
			/// <summary>
			/// Just a single line; not an aggregate
			/// </summary>
			SingleLine,

			/// <summary>
			/// A serial aggregation of one or more lines (or aggregates of lines)
			/// </summary>
			Serial,

			/// <summary>
			/// A parallel aggregation of one or more lines (or aggregates of lines)
			/// </summary>
			Parallel
		}

		/// <summary>
		/// A sequence of directed lines to merge into one line
		/// </summary>
		private class LinesToMerge
		{
			/// <summary>
			/// The lines to merge
			/// </summary>
			public List<DirectedLine> Lines;

			/// <summary>
			/// If true, the merged line should be a switch
			/// </summary>
			public bool IsSwitch;

			public LinesToMerge(List<DirectedLine> lines, bool isSwitch)
			{
				Lines = lines;
				IsSwitch = isSwitch;
			}
		}

		/// <summary>
		/// Options for network aggregation
		/// </summary>
		public class Options
		{
			/// <summary>
			/// If true, switches can be included in a serial aggregation of lines.
			/// If false, switches are never aggregated.
			/// False by default.
			/// 
			/// NOTE! This option is not safe when KILE cost is used, as it can remove
			/// the last nonfaulting switch between an provider and a faulting line, which 
			/// is a fatal error.
			/// (It also is nonexact with regard to the KILE cost objective.)
			/// We cannot use this option before a fix for this has been found.
			/// </summary>
			public bool AggregateSerialSwitches { get; set; } = false;

			/// <summary>
			/// If true, one switch in each serial sequence of lines is always preserved,
			/// unmerged.
			/// If false (and <see cref="AggregateSerialSwitches"/> is true), a serial sequence
			/// of lines will be merged into just one line, which will be a switch if any of the
			/// original lines was.
			/// False by default
			/// </summary>
			public bool KeepOneUnmergedSwitch { get; set; } = false;
		}
	}

	/// <summary>
	/// Information about how lines were aggregated to produce a line
	/// in an aggregated network
	/// </summary>
	public class MergedLine
	{
		/// <summary>
		/// The type of aggregation that produced the line
		/// </summary>
		public NetworkAggregation.AggregateType Type { get; }

		/// <summary>
		/// The parts (lines or aggregates) that were aggregated, if this is an actual aggregate.
		/// Null if this is a single line.
		/// 
		/// For a serial aggregation, the parts are given in order (and direction) from start to 
		/// end of the merged line.
		/// For a parallel aggregation, the parts are given in order sorted by line name,
		/// and oriented in the same direction as the merged line.
		/// </summary>
		public List<DirectedMergedLine> Parts { get; }

		/// <summary>
		/// The line, if this is just a single line, oriented from start to end of the merged line.
		/// Null if this is an actual aggregate.
		/// </summary>
		public DirectedLine? Line { get; }

		/// <summary>
		/// The line, if this is just a single line.
		/// Throws an exception if this is an actual aggregate.
		/// </summary>
		public Line SingleLine => Line.Value.Line;

		/// <summary>
		/// Enumerates all original lines included in the merged line, in
		/// an arbitrary order
		/// </summary>
		public IEnumerable<Line> Lines
		{
			get
			{
				if (Line != null)
					return new[] { Line.Value.Line };

				return Parts.SelectMany(part => part.MergedLine.Lines);
			}
		}

		/// <summary>
		/// Enumerates all original lines included in the merged line, in the direction
		/// they are used (with respect to the merged line's forward direction)
		/// </summary>
		public IEnumerable<DirectedLine> DirectedLines
		{
			get
			{
				if (Line != null)
					return new[] { Line.Value };

				return Parts.SelectMany(part => part.DirectedLines);
			}
		}

		/// <summary>
		/// Enumerates a path of original lines from the start to the end of this
		/// merged line, in the direction they are used.
		/// When there is parallel aggregation, the result contains one of the parallel
		/// lines (arbitrarily, by line names).
		/// </summary>
		public IEnumerable<DirectedLine> OneDirectedPath
		{
			get
			{
				if (Line != null)
					return new[] { Line.Value };

				if (Type == NetworkAggregation.AggregateType.Parallel)
					// Select the parallel that contains the first line name lexically
					return Parts.ArgMin(p => p.OneDirectedPath.Min(l => l.Line.Name))
						.OneDirectedPath;
				else
					return Parts.SelectMany(part => part.OneDirectedPath);
			}
		}

		/// <summary>
		/// The bus at the start of the merged line.
		/// This is a bus in the unaggregated network.
		/// </summary>
		public Bus StartNode => Line?.StartNode ?? Parts.First().StartNode;

		/// <summary>
		/// The bus at the end of the merged line.
		/// This is a bus in the unaggregated network.
		/// </summary>
		public Bus EndNode => Line?.EndNode ?? Parts.Last().EndNode;

		/// <summary>
		/// The impedance of the aggregated line
		/// </summary>
		public Complex Impedance { get; }

		/// <summary>
		/// The maximal current allowed in the aggregated line
		/// </summary>
		public double IMax { get; }

		/// <summary>
		/// Initializes information for a single, unaggregated, line
		/// </summary>
		public MergedLine(DirectedLine line)
		{
			Type = NetworkAggregation.AggregateType.SingleLine;
			Line = line;
			Impedance = line.Line.Impedance;
			IMax = line.Line.IMax;
		}

		/// <summary>
		/// Initializes information for an actual aggregate
		/// </summary>
		/// <param name="type">The type of aggregation</param>
		/// <param name="parts">The parts that were aggregated</param>
		/// <param name="impedance">The merged line's impedance</param>
		/// <param name="iMax">The maximal current allowed in the merged line</param>
		public MergedLine(NetworkAggregation.AggregateType type, List<DirectedMergedLine> parts, Complex impedance, double iMax)
		{
			Type = type;
			Parts = parts;
			Impedance = impedance;
			IMax = iMax;
		}
	}

	/// <summary>
	/// A merged line and a direction along it
	/// </summary>
	public struct DirectedMergedLine
	{
		/// <summary>
		/// The merged line
		/// </summary>
		public readonly MergedLine MergedLine;

		/// <summary>
		/// The direction
		/// </summary>
		public readonly LineDirection Direction;

		/// <summary>
		/// Enumerates all original lines included in the merged line, in the direction
		/// they are used (with respect to the merged line seen in <see cref="Direction"/>)
		/// </summary>
		public IEnumerable<DirectedLine> DirectedLines
		{
			get
			{
				if (Direction == LineDirection.Forward)
					return MergedLine.DirectedLines;
				else
					return MergedLine.DirectedLines.ReverseDirectedPath();
			}
		}

		/// <summary>
		/// Enumerates a path of original lines from the start to the end of this
		/// merged line.
		/// When there is parallel aggregation, the result contains one of the parallel
		/// lines (arbitrarily, the first).
		/// </summary>
		public IEnumerable<DirectedLine> OneDirectedPath
		{
			get
			{
				if (Direction == LineDirection.Forward)
					return MergedLine.OneDirectedPath;
				else
					return MergedLine.OneDirectedPath.ReverseDirectedPath();
			}
		}

		/// <summary>
		/// The bus at the start (relative to <see cref="Direction"/>) of the merged line.
		/// This is a bus in the unaggregated network.
		/// </summary>
		public Bus StartNode
		{
			get
			{
				if (Direction == LineDirection.Forward)
					return MergedLine.StartNode;
				else
					return MergedLine.EndNode;
			}
		}

		/// <summary>
		/// The bus at the end (relative to <see cref="Direction"/>) of the merged line.
		/// This is a bus in the unaggregated network.
		/// </summary>
		public Bus EndNode
		{
			get
			{
				if (Direction == LineDirection.Forward)
					return MergedLine.EndNode;
				else
					return MergedLine.StartNode;
			}
		}

		/// <summary>
		/// The same merged line in the opposite direction
		/// </summary>
		public DirectedMergedLine Reversed => new DirectedMergedLine(MergedLine, OppositeDirection);

		/// <summary>
		/// See <see cref="MergedLine.Type"/>
		/// </summary>
		public NetworkAggregation.AggregateType Type => MergedLine.Type;

		/// <summary>
		/// Enumerates the merged line's <see cref="MergedLine.Parts"/>, oriented
		/// with respect to <see cref="Direction"/>
		/// </summary>
		public IEnumerable<DirectedMergedLine> Parts
		{
			get
			{
				if (Direction == LineDirection.Forward)
					return MergedLine.Parts;
				else
					return MergedLine.Parts.Reverse<DirectedMergedLine>().Select(p => p.Reversed);
			}
		}

		/// <summary>
		/// The impedance of the aggregated line
		/// </summary>
		public Complex Impedance => MergedLine.Impedance;

		/// <summary>
		/// The maximal current in the aggregated line
		/// </summary>
		public double Imax => MergedLine.IMax;

		/// <summary>
		/// See <see cref="MergedLine.SingleLine"/>
		/// </summary>
		public Line SingleLine => MergedLine.SingleLine;

		/// <summary>
		/// Initializes a directed merged line
		/// </summary>
		/// <param name="line">The line</param>
		/// <param name="direction">The direction</param>
		public DirectedMergedLine(MergedLine line, LineDirection direction)
		{
			MergedLine = line;
			Direction = direction;
		}

		/// <summary>
		/// The opposite of <see cref="Direction"/>
		/// </summary>
		private LineDirection OppositeDirection
		{
			get
			{
				if (Direction == LineDirection.Forward)
					return LineDirection.Reverse;
				else
					return LineDirection.Forward;
			}
		}
	}

	public static partial class Extensions
	{
		/// <summary>
		/// Returns this merged line in the direction pointing from the bus with the given name
		/// </summary>
		public static DirectedMergedLine InDirectionFrom(this MergedLine line, string busName)
		{
			if (busName == line.StartNode.Name)
				return new DirectedMergedLine(line, LineDirection.Forward);
			if (busName == line.EndNode.Name)
				return new DirectedMergedLine(line, LineDirection.Reverse);

			throw new Exception("Line does not start/end in that bus");
		}
	}
}
