using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// A helper class for setting up power networks from test code
	/// </summary>
	public class NetworkBuilder
	{
		#region Public properties

		/// <summary>
		/// The impedance given to a line that does not specify it
		/// </summary>
		public Complex DefaultImpedance { get; set; } = 1;

		/// <summary>
		/// Returns the network that has been built
		/// </summary>
		public PowerNetwork Network => _network;

		/// <summary>
		/// Returns a provider with the data that were specified
		/// using [faultsPerYear], [sectioningTime] and [repairTime] line properties.
		/// </summary>
		public ILineFaultPropertiesProvider LineFaultPropertiesProvider => _faultPropertiesProvider;

		/// <summary>
		/// Returns a provider with the data that was specified on consumption type(s) per consumer
		/// </summary>
		public ConsumerCategoryProvider ConsumerCategoryProvider => _consumerTypeProvider;

		/// <summary>
		/// Returns a network configuration for the network, using the switch settings
		/// given by [open] and [closed] line properties.
		/// </summary>
		public NetworkConfiguration Configuration => new NetworkConfiguration(_network,
			new SwitchSettings(_network, l => _lineIsOpen[l]));

		/// <summary>
		/// Returns the power demands that have been specified
		/// </summary>
		public PowerDemands Demands
		{
			get
			{
				if (_powerDemands == null)
				{
					_powerDemands = new PowerDemands(_network);

					foreach (var (bus, demand) in this._demands)
						_powerDemands.SetPowerDemand(bus, demand);
				}

				return _powerDemands;
			}
		}

		/// <summary>
		/// Returns a flow problem for the network, using the switch settings
		/// given by [open] and [closed] line properties, and the power demands
		/// that have been specified for consumer buses.
		/// </summary>
		public FlowProblem FlowProblem
		{
			get
			{
				return new FlowProblem(Configuration, Demands);
			}
		}

		/// <summary>
		/// Returns a single period configuration problem for the network, using the power demands
		/// that have been specified for consumer buses.
		/// </summary>
		public PgoProblem SinglePeriodProblem => Problem(new[] { PeriodData }, PeriodData.Name);

		/// <summary>
		/// Returns a problem using <see cref="Network"/> and the given set of periods, with demand
		/// <see cref="Demands"/> in each period.
		/// </summary>
		/// <param name="periods">The periods to use</param>
		/// <param name="name">The name of the problem</param>
		public PgoProblem Problem(IEnumerable<PeriodData> periods, string name)
		{
			return new PgoProblem(periods, _flowProvider, name);
		}

		/// <summary>
		/// Returns the single PeriodData specified by the network and demands
		/// </summary>
		public PeriodData PeriodData
		{
			get
			{
				if (_periodData == null)
					_periodData = new PeriodData(Network, Demands, _period);
				return _periodData;
			}
		}

		/// <summary>
		/// Returns the single PeriodData specified by the network and demands
		/// </summary>
		public IEnumerable<PeriodData> RepeatedPeriodData(int periodCount)
		{
			List<PeriodData> result = new List<PeriodData>();

			var data = PeriodData;

			for (int i = 0; i < periodCount; ++i)
			{
				result.Add(data);

				var nextPeriod = Period.Following(data.Period, data.Period.Length);
				data = new PeriodData(Network, data.Demands, nextPeriod);
			}

			return result;
		}

		/// <summary>
		/// Returns a single period configuration solution for the network, using the power demands
		/// and switch settings that have been specified for consumer buses.
		/// </summary>
		public PgoSolution SinglePeriodSolution => Solution(new[] { PeriodData }, PeriodData.Name);

		/// <summary>
		/// Returns a solution for the <see cref="Network"/> and the given set of periods.
		/// Demands are <see cref="Demands"/> and the configuration is <see cref="Configuration"/> in each period.
		/// </summary>
		/// <param name="periods">The periods to use</param>
		/// <param name="problemName">The name of the problem</param>
		public PgoSolution Solution(IEnumerable<PeriodData> periods, string problemName)
		{
			PgoProblem problem = Problem(periods, problemName);
			PgoSolution solution = new PgoSolution(problem);

			foreach (var periodData in problem.AllPeriodData)
			{
				PeriodSolution periodSolution = new PeriodSolution(periodData, Configuration.SwitchSettings);
				solution.UpdateSolutionForPeriod(periodSolution);
			}

			return solution;
		}

		/// <summary>
		/// Returns a solution for the given problem.
		/// The configuration is <see cref="Configuration"/> in each period.
		/// </summary>
		public PgoSolution Solution(PgoProblem problem)
		{
			PgoSolution solution = new PgoSolution(problem);

			foreach (var periodData in problem.AllPeriodData)
			{
				PeriodSolution periodSolution = new PeriodSolution(periodData, Configuration.SwitchSettings);
				solution.UpdateSolutionForPeriod(periodSolution);
			}

			return solution;
		}

		/// <summary>
		/// Returns a single period configuration problem for the network, using the power demands
		/// that have been specified for consumer buses.
		/// </summary>
		public PgoProblem OnePeriodProblem
		{
			get
			{
				var singlePeriodForecast = new List<PeriodData> { PeriodData };
				return new PgoProblem(singlePeriodForecast, _flowProvider, "OnePeriodProblem", Configuration, null);
			}
		}

		/// <summary>
		/// Returns a provider that calculates power consumption as equal to the specified demands
		/// </summary>
		public ExpectedConsumptionProvider ConsumptionFromDemand => new ExpectedConsumptionFromDemand(new[] { PeriodData });

		#endregion

		#region Private data members

		/// <summary>
		/// The voltage given to a generator that does not specify it
		/// </summary>
		private double _defaultGeneratorVoltage = 1;

		/// <summary>
		/// The demand given to a consumer that does not specify it
		/// </summary>
		private double _defaultConsumerDemand = 1;

		/// <summary>
		/// The network being built
		/// </summary>
		private PowerNetwork _network;

		/// <summary>
		/// Contains the line fault properties read
		/// </summary>
		private LineFaultPropertiesProvider _faultPropertiesProvider;

		/// <summary>
		/// The period for which consumer demands are given
		/// </summary>
		private Period _period;

		/// <summary>
		/// Contains the consumer types read
		/// </summary>
		private ConsumerCategoryProvider _consumerTypeProvider;

		/// <summary>
		/// Power demands for the consumer buses
		/// </summary>
		private Dictionary<Bus, Complex> _demands;

		/// <summary>
		/// Holds the final demands. Lazily built from _demands
		/// </summary>
		private PowerDemands _powerDemands;

		/// <summary>
		/// The single period data. Lazily built.
		/// </summary>
		private PeriodData _periodData;

		/// <summary>
		/// Configuration settings for the switchable lines
		/// </summary>
		private Dictionary<Line, bool> _lineIsOpen;

		/// <summary>
		/// Counter for assigning names to the anonymous nodes we create for input
		/// like "N1 -- Line1 -o- line2 -- N2"
		/// </summary>
		private int _anonymousNodeCounter = 1;

		/// <summary>
		/// The flow provider to set for the created problem
		/// </summary>
		private IFlowProvider _flowProvider;

		#endregion

		/// <summary>
		/// Initializes a network builder
		/// </summary>
		/// <param name="flowProvider">The default flow provider to set for the created problem.
		///   If null, uses Simplified DistFlow.</param>
		public NetworkBuilder(IFlowProvider flowProvider = null)
		{
			_flowProvider = flowProvider ?? new SimplifiedDistFlowProvider();
			_period = Period.Default;
			_network = new PowerNetwork();
			_demands = new Dictionary<Bus, Complex>();
			_lineIsOpen = new Dictionary<Line, bool>();

			_faultPropertiesProvider = _network.PropertiesProvider;
			_consumerTypeProvider = _network.CategoryProvider;
		}

		/// <summary>
		/// Initializes a network builder by passing the given network
		/// description to <see cref="Add"/>
		/// </summary>
		/// <param name="networkDescription">The network description</param>
		/// <returns>The network builder</returns>
		public static NetworkBuilder Create(params string[] networkDescription)
		{
			NetworkBuilder builder = new NetworkBuilder();
			foreach (var line in networkDescription)
				builder.Add(line);

			return builder;
		}

		#region Public methods

		/// <summary>
		/// Sets the given period as the time interval for which consumer demands are given
		/// </summary>
		public void UsePeriod(Period period)
		{
			_period = period;
		}

		/// <summary>
		/// Adds lines and/or nodes to the network.
		/// Any description valid for <see cref="AddBus"/>, 
		/// <see cref="AddLine(string)"/>, <see cref="AddTransformer(String)"/> or <see cref="AddLines"/> may be given.
		/// </summary>
		public void Add(string description)
		{
			if (description.Contains(" -- "))
				AddLines(description);
			else
				AddBus(description);
		}

		/// <summary>
		/// Adds a bus to the network.
		///  
		/// If the node has consumer properties, it becomes a consumer bus.
		/// If the node has producer properties, it becomes a producer bus.
		/// If the node has the transformer property it will be a transformer-connected bus.
		/// Otherwise, it becomes a connection bus.
		/// Any properties not speficied are given default values.
		/// If a bus with the name already exists, it is returned, and no properties may be given.
		///
		/// Examples:
		///  AddBus("Node1")  // Connection bus
		///  AddBus("Consumer1[consumption=(1, 0.2)]")     // VA
		///  AddBus("Producer1[generatorVoltage=1000.0]")  // V
		///  AddBus("Transformer1[transformer;ends=(node1,node2);voltages=(1000,500);operation=fixed;factor=0.98]")
		/// </summary>
		/// <param name="description">The bus name, or a fuller description, in format: 
		///   "name[property1=value1; property2=value2; property3; ... ]"</param>
		/// <returns>The bus</returns>
		public Bus AddBus(string description)
		{
			var (name, properties) = Parse(description);

			if (properties.ContainsKey("transformer"))
				return AddTransformer(name, properties).Bus;

			if (_network.HasBus(name))
			{
				if (properties.Any())
					throw new Exception("Cannot add properties to an existing bus");

				return _network.GetBus(name);
			}

			// Set default properties

			var type = BusTypes.Connection;
			Complex consumption = new Complex();
			string consumptionType = null;
			double generatorVoltage = 0;
			Complex generationCapacity = new Complex(1e20, 1e20);
			Complex generationLowerBound = Complex.Zero;
			double vMinVolts = 0;
			double vMaxVolts = 1e10;

			// Parse properties

			foreach (var (key, value) in properties)
			{
				switch (key)
				{
					case "consumption":

						type = BusTypes.PowerConsumer;
						consumption = ParseComplex(value);
						break;

					case "consumer":

						type = BusTypes.PowerConsumer;
						consumption = _defaultConsumerDemand;
						break;

					case "type":

						consumptionType = value;
						break;

					case "vMinV":

						vMinVolts = ParseDouble(value);
						break;

					case "vMaxV":

						vMaxVolts = ParseDouble(value);
						break;

					case "generatorVoltage":

						type = BusTypes.PowerProvider;
						generatorVoltage = ParseDouble(value);
						break;

					case "generator":
					case "provider":

						type = BusTypes.PowerProvider;
						generatorVoltage = _defaultGeneratorVoltage;
						break;

					case "generationCapacity":

						generationCapacity = ParseComplex(value);
						break;

					case "generationLowerBound":

						generationLowerBound = ParseComplex(value);
						break;

					default:
						throw new ArgumentException($"Unknown property {key}");
				}
			}

			// Create the bus

			double vMin = vMinVolts;
			double vMax = vMaxVolts;

			Bus bus = null;
			switch (type)
			{
				case BusTypes.Connection:
					bus = _network.AddTransition(vMin, vMax, name);
					break;

				case BusTypes.PowerConsumer:
					bus = _network.AddConsumer(vMin, vMax, name);
					_demands.Add(bus, consumption);
					break;

				case BusTypes.PowerProvider:
					bus = _network.AddProvider(generatorVoltage, generationCapacity, generationLowerBound, name);
					break;

				default:
					throw new Exception();
			}

			if (consumptionType != null)
				ParseConsumptionType(bus, consumptionType);

			return bus;
		}

		/// <summary>
		/// Adds a transformer to the network.
		///  
		/// Any properties not speficied are given default values.
		/// If a bus with the name already exists, it is returned, and no properties may be given.
		///
		/// Examples:
		///  AddTransformer("Transformer1[transformer;ends=(node1,node2);voltages=(1000,500);operation=fixed;factor=0.98]")
		/// </summary>
		/// <param name="description">The transformer description, in format: 
		///   "name[property1=value1; property2=value2; property3; ... ]"</param>
		/// <returns>The transformer</returns>
		public Transformer AddTransformer(string description)
		{
			var (name, properties) = Parse(description);

			return AddTransformer(name, properties);
		}

		/// <summary>
		/// Adds a line to the network.
		/// 
		/// The <paramref name="description"/> format is 
		///  "from -- name[property1=value1; property2=value2; property3; ... ] -- to".
		/// "from" and "to" are bus descriptions given to <see cref="AddBus"/>.
		/// Any properties not speficied are given default values.
		/// 
		/// Examples:
		///  AddLine("Node1 -- Line -- Node2")
		///  AddLine("Node1 -- Line[r=0.7] -- Node2[consumption=(1, 0.2)]")
		///  AddLine("Node1 -- Trafo[transformer, voltages=(100,50)] -- Node2")
		/// </summary>
		/// <param name="description">The line description</param>
		public Line AddLine(string description)
		{
			var (fromDescription, lineDescription, toDescription) = description.Split(new[] { " -- " }, StringSplitOptions.None);

			return AddLine(fromDescription, lineDescription, toDescription);
		}

		/// <summary>
		/// Adds one or more lines to the network.
		/// 
		/// Examples:
		///  AddLines("Node1 -- Line[r=0.7] -- Node2[consumption=(1, 0.2)] -- Line2 -- Node3")
		///  AddLines("Node1 -- Line[r=0.7] -o- Switch[closed] -- Node3")
		///  
		/// In the latter example, an anonymous node is created between Line and Switch.
		/// </summary>
		/// <param name="description">The line(s) description</param>
		public void AddLines(string description)
		{
			var items = description
				.Split(new[] { " -- " }, StringSplitOptions.None)
				.SelectMany(s => InsertAnonymousNodes(s))
				.Select(s => s.Trim())
				.ToArray();

			if (items.Count() % 2 == 0)
				throw new ArgumentException("Nodes and lines must alternate: Node -- line -- node -- ... -- node");

			// To account for transformers we'll add everything in the following order
			// 1. Buses
			// 2. Transformers with connecting lines (Automatic when adding transformer)
			// 3. Other lines

			// Could be nicer with slicing, but wthat requires C# 8
			Regex transformerPropertyRegex = new Regex(@"[\[;]transformer[\];]", RegexOptions.IgnoreCase);

			// Add all buses
			for (int i = 0; i < items.Count(); i += 2)
			{
				var item = items[i];
				if (!transformerPropertyRegex.IsMatch(item))
				{
					AddBus(item);
				}
			}

			// Now to add transformers
			Regex endsPropertyRegex = new Regex(@"[\[;]ends=", RegexOptions.IgnoreCase);
			for (int i = 0; i < items.Count(); i += 2)
			{
				var item = items[i];
				if (transformerPropertyRegex.IsMatch(item))
				{
					// If ends property not given we'll infer and insert it
					if (!endsPropertyRegex.IsMatch(item))
					{
						if (i == 0 || i + 2 >= items.Count())
							throw new ArgumentException("Can only infer transformer ends in interior of branch!");
						string prevNode = GetName(items[i - 2]);
						string prevLine = GetName(items[i - 1]);
						string nextLine = GetName(items[i + 1]);
						string nextNode = GetName(items[i + 2]);
						item = item.Insert(item.IndexOf(']'), $";ends=({prevNode},{nextNode});lines=({prevLine},{nextLine})");
					}

					AddTransformer(item);
				}
			}

			// And the lines
			for (int i = 0; i + 2 < items.Count(); i += 2)
			{
				// Strip properties from node description when repeating it
				string fromDescription = GetName(items[i]);
				string toDescription = GetName(items[i + 2]);

				AddLine(fromDescription, items[i + 1], toDescription);
			}
		}

		/// <summary>
		/// Gets the name from a node/line with or without properties (i.e. strip properties if present)
		/// </summary>
		private static string GetName(string item)
		{
			return item.Contains('[') ? item.Substring(0, item.IndexOf('[')) : item;
		}

		/// <summary>
		/// Converts a description of one or more connected lines into a line/node/line... sequence
		/// by inserting anonymous nodes in place of '-o-'.
		/// 
		/// Example: "L1 -o- Line -o- L3" becomes "L1", "_anon_1", "Line", "_anon_2", "L3"
		/// </summary>
		private IEnumerable<string> InsertAnonymousNodes(string linesDescription)
		{
			var lines = linesDescription
				.Split(new[] { " -o- " }, StringSplitOptions.None)
				.ToArray();

			yield return lines.First();

			foreach (var line in lines.Skip(1))
			{
				yield return $"_anon_{_anonymousNodeCounter++}";
				yield return line;
			}
		}

		/// <summary>
		/// Returns the bus with the given name
		/// </summary>
		public Bus Bus(string name)
		{
			return _network.GetBus(name);
		}

		/// <summary>
		/// Returns the power demand of the given consumer bus
		/// </summary>
		public Complex Demand(Bus consumer)
		{
			return _demands[consumer];
		}

		/// <summary>
		/// Writes the contents of the given network, configuration and demands to the given writer,
		/// in the format understood by <see cref="NetworkBuilder"/>
		/// </summary>
		public static void Write(TextWriter writer, PowerNetwork network, NetworkConfiguration configuration, PowerDemands powerDemands)
		{
			foreach (var provider in network.Providers)
			{
				var properties = new List<string> {
					$"generatorVoltage={provider.GeneratorVoltage}",
					$"generationCapacity={Format(provider.GenerationCapacity)}"
				};

				if (provider.GenerationLowerBound != 0)
					properties.Add($"generationLowerBound={Format(provider.GenerationLowerBound)}");

				writer.WriteLine($"{provider.Name}[{properties.Concatenate("; ")}]");
			}

			foreach (var consumer in network.Consumers)
			{
				writer.WriteLine($"{consumer.Name}[consumption={Format(powerDemands.PowerDemand(consumer))}]");
			}

			foreach (var line in network.Lines)
			{
				string properties = "";
				properties = $"z={Format(line.Impedance)}";
				if (line.IsSwitchable)
				{
					if (configuration.IsOpen(line))
						properties += "; open";
					else
						properties += "; closed";
				}
				writer.WriteLine($"{line.Node1.Name} -- {Sanitize(line.Name)}[{properties}] -- {line.Node2.Name}");
			}
		}

		/// <summary>
		/// Creates a new network builder and populates it using the text lines
		/// found on the given stream 
		/// </summary>
		public static NetworkBuilder Read(Stream stream)
		{
			var reader = new StreamReader(stream);
			var builder = new NetworkBuilder();

			while (!reader.EndOfStream)
			{
				string line = reader.ReadLine();
				if (!line.NullOrEmpty())
					builder.Add(line);
			}

			return builder;
		}

		#endregion

		#region Private methods

		/// <summary>
		/// Adds a line.
		/// If the description has the property 'transformer', adds a transformer instead.
		/// </summary>
		/// <param name="fromDescription">The from node description</param>
		/// <param name="lineDescription">The line description (without nodes)</param>
		/// <param name="toDescription">The to node description</param>
		/// <returns></returns>
		private Line AddLine(string fromDescription, string lineDescription, string toDescription)
		{
			// Add or get the nodes
			var from = AddBus(fromDescription);
			var to = AddBus(toDescription);

			// Parse properties

			var (name, properties) = Parse(lineDescription);

			// If the line is a connecting line it was added by the transformer
			if (from.IsTransformer || to.IsTransformer)
			{
				if (properties.Any())
					throw new ArgumentException("Cannot give properties to transformer connection string!");
				return null;
			}

			if (properties.ContainsKey("transformer"))
			{
				AddTransformer(name, properties, new[] { fromDescription, toDescription });
				return null;
			}

			Complex impedance = DefaultImpedance;
			bool? isOpen = null;
			bool isBreaker = false;

			double iMax = 100;
			double vMax = double.PositiveInfinity;

			double faultsPerYear = 0;
			TimeSpan? sectioningTime = null;
			TimeSpan? repairTime = null;

			foreach (var (key, value) in properties)
			{
				switch (key)
				{
					case "r":

						impedance = new Complex(value.ParseInvariantDouble(), 0);
						break;

					case "z":

						impedance = ParseComplex(value);
						break;

					case "open":

						isOpen = true;
						break;

					case "closed":

						isOpen = false;
						break;

					case "breaker":

						isBreaker = true;
						break;

					case "iMax":

						iMax = value.ParseInvariantDouble();
						break;

					case "vMax":

						vMax = value.ParseInvariantDouble();
						break;

					case "faultsPerYear":

						faultsPerYear = value.ParseInvariantDouble();
						break;

					case "sectioningTime":

						sectioningTime = value.ParseISOTimeSpan();
						break;

					case "repairTime":

						repairTime = value.ParseISOTimeSpan();
						break;

					default:
						throw new ArgumentException($"Unknown property {key}");
				}
			}

			// Create the line
			bool isSwitchable = (isOpen != null);
			var line = _network.AddLine(from.Name, to.Name, impedance, iMax, vMax, isSwitchable, isBreaker: isBreaker, name: name);

			if (isSwitchable)
				_lineIsOpen[line] = isOpen.Value;

			if (faultsPerYear == 0)
			{
				if (sectioningTime != null || repairTime != null)
					throw new ArgumentException("sectioningTime and repairTime are valid only if faultsPerYear is given");
			}
			else
			{
				if (sectioningTime == null || repairTime == null)
					throw new ArgumentException("sectioningTime and repairTime are requred if faultsPerYear is given");

				_faultPropertiesProvider.Add(line, faultsPerYear, sectioningTime.Value, repairTime.Value);
			}

			return line;
		}

		/// <summary>
		/// Adds a tranformer
		/// </summary>
		/// <param name="name">The transformer's name</param>
		/// <param name="properties">The transformer properties</param>
		/// <param name="terminals">The names of the buses to connect to. If null, the 'ends'
		///   property must be given. If not null, 'ends' may not be given.</param>
		/// <param name="upstreamTerminals">The terminals that can be used as upstream ends.
		/// If null, the 'upstream' property may be given. By default, all terminals are allowed 
		/// upstream terminals. If not null, 'upstreamTerminals' may not be given.</param>
		/// <param name="lineNames">The names of the connection lines. If null, the 'lines'
		///   property may be given. If not null, 'lines' may not be given.</param>
		/// <returns>The transformer</returns>
		private Transformer AddTransformer(string name, Dictionary<string, string> properties,
			string[] terminals = null, string[] upstreamTerminals = null, string[] lineNames = null)
		{
			if (!properties.ContainsKey("transformer"))
				throw new ArgumentException("Missing property 'transformer' for a transformer");

			List<double> terminalVoltages = new List<double>();
			TransformerOperationType operation = TransformerOperationType.FixedRatio;
			double powerFactor = 1.0;

			foreach (var (key, value) in properties)
			{
				switch (key)
				{
					case "transformer":
						break;

					case "ends":
						if (terminals != null)
							throw new Exception("Cannot give transformer ends here");
						terminals = ParseList(value).ToArray();
						break;

					case "upstream":
						if (upstreamTerminals != null)
							throw new Exception("Cannot give upstream terminals here");
						upstreamTerminals = ParseList(value).ToArray();
						break;

					case "lines":
						if (lineNames != null)
							throw new Exception("Cannot give line names here");
						lineNames = ParseList(value).ToArray();
						break;

					case "voltages":
						terminalVoltages = ParseList(value).Select(v => ParseDouble(v)).ToList();
						break;

					case "operation":
						switch (value)
						{
							case "fixed":
								operation = TransformerOperationType.FixedRatio;
								break;
							case "auto":
								operation = TransformerOperationType.Automatic;
								break;
							default:
								throw new ArgumentException($"Unknown transformer operation type {value}");
						}
						break;

					case "factor":
						powerFactor = ParseDouble(value);
						break;

					default:
						throw new ArgumentException($"Unknown property {key}");
				}
			}

#if NET7_0_OR_GREATER
			IEnumerable<(string First, double Second)> terminalsWithVoltages = terminals.Zip(terminalVoltages);
#else
			IEnumerable<(string First, double Second)> terminalsWithVoltages = terminals.Zip(terminalVoltages).ToValuePairs();
#endif
			Transformer transformer = _network.AddTransformer(terminalsWithVoltages, modes: null, name, lineNames: lineNames);

			switch (transformer.Terminals.Count())
			{
				case 2: // All two-winding transformers treated as bidirectional

					if (upstreamTerminals?.Contains(terminals[0]) ?? true)
						transformer.AddMode(terminals[0], terminals[1], operation, terminalVoltages[0] / terminalVoltages[1], powerFactor);

					if (upstreamTerminals?.Contains(terminals[1]) ?? true)
						transformer.AddMode(terminals[1], terminals[0], operation, terminalVoltages[1] / terminalVoltages[0], powerFactor);
					break;

				case 3: // All three-winding transformers have six possible directions

					if (upstreamTerminals?.Contains(terminals[0]) ?? true)
						transformer.AddMode(terminals[0], terminals[1], operation, terminalVoltages[0] / terminalVoltages[1], powerFactor);

					if (upstreamTerminals?.Contains(terminals[1]) ?? true)
						transformer.AddMode(terminals[1], terminals[0], operation, terminalVoltages[1] / terminalVoltages[0], powerFactor);

					if (upstreamTerminals?.Contains(terminals[0]) ?? true)
						transformer.AddMode(terminals[0], terminals[2], operation, terminalVoltages[0] / terminalVoltages[2], powerFactor);

					if (upstreamTerminals?.Contains(terminals[2]) ?? true)
						transformer.AddMode(terminals[2], terminals[0], operation, terminalVoltages[2] / terminalVoltages[0], powerFactor);

					if (upstreamTerminals?.Contains(terminals[1]) ?? true)
						transformer.AddMode(terminals[1], terminals[2], operation, terminalVoltages[1] / terminalVoltages[2], powerFactor);

					if (upstreamTerminals?.Contains(terminals[2]) ?? true)
						transformer.AddMode(terminals[2], terminals[1], operation, terminalVoltages[2] / terminalVoltages[1], powerFactor);

					break;

				default:
					throw new Exception("Can only add two-or-three winding transformers");
			}

			return transformer;
		}

		/// <summary>
		/// Parses a node or line description into a name and properties
		/// </summary>
		/// <param name="description"></param>
		/// <returns></returns>
		private static (string Name, Dictionary<string, string> Properties) Parse(string description)
		{
			var name = description;
			var properties = new Dictionary<string, string>();

			if (!description.Contains("["))
				// No properties are given
				return (name, properties);

			// Split name from properties

			string propertyString;
			(name, propertyString) = description.Split('[');

			if (propertyString.Last() != ']')
				throw new ArgumentException("Format: name[property1=...; property2=...; ...]");
			propertyString = propertyString.Split(']').Single(s => s.Length > 0);

			// Parse each property

			foreach (var property in propertyString.Split(';'))
			{
				if (property.Contains("="))
				{
					var (key, value) = property.Split(new[] { '=' });

					properties.Add(key.Trim(), value);
				}
				else
					properties.Add(property.Trim(), null);
			}

			return (name, properties);
		}

		/// <summary>
		/// Parses and records the value of a [type=] consumer property. The value is either a <see cref="ConsumerCategory"/>,
		/// or a distribution over consumption types that add to 1, e.g. 'Agriculture/0.2,Industry/0.8'.
		/// </summary>
		private void ParseConsumptionType(Bus consumer, string consumptionType)
		{
			var parts = consumptionType.Split(new[] { ',' });

			if (parts.CountIs(1))
			{
				// Parse single consumer type
				var type = (ConsumerCategory)Enum.Parse(typeof(ConsumerCategory), parts[0]);
				_consumerTypeProvider.Set(consumer, type);
				return;
			}

			double sum = 0;
			foreach (var part in parts)
			{
				// Parse one part of the distribution
				var (typeString, fractionString) = part.Split(new[] { '/' });

				var type = (ConsumerCategory)Enum.Parse(typeof(ConsumerCategory), typeString);
				double fraction = fractionString.ParseInvariantDouble();

				_consumerTypeProvider.Set(consumer, type, fraction);
				sum += fraction;
			}

			if (sum != 1)
				throw new ArgumentException($"Consumption type '{consumptionType}' does not add up to 1");
		}

		/// <summary>
		/// Parses a double number, e.g. "3.4"
		/// </summary>
		private static double ParseDouble(string value)
		{
			return double.Parse(value, CultureInfo.InvariantCulture);
		}

		//Parses a tuple to a list of strings
		private static IEnumerable<string> ParseList(string value)
		{
			value = value.Trim();
			if (value.StartsWith("(") && value.EndsWith(")"))
			{
				value = value.Substring(1, value.Length - 2);
				var values = value.Split(',').Select(v => v.Trim());
				return values;
			}
			throw new Exception("Incorrectly formatted list");
		}

		/// <summary>
		/// Parses a complex number. Example: "(4, 7)".
		/// Also accepts a normal double
		/// </summary>
		private static Complex ParseComplex(string value)
		{
			value = value.Trim();
			if (value.StartsWith("(") && value.EndsWith(")"))
			{
				var (re, im) = new Regex(@"[(](.+),\s*(.+)[)]").Match(value).Groups.Cast<Group>().Skip(1).Select(g => g.Value);
				return new Complex(re.ParseInvariantDouble(), im.ParseInvariantDouble());
			}

			return new Complex(value.ParseInvariantDouble(), 0);
		}

		/// <summary>
		/// Formats a Complex to a machine-independent string
		/// </summary>
		private static string Format(Complex value)
		{
			return value.ToString("g", CultureInfo.InvariantCulture);
		}

		/// <summary>
		/// Strips bad characters from a line name
		/// </summary>
		private static string Sanitize(string name)
		{
			return name.Replace(" ", "");
		}

#endregion
	}
}
