using Sintef.Scoop.Utilities;
using Sintef.Scoop.Utilities.GeoCoding;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Builds random power networks.
	/// 
	/// For network topology, you can specify the number of lines, branches and cycles.
	/// For line properties, you can specify the number of breakers, and density of switches.
	/// For node properties, you can specify the number of providers and consumers.
	/// Electrical properties are so far just set to defaults.
	/// 
	/// The network is built by first creating one line between two nodes.
	/// Then more lines are added one by one in one of three ways:
	///  - Extending at a leaf node
	///  - Branching at an internal node
	///  - Connecting two existing nodes
	///  
	/// Before starting, the algorithm generates sequences containing the desired
	/// distributions of line types, node types and ways to add lines and shuffles them. During construction,
	/// what to add next, and how, is decided by popping the next element from one of these sequences.
	/// </summary>
	public class RandomNetworkBuilder
	{
		/// <summary>
		/// Parameters for the topology of the network
		/// </summary>
		public TopologyParameters Topology { get; }

		/// <summary>
		/// Parameters for which network components to generate
		/// </summary>
		public ComponentParameters Components { get; private set; }

		private PowerNetwork _network;
		private List<char> _nodeTypes;
		private List<char> _lineTypes;
		private Random _random;
		private int _nodeCounter = 1;
		private int _lineCounter = 1;

		private double _vMin = 8000;
		private double _vMax = 12000;
		private double _generatorVoltage = 10000;
		private Complex _generatorMin = new Complex();
		private Complex _generatorMax = new Complex(10000, 10000);
		private double _lineIMax;
		private double _lineVMax = double.PositiveInfinity;
		private Complex _impedance = new Complex(1, 0.5);

		// Caches the set of leaf nodes
		private QuickList<Bus> _leaves;
		// Caches the set of non-leaf nodes
		private QuickList<Bus> _nonLeaves;

		/// <summary>
		/// Creates a network based on the given parameters
		/// </summary>
		/// <param name="lineCount">The number of lines</param>
		/// <param name="branchCount">The number of branches</param>
		/// <param name="cycleCount">The number of cycles</param>
		/// <param name="consumerCount">The number of consumer nodes</param>
		/// <param name="providerCount">The number of provider nodes</param>
		/// <param name="switchFraction">The fraction of lines that are switches (in percent)</param>
		/// <param name="breakerCount">The number of lines that are breakers</param>
		/// <param name="addBreakersAtGenerators">If true, ensures that each line connected to a provider is
		///   a breaker, by adding extra lines if necessary</param>
		///   <param name="random">The random generator to use. If null, one is created</param>
		///   <param name="buildAroundEachProvider">If true, we build trees around randomly located providers, instead
		/// of building one connected graph starting at a single random bus.
		/// Default false.</param>
		/// <param name="createRandomCoordinates">If set to true, random coordinates will be generated for buses. Optional, default false.</param>
		/// <returns></returns>
		public static PowerNetwork Create(int lineCount, int branchCount, int cycleCount,
			int consumerCount, int providerCount,
			double switchFraction, int breakerCount, bool addBreakersAtGenerators, bool createRandomCoordinates = false,
			bool buildAroundEachProvider = false,
			Random random = null)
		{
			var builder = new RandomNetworkBuilder(random);

			builder.Topology.LineCount = lineCount;
			builder.Topology.BranchCount = branchCount;
			builder.Topology.CycleCount = cycleCount;
			builder.Topology.BuildTreesAroundProviders = buildAroundEachProvider;

			builder.Components.ConsumerCount = consumerCount;
			builder.Components.ProviderCount = providerCount;
			builder.Components.SwitchFraction = switchFraction;
			builder.Components.BreakerCount = breakerCount;
			builder.Components.AddBreakersAtGenerators = addBreakersAtGenerators;
			builder.Components.CreateRandomCoordinates = createRandomCoordinates;

			return builder.Create(true);
		}

		/// <summary>
		/// Initializes a network builder
		/// </summary>
		/// <param name="random">The random generator to use. If null, one is created</param>
		public RandomNetworkBuilder(Random random = null)
		{
			_random = random ?? new Random();

			Topology = new TopologyParameters();
			Components = new ComponentParameters();
		}

		/// <summary>
		/// Creates and returns a network according to the configuration
		/// </summary>
		public PowerNetwork Create(bool requireOkConnectivity = false, int maxTries = 10)
		{
			PowerNetwork network = null;

			for (int i = 0; i < maxTries; ++i)
			{
				network = DoCreate();

				if (!requireOkConnectivity || network.Connectivity == PowerNetwork.ConnectivityType.Ok)
					return network;
			}

			throw new Exception($"Failed to create an OK network in {maxTries} tries. Last connectivity is: {network.Connectivity}");
		}

		private PowerNetwork DoCreate()
		{
			int consumerCount = Components.ConsumerCount;

			_lineIMax = consumerCount;
			_leaves = new QuickList<Bus>();
			_nonLeaves = new QuickList<Bus>();


			// Find the number of nodes to generate of each type

			int nodeCount = Topology.LineCount + 1 - Topology.CycleCount;
			int transitionNodeCount = nodeCount - consumerCount - Components.ProviderCount;
			if (transitionNodeCount < 0)
			{
				consumerCount += transitionNodeCount;
				transitionNodeCount = 0;
			}

			// Make the sequence of node types to generate


			//Put the providers last
			if (Topology.BuildTreesAroundProviders)
			{
				_nodeTypes = Repeat('T', transitionNodeCount)
					.Concat(Repeat('C', consumerCount))
					.Shuffled(_random).Concat(Repeat('P', Components.ProviderCount))
					.ToList();
			}
			else
			{
				_nodeTypes = Repeat('T', transitionNodeCount)
					.Concat(Repeat('C', consumerCount))
					.Concat(Repeat('P', Components.ProviderCount))
					.Shuffled(_random)
					.ToList();
			}

			// Find the sequence of how to connect each new line

			int extendLineCount = Topology.LineCount - Topology.BranchCount - Topology.CycleCount;

			var lineConnectTypes = Repeat('E', extendLineCount - 1)
				.Concat(Repeat('B', Topology.BranchCount))
				.Concat(Repeat('C', Topology.CycleCount))
				.Shuffled(_random)
				.ToList();


			// Find the number of lines to generate of each type

			int switchCount = (int)Math.Round(Topology.LineCount * Components.SwitchFraction);
			int normalLineCount = Topology.LineCount - switchCount - Components.BreakerCount;
			if (normalLineCount < 0)
			{
				switchCount += normalLineCount;
				normalLineCount = 0;
			}

			// Make the sequence of line types to generate

			_lineTypes = Repeat('L', normalLineCount)
				.Concat(Repeat('S', switchCount))
				.Concat(Repeat('B', Components.BreakerCount))
				.Shuffled(_random)
				.ToList();

			// Start creating the network

			_network = new PowerNetwork("Random network");

			if (Topology.BuildTreesAroundProviders)
			{
				// Add the first lines, putting each provider at a random location
				List<Bus> addedProviderNodes = new List<Bus>();
				for (int i = 0; i < Components.ProviderCount; i++)
				{
					addedProviderNodes.Add(AddNode());
				}
				foreach (var firstNode in addedProviderNodes)
				{
					var secondNode = AddNode(firstNode.Location, 10);
					AddLine(firstNode, secondNode);
				}

				// Then connect each additional line
				foreach (var type in lineConnectTypes)
				{
					if (_nodeTypes.Any())
						AddLine(type);
					else
						break;
				}
			}
			else
			{
				// Add the first lines

				var firstNode = AddNode();
				var secondNode = AddNode();
				AddLine(firstNode, secondNode);

				// The connect each additional line

				foreach (var type in lineConnectTypes)
					AddLine(type);
			}

			//Add random consumer categories and line fault properties
			_network.CategoryProvider.Randomize(_random,1);
			_network.PropertiesProvider.SetAll(0.1, TimeSpan.FromHours(4), TimeSpan.FromHours(3));
			_network.PropertiesProvider.Randomize(_random);
			_network.PropertiesProvider.SuppressFaultsAroundProviders();

			return _network;
		}

		/// <summary>
		/// Adds a line (and possibly a node) to the network
		/// </summary>
		/// <param name="connectType">The connection type to make:
		///  'E'xtend from leaf, or
		///  'B'ranch (extend from internal node), or
		///  create 'C'ycle 
		///  </param>
		private void AddLine(char connectType)
		{
			Bus firstNode;
			Bus secondNode;
			switch (connectType)
			{
				case 'E':
					firstNode = ExistingNode(preferLeaf: true);
					secondNode = AddNode(firstNode.Location, 10);

					break;

				case 'B':
					firstNode = ExistingNode(preferLeaf: false);
					secondNode = AddNode(firstNode.Location, 10);

					break;

				case 'C':
					firstNode = ExistingNode(preferLeaf: true);
					secondNode = ExistingNode(preferLeaf: true, avoid: firstNode);
					break;

				default:
					throw new Exception();
			}

			AddLine(firstNode, secondNode);
		}

		/// <summary>
		/// Returns a random existing node in the network
		/// </summary>
		/// <param name="preferLeaf">If true, returns a node with one incident line, if possible.
		/// If false, returns a node with more than one incident line, if possible</param>
		/// <param name="avoid">This node may not be returned</param>
		private Bus ExistingNode(bool preferLeaf, Bus avoid = null)
		{
			Func<Bus, bool> filter = b => b != avoid && !b.Name.StartsWith("artificial_");

			QuickList<Bus> firstPri = preferLeaf ? _leaves : _nonLeaves;
			QuickList<Bus> secondPri = preferLeaf ? _nonLeaves : _leaves;

			return firstPri.RandomElementOrDefault(_random, filter) ?? secondPri.RandomElementOrDefault(_random, filter);
		}

		/// <summary>
		/// Adds a new node to the network, adding a random coordinate at a given distance if 
		///  <see cref="ComponentParameters.CreateRandomCoordinates"/> returns true. If false, the
		///  function just calls <see cref="AddNode(Coordinate)"/>().
		/// </summary>
		/// <param name="center">A center coordinate.</param>
		/// <param name="distanceFromCenter">The distance from center at which to generate the new coordinate</param>
		private Bus AddNode(Coordinate center, double distanceFromCenter)
		{
			if (Components.CreateRandomCoordinates)
			{
				return AddNode(CloseTo(center,distanceFromCenter));
			}
			else
				return AddNode();
		}

		/// <summary>
		/// Returns a random coordinate at a the given distance from the given <paramref name="center"/> coordinate.
		/// </summary>
		/// <param name="center"></param>
		/// <param name="distanceFromCenter"></param>
		/// <returns></returns>
		private Coordinate CloseTo(Coordinate center, double distanceFromCenter)
		{
			double angle = (_random.NextDouble() - 0.5)*120; 
			double x = center.X + distanceFromCenter * Math.Cos(angle);
			double y = center.Y + distanceFromCenter * Math.Sin(angle);
			return new Coordinate(x, y);
		}
			/// <summary>
			/// Adds a new node to the network
			/// </summary>
			/// <param name="location">If given, this coordinate is used. If not, and 
			/// <see cref="ComponentParameters.CreateRandomCoordinates"/> = true, a random coorinate is generated.</param>
			private Bus AddNode(Coordinate location = null)
		{
			char type = Pop(_nodeTypes);

			Coordinate coordinate = location;
			if (Components.CreateRandomCoordinates && coordinate == null)
			{
				coordinate = new Coordinate(_random.Next(0, Math.Max(10, Components.ConsumerCount / 10)),
																		_random.Next(0, Math.Max(10, Components.ConsumerCount / 10)));
			}

			switch (type)
			{
				case 'T':
					return _network.AddTransition(_vMin, _vMax, $"transition_{_nodeCounter++}",location:coordinate);
				case 'C':
					var bus = _network.AddConsumer(_vMin, _vMax, $"consumer_{_nodeCounter++}", location: coordinate);
					//_network.CategoryProvider.Set(bus, ConsumerCategory.Domestic);
					return bus;
				case 'P':
					return _network.AddProvider(_generatorVoltage, _generatorMax, _generatorMin, $"provider_{_nodeCounter++}", location: coordinate);
				default:
					throw new Exception();
			}
		}

		/// <summary>
		/// Pops a character off end of the given list
		/// </summary>
		private char Pop(List<char> list)
		{
			char type = list.Last();
			list.RemoveAt(list.Count - 1);
			return type;
		}

		/// <summary>
		/// Adds a line between the given nodes
		/// </summary>
		private void AddLine(Bus node1, Bus node2)
		{
			var lineType = Pop(_lineTypes);

			AddLine(node1, node2, lineType);
		}

		/// <summary>
		/// Adds a line between the given nodes, of the given type
		/// </summary>
		/// <param name="node1"></param>
		/// <param name="node2"></param>
		/// <param name="lineType">The line type:
		///  'S'witch, or
		///  'B'reaker, or
		///  normal 'L'ine
		/// </param>
		private void AddLine(Bus node1, Bus node2, char lineType)
		{
			if (Components.AddBreakersAtGenerators)
			{
				Coordinate coordinate = null;
				if (node1.Location != null && node2.Location != null)
					coordinate = Coordinate.CenterPoint(node1.Location, node2.Location);

				if (node1.IsProvider && lineType != 'B')
				{
					var extraNode = _network.AddTransition(_vMin, _vMax, $"artificial_{_nodeCounter++}", location: coordinate);
					AddLine(node1, extraNode, 'B');
					AddLine(extraNode, node2, lineType);
					return;
				}

				if (node2.IsProvider && lineType != 'B')
				{
					var extraNode = _network.AddTransition(_vMin, _vMax, $"artificial_{_nodeCounter++}", location: coordinate);
					AddLine(node1, extraNode, lineType);
					AddLine(extraNode, node2, 'B');
					return;
				}
			}

			var impedance = _impedance;
			bool isSwitch = false;
			bool isBreaker = false;
			string name;

			switch (lineType)
			{
				case 'S':
					isSwitch = true;
					name = $"switch_{_lineCounter++}";
					break;
				case 'B':
					isBreaker = true;
					if (Components.BreakersAreSwitches)
						isSwitch = true;
					impedance = 0;
					name = $"breaker_{_lineCounter++}";
					break;
				case 'L':
					name = $"line_{_lineCounter++}";
					break;
				default:
					throw new Exception();
			}

			_network.AddLine(node1.Name, node2.Name, impedance, _lineIMax, _lineVMax, isSwitch, 1, isBreaker, name: name);

			UpdateLeaves(node1);
			UpdateLeaves(node2);
		}

		/// <summary>
		/// Updates the membership in _leaves and _nonLeaves for the given node
		/// </summary>
		private void UpdateLeaves(Bus node)
		{
			if (node.IncidentLinesCount == 1)
			{
				_nonLeaves.Remove(node);
				_leaves.Add(node);
			}
			else
			{
				_leaves.Remove(node);
				_nonLeaves.Add(node);
			}
		}

		private static IEnumerable<char> Repeat(char c, int count)
		{
			return Enumerable.Repeat(c, count);
		}
		

		/// <summary>
		/// Parameters for the topology of the network
		/// </summary>
		public class TopologyParameters
		{
			/// <summary>
			/// The number of lines.
			/// This inludes normal lines, switchable lines and breakers.
			/// Default 20.
			/// </summary>
			public int LineCount { get; set; } = 20;
			/// <summary>
			/// The number of branches.
			/// Default 4.
			/// </summary>
			public int BranchCount { get; set; } = 4;
			/// <summary>
			/// The number of cycles.
			/// Default 4.
			/// </summary>
			public int CycleCount { get; set; } = 4;

			/// <summary>
			/// If true, we build trees around randomly located providers, instead
			/// of building one connected graph starting at a single random bus.
			/// Default false.
			/// </summary>
			public bool BuildTreesAroundProviders { get; set; } = false;

		}

		/// <summary>
		/// Parameters for which network components to generate
		/// </summary>
		public class ComponentParameters
		{
			/// <summary>
			/// The number of consumer nodes.
			/// Default 5.
			/// </summary>
			public int ConsumerCount { get; set; } = 5;
			/// <summary>
			/// The number of provider nodes.
			/// Default 2.
			/// </summary>
			public int ProviderCount { get; set; } = 2;
			/// <summary>
			/// The fraction of lines that are switches.
			/// Default 0.5.
			/// </summary>
			public double SwitchFraction { get; set; } = 0.5;
			/// <summary>
			/// The number of lines that are breakers.
			/// Default 3.
			/// </summary>
			public int BreakerCount { get; set; } = 3;
			/// <summary>
			/// If true, ensures that each line connected to a provider is
			/// a breaker, by adding extra lines if necessary.
			/// True by default.
			/// </summary>
			public bool AddBreakersAtGenerators { get; set; } = true;
			/// <summary>
			/// If true, each breaker in the network will also be set to be a switch. 
			/// False by default.
			/// </summary>
			public bool BreakersAreSwitches { get; set; } = false;

			/// <summary>
			/// If set to true, random coordinates will be generated for buses.
			/// </summary>
			public bool CreateRandomCoordinates { get; set; } = false;
		}
	}
}
