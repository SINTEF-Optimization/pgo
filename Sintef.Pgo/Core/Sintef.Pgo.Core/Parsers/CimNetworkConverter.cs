using Sintef.Pgo.Cim;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Sintef.Scoop.Utilities;

using Voltage = UnitsNet.ElectricPotential;
using Resistance = UnitsNet.ElectricResistance;
using Conductance = UnitsNet.ElectricConductance;
using CurrentFlow = UnitsNet.ElectricCurrent;
using ActivePower = UnitsNet.Power;
using ReactivePower = UnitsNet.ReactivePower;
using ApparentPower = UnitsNet.ApparentPower;

namespace Sintef.Pgo.Core.IO
{
	using Sintef.Pgo.DataContracts;
	using Sintef.Scoop.Utilities;

	/// <summary>
	/// Creates a <see cref="PowerNetwork"/> from the CIM objects in a <see cref="Sintef.Pgo.DataContracts.CimNetwork"/>.
	/// </summary>
	public class CimNetworkConverter
	{
		/// <summary>
		/// The CIM network data to convert
		/// </summary>
		public CimNetwork CimNetwork { get; }

		/// <summary>
		/// The parser that created the objects in <see cref="CimNetwork"/>, or null if not available
		/// </summary>
		public CimJsonParser NetworkParser { get; }

		/// <summary>
		/// The created network
		/// </summary>
		public PowerNetwork Network { get; private set; }

		/// <summary>
		/// Enumerates the warnings produced during network creation
		/// </summary>
		public IEnumerable<string> Warnings => _warnings.Select(kv =>
		{
			var (count, example) = kv.Value;

			string exampleString = "";
			if (example != null)
			{
				if (count == 1)
					exampleString = $", for {example.MRID}";
				else
					exampleString = $", for e.g. {example.MRID}";
			}

			string plural = "";
			if (count > 1)
				plural = "s";

			return $"{kv.Key} ({count} occurrence{plural}{exampleString})";
		});

		/// <summary>
		/// Options for creating the network
		/// </summary>
		private CimNetworkConversionOptions _options;

		/// <summary>
		/// Warnings about problems found during network conversion.
		/// message -> (# occurences, some CIM object to which the message applies)
		/// </summary>
		private Dictionary<string, (int Count, IdentifiedObject Example)> _warnings = new();

		/// <summary>
		/// Initializes the converter
		/// </summary>
		/// <param name="cimNetwork">The CIM network data to convert</param>
		/// <param name="options">Options for creating the network</param>
		/// <param name="parser">The parser that created the objects in <paramref name="cimNetwork"/>, or null if not available</param>
		public CimNetworkConverter(CimNetwork cimNetwork, CimNetworkConversionOptions options, CimJsonParser parser = null)
		{
			CimNetwork = cimNetwork ?? throw new ArgumentNullException(nameof(cimNetwork));
			_options = options ?? throw new ArgumentNullException(nameof(options));
			NetworkParser = parser;
		}

		/// <summary>
		/// Creates and returns a converter
		/// </summary>
		/// <param name="parser">The parser that contains the CIM objects to use</param>
		/// <param name="options">Options for creating the network</param>
		public static CimNetworkConverter FromParser(CimJsonParser parser, CimNetworkConversionOptions options)
		{
			var cimNetwork = CimNetwork.FromObjects(parser.CreatedObjects<IdentifiedObject>());
			return new CimNetworkConverter(cimNetwork, options, parser);
		}

		/// <summary>
		/// Creates and returns the power network
		/// </summary>
		/// <returns></returns>
		public PowerNetwork CreateNetwork()
		{
			try
			{
				CheckInput();

				Network = new PowerNetwork();

				CreateBuses();

				CreateLines();

				CreateProviders();

				CreateTransformers();

				CreateSwitches();

				CreateConsumers();

				// The Digin10 data contains a LinearShuntCompensator, which regulates reactive power.
				// We don't model such devices yet, but we probably should.
				//
				// CreateShuntCompensators()

				WarnAboutUnusedData();

				return Network;
			}
			catch (Exception ex)
			{
				throw new Exception($"An error occurred while parsing the network data: {ex.Message}");
			}
		}

		/// <summary>
		/// Performs checks on the input data that do not naturally fit in conversion process
		/// </summary>
		private void CheckInput()
		{
			if (CimNetwork.ACLineSegments == null)
				throw new ArgumentException("ACLineSegments is null");
			if (CimNetwork.GeneratingUnits == null)
				throw new ArgumentException("GeneratingUnits is null");
			if (CimNetwork.PowerTransformers == null)
				throw new ArgumentException("PowerTransformers is null");
			if (CimNetwork.Switches == null)
				throw new ArgumentException("Switches is null");
			if (CimNetwork.EnergyConsumers == null)
				throw new ArgumentException("EnergyConsumers is null");
			if (CimNetwork.EquivalentInjections == null)
				throw new ArgumentException("EquivalentInjections is null");

			if (CimNetwork.ACLineSegments.Contains(null))
				throw new ArgumentException("ACLineSegments contains null");
			if (CimNetwork.GeneratingUnits.Contains(null))
				throw new ArgumentException("GeneratingUnits contains null");
			if (CimNetwork.PowerTransformers.Contains(null))
				throw new ArgumentException("PowerTransformers contains null");
			if (CimNetwork.Switches.Contains(null))
				throw new ArgumentException("Switches contains null");
			if (CimNetwork.EnergyConsumers.Contains(null))
				throw new ArgumentException("EnergyConsumers contains null");
			if (CimNetwork.EquivalentInjections.Contains(null))
				throw new ArgumentException("EquivalentInjections contains null");

			// Verify all objects have a non-null and unique MRID
			var identifiedObjects = CimNetwork.IdentifiedObjects().ToList();

			foreach (var identifiedObject in identifiedObjects)
			{
				if (identifiedObject.MRID == null)
					Complain(identifiedObject, "No MRID is given");
			}

			if (identifiedObjects.Select(x => x.MRID).Distinct().Count() < identifiedObjects.Count)
			{
				var objectWithDuplicateMrid = identifiedObjects.GroupBy(x => x.MRID).First(g => g.Count() > 1).First();
				Complain(objectWithDuplicateMrid, "MRID is not unique");
			}

			// All base voltages must be OK.

			foreach (var baseVoltage in CimNetwork.BaseVoltages())
			{
				var voltage = baseVoltage.RequireValue(v => v.NominalVoltage, nameof(baseVoltage.NominalVoltage));
				if (voltage.Volts <= 0)
					Complain(baseVoltage, $"{nameof(baseVoltage.NominalVoltage)} must be positive");
			}

			// All voltage levels must have a base voltage

			foreach (var level in CimNetwork.VoltageLevels())
			{
				level.RequireValue(l => l.BaseVoltage, nameof(level.BaseVoltage));
			}

			// All conducting equipment must have terminals

			foreach (var equipment in CimNetwork.ConductingEquipment())
			{
				if (equipment is PowerTransformer)
					// Don't care about the terminals of transformers -- they are given in each end
					continue;

				if (equipment.Terminals == null || equipment.Terminals.Count == 0)
					Complain(equipment, "The conducting equipment has no terminals");

				// Each terminal must have a connectivity node

				foreach (var terminal in equipment.Terminals)
				{
					if (terminal == null)
						Complain(equipment, "The list of terminals contains null");

					if (terminal.ConnectivityNode == null)
						Complain(terminal, "The terminal has no connectivity node");
				}

				// The terminals must be distinct
				if (equipment.Terminals.GroupBy(t => t).FirstOrDefault(g => g.Count() >= 2) is IGrouping<Terminal, Terminal> group)
					Complain(equipment, $"{group.Key.Describe()} appears twice");

				// The terminals must connect to disctinct connectivityNodes
				if (equipment.Terminals.Select(t => t.ConnectivityNode).GroupBy(n => n).FirstOrDefault(g => g.Count() >= 2) is IGrouping<ConnectivityNode, ConnectivityNode> nodeGroup)
					Complain(equipment, $"More than one terminal is connected to {nodeGroup.Key.Describe()}");
			}

			// min/max P/Q for an EquivalentInjection must be consistent
			foreach (var injection in CimNetwork.EquivalentInjections)
			{
				bool hasLimit = injection.minP.HasValue || injection.maxP.HasValue || injection.minQ.HasValue || injection.maxQ.HasValue;
				bool hasAllLimits = injection.minP.HasValue && injection.maxP.HasValue && injection.minQ.HasValue && injection.maxQ.HasValue;

				if (hasLimit && !hasAllLimits)
					Complain(injection, "Either all of min/max P/Q must be given, or none");

				if (!hasLimit)
					continue;

				if (injection.minP > injection.maxP)
					Complain(injection, "minP is larger than maxP");

				if (injection.minP.Value.Watts < 0 && injection.maxP.Value.Watts > 0)
					Complain(injection, "minP is negative and maxP positive. This is not supported by PGO at the moment. " +
						"To model a provider, give a negative maxP. To model a consumer, give a positive or zero minP, " +
						"or leave it unspecified.");

				if (injection.minQ > injection.maxQ)
					Complain(injection, "minQ is larger than maxQ");
			}
		}

		/// <summary>
		/// Adds connection buses to the network
		/// </summary>
		private void CreateBuses()
		{
			foreach (var node in CimNetwork.ConnectivityNodes())
			{
				Network.AddTransition(0, double.MaxValue, node.MRID);
			}
		}

		/// <summary>
		/// Adds regular lines to the network
		/// </summary>
		private void CreateLines()
		{
			foreach (var segment in CimNetwork.ACLineSegments)
			{
				RequireTerminalCount(segment, 2);

				string bus1Name = segment.Terminals[0].ConnectivityNode.MRID;
				string bus2Name = segment.Terminals[1].ConnectivityNode.MRID;

				var r = segment.RequireValue(s => s.R, nameof(segment.R));
				var x = segment.RequireValue(s => s.X, nameof(segment.X));

				if (r.Ohms < 0)
					Complain(segment, "The resistance 'r' cannot be negative");

				r *= _options.LineImpedanceScaleFactor ?? 1;
				x *= _options.LineImpedanceScaleFactor ?? 1;

				Complex impedance = new Complex(r.Ohms, x.Ohms);
				double imax = FindIMax(segment);
				double vmax = double.MaxValue;

				Network.AddLine(bus1Name, bus2Name, impedance, imax, vmax, name: segment.MRID);
			}
		}

		/// <summary>
		/// Adds providers to the network
		/// </summary>
		private void CreateProviders()
		{
			if (_options.ProviderSources.Contains(CimProviderSource.GeneratingUnits))
				CreateGeneratingUnitProviders();

			if (_options.ProviderSources.Contains(CimProviderSource.EquivalentInjections))
				CreateEquivalentInjectionProviders(CimNetwork.ProviderEquivalentInjections());
		}

		/// <summary>
		/// Creates providers from <see cref="GeneratingUnit"/>s 
		/// </summary>
		private void CreateGeneratingUnitProviders()
		{
			foreach (var generator in CimNetwork.GeneratingUnits)
			{
				if (generator.RotatingMachines == null || generator.RotatingMachines.Count == 0)
					Complain(generator, "The generator has no rotating machines");

				// Determine generator voltage

				var generatorVoltage = generator.RotatingMachines
					.Common(VoltageOf, $"the voltages of rotating machines of {generator.Describe()}");

				// Determine min/max generation

				var maxP = generator.RequireValue(g => g.MaxOperatingP, nameof(generator.MaxOperatingP));
				var minP = generator.RequireValue(g => g.MinOperatingP, nameof(generator.MinOperatingP));

				if (minP.Value < 0)
					Complain(generator, $"{nameof(generator.MinOperatingP)} cannot be negative");
				if (minP > maxP)
					Complain(generator, $"{nameof(generator.MinOperatingP)} cannot be larger than {nameof(generator.MaxOperatingP)}");

				ReactivePower maxQ = ReactivePower.Zero;
				ReactivePower minQ = ReactivePower.Zero;

				foreach (var machine in generator.RotatingMachines.OfType<SynchronousMachine>())
				{
					var type = machine.RequireValue(m => m.Type, nameof(machine.Type));
					if (machine.Type != SynchronousMachineKind.Generator)
						Complain(machine, "Only type 'Generator' is supported");

					var min = machine.RequireValue(m => m.MinQ, nameof(machine.MinQ));
					var max = machine.RequireValue(m => m.MaxQ, nameof(machine.MaxQ));

					if (min > max)
						Complain(machine, $"{nameof(machine.MinQ)} cannot be larger than {nameof(machine.MaxQ)}");

					minQ += min;
					maxQ += max;
				}

				Complex generationMax = new Complex((double)maxP.Watts, maxQ.VoltamperesReactive);
				Complex generationMin = new Complex((double)minP.Watts, minQ.VoltamperesReactive);

				// Check terminal count

				RequireTerminalCount(generator.RotatingMachines, 1);

				// Create

				Network.AddProvider(generatorVoltage.Volts, generationMax, generationMin, name: generator.MRID);

				// Connect to the correct bus

				var connectivityNode = generator.RotatingMachines
						.Common(m => m.Terminals.Single().ConnectivityNode,
						$"the {nameof(Terminal.ConnectivityNode)} for rotating machines of {generator.Describe()}");

				Connect(generator.MRID, connectivityNode.MRID);
			}


			// Finds the voltage produced by the given rotating machine
			Voltage? VoltageOf(RotatingMachine machine)
			{
				if (machine.RatedU is Voltage voltage)
				{
					if (voltage.Volts <= 0)
						Complain(machine, $"{nameof(machine.RatedU)} must be positive");

					return voltage;
				}

				Warn(machine, "No rated voltage found for RotatingMachine -- falling back to container's base voltage");

				return AddToErrorMessage(() => machine.GetBaseVoltage(), $"(looking for base voltage because {nameof(machine.RatedU)} is not supplied)");
			}
		}

		/// <summary>
		/// Creates providers from <see cref="EquivalentInjection"/>s whose maxP is nonpositive
		/// </summary>
		private void CreateEquivalentInjectionProviders(IEnumerable<EquivalentInjection> injections)
		{
			RequireTerminalCount(injections, 1);

			foreach (var injection in injections)
			{
				var (providerVoltage, noVoltageReason) = FindProviderVoltage(injection);
				if (!providerVoltage.HasValue)
				{
					providerVoltage = AddToErrorMessage(() => injection.GetBaseVoltage(), $"(looking for base voltage because {noVoltageReason})");
				}

				// Here we invert the sign because injections are expressed using load sign convention, i.e. as consumption.
				ActivePower minP = injection.RequireValue(i => -i.maxP, nameof(injection.maxP));
				ActivePower maxP = injection.RequireValue(i => -i.minP, nameof(injection.minP));
				ReactivePower minQ = injection.RequireValue(i => -i.maxQ, nameof(injection.maxQ));
				ReactivePower maxQ = injection.RequireValue(i => -i.minQ, nameof(injection.minQ));

				Complex generationMin = new((double)minP.Watts, minQ.VoltamperesReactive);
				Complex generationMax = new((double)maxP.Watts, maxQ.VoltamperesReactive);

				Network.AddProvider(providerVoltage.Value.Volts, generationMax, generationMin, injection.MRID);
				
				Connect(injection.MRID, injection.Terminals[0].ConnectivityNode.MRID);
			}
		}

		private (Voltage?, string) FindProviderVoltage(EquivalentInjection injection)
		{
			var terminals = injection.Terminals[0].ConnectivityNode.Terminals;

			var transformerEnds = terminals.SelectManyUnlessNull(t => t.TransformerEnds).OfType<PowerTransformerEnd>();

			if (!transformerEnds.Any())
				return (null, "the injection is not connected to a transformer end");

			var voltages = transformerEnds.Select(end => end.RequireValue(e => e.RatedU, nameof(end.RatedU))).ToList();
			if (voltages.Count == 1)
				return (voltages[0], "");

			if (voltages.Cast<Voltage?>().Common() is Voltage v)
				return (v, "");

			return (null, "different rated voltages were found at the connected tranformer ends");
		}

		/// <summary>
		/// Adds transformers to the network
		/// </summary>
		/// <exception cref="Exception"></exception>
		private void CreateTransformers()
		{
			foreach (var transformer in CimNetwork.PowerTransformers)
			{
				var transformerEnds = transformer.PowerTransformerEnds;

				if (transformerEnds == null || transformerEnds.Count == 0)
					Complain(transformer, "The transformer has no ends");

				if (transformerEnds.Contains(null))
					Complain(transformer, "The list of transformer ends contains null");

				if (transformerEnds.Count < 2)
					Complain(transformer, "The transformer must have at least 2 ends");

				List<TransformerModeData> modes = new();

				var terminalsWithVoltages = transformerEnds.Select(end => (BusId(end), Voltage(end)));

				// Consider each pair of transformer ends
				foreach (var end1 in transformerEnds)
				{
					foreach (var end2 in transformerEnds.TakeWhile(e => e != end1))
					{
						var (highVoltageEnd, lowVoltageEnd) = (end1, end2);

						if (Voltage(lowVoltageEnd) > Voltage(highVoltageEnd))
							(highVoltageEnd, lowVoltageEnd) = (end2, end1);

						// For now, we assume modes in both directions,
						// with fixed ratio given by the base voltage levels and no loss.
						// This could be refined.

						var mode = new TransformerModeData(
							inputBusName: BusId(highVoltageEnd),
							outputBusName: BusId(lowVoltageEnd),
							operationType: TransformerOperationType.FixedRatio,
							voltageRatio: Voltage(highVoltageEnd) / Voltage(lowVoltageEnd),
							powerFactor: 1,
							bidirectional: true);

						modes.Add(mode);
					}
				}

				Network.AddTransformer(terminalsWithVoltages: terminalsWithVoltages, modes: modes, name: transformer.MRID);

				// The CIM model also contains info on tap changers, which allows you to change the voltage
				// ratio. We don't model this for now.
				// (What would happen if we added multiple modes with different voltage ratios?)
			}

			string BusId(PowerTransformerEnd end) => end.RequireValue(e => e.Terminal, nameof(end.Terminal))
				.RequireValue(t => t.ConnectivityNode, nameof(Terminal.ConnectivityNode))
				.RequireValue(n => n.MRID, nameof(IdentifiedObject.MRID));

			double Voltage(PowerTransformerEnd end)
			{
				double result = end.RequireValue(e => e.RatedU, nameof(end.RatedU)).Volts;
				if (result <= 0)
					Complain(end, $"{nameof(end.RatedU)} must be positive");
				return result;
			}
		}

		/// <summary>
		/// Adds switches to the network
		/// </summary>
		private void CreateSwitches()
		{
			foreach (var theSwitch in CimNetwork.Switches)
			{
				if (theSwitch is GroundDisconnector)
					// Not supported (for now?)
					continue;

				// Classify as switch or breaker according to the switch's CIM class.

				CimSwitchType? type = theSwitch switch
				{
					DisconnectingCircuitBreaker => CimSwitchType.DisconnectingCircuitBreaker,
					Disconnector => CimSwitchType.Disconnector,
					LoadBreakSwitch => CimSwitchType.LoadBreakSwitch,
					Breaker => CimSwitchType.Breaker,
					Fuse => CimSwitchType.Fuse,
					Sectionalizer => CimSwitchType.Sectionalizer,
					Recloser => CimSwitchType.Recloser,
					Jumper => CimSwitchType.Jumper,
					_ => null
				};

				if (type == null) {
					Warn(theSwitch, $"Don't know how to model {theSwitch.GetType().Name} - ignoring");
					continue;
				}

				bool isSwitchable = _options.ControllableSwitchTypes.Contains(type.Value);
				bool isBreaker = _options.BreakingSwitchTypes.Contains(type.Value);
				double switchingCost = 1;

				RequireTerminalCount(theSwitch, 2);

				var (fromId, toId) = theSwitch.Terminals
					.Select(t => t.ConnectivityNode.MRID);

				Network.AddLine(fromId, toId, 0, double.MaxValue, double.MaxValue, isSwitchable, switchingCost, isBreaker, theSwitch.MRID);
			}
		}

		/// <summary>
		/// Adds consumers to the network
		/// </summary>
		private void CreateConsumers()
		{
			if (_options.ConsumerSources.Contains(CimConsumerSource.EnergyConsumers))
				CreateConsumers(CimNetwork.EnergyConsumers.Where(x => x is not StationSupply));

			if (_options.ConsumerSources.Contains(CimConsumerSource.EquivalentInjections))
				CreateConsumers(CimNetwork.ConsumerEquivalentInjections());


			void CreateConsumers(IEnumerable<ConductingEquipment> loads)
			{
				RequireTerminalCount(loads, 1);

				foreach (var load in loads)
				{
					Voltage? baseVoltage = null;

					if ((_options.ConsumerMinVoltageFactor ?? _options.ConsumerMaxVoltageFactor) is not null)
					{
						// base voltage is required when a factor is given
						baseVoltage = AddToErrorMessage(() => load.GetBaseVoltage(),
							$"(looking for base voltage because option {nameof(_options.ConsumerMinVoltageFactor)} or {nameof(_options.ConsumerMaxVoltageFactor)} is given)");
					}

					Voltage vMin = (baseVoltage * _options.ConsumerMinVoltageFactor) ?? Voltage.FromVolts(0);
					Voltage vMax = (baseVoltage * _options.ConsumerMaxVoltageFactor) ?? Voltage.FromMegavolts(1000);

					Network.AddConsumer(vMin.Volts, vMax.Volts, name: load.MRID);

					Connect(load.MRID, load.Terminals.Single().ConnectivityNode.MRID);
				}
			}
		}

		/// <summary>
		/// Emits warnings if the RDF data contains data that we don't support but maybe should
		/// </summary>
		private void WarnAboutUnusedData()
		{
			var limits = CimNetwork.Equipment()
				.SelectManyUnlessNull(e => e.OperationalLimitSets)
				.SelectMany(set => set.OperationalLimits)
				.Where(l => l is not CurrentLimit);

			foreach (var limit in limits)
				Warn(limit, $"Operational limit of type {limit.GetType().Name} is not supported");

			if (NetworkParser != null)
			{
				foreach (var x in NetworkParser.CreatedObjects<ShuntCompensator>())
					Warn(x, $"Shunt compensators are not supported");
			}
		}

		/// <summary>
		/// Returns the maximal current of the given segment, in Ampere
		/// </summary>
		private double FindIMax(ACLineSegment segment)
		{
			double none = double.PositiveInfinity;

			// Find the applicable limits
			var limits = segment.Terminals.Select(t => t.OperationalLimitSets)
				.Concat(segment.OperationalLimitSets)
				.Except(null)
				.SelectMany(set => set)
				.SelectMany(set => set.OperationalLimits)
				.OfType<CurrentLimit>()
				.ToList();

			// Only AbsoluteValue limits are supported
			limits = limits
				 .Eliminate(l => l.OperationalLimitType.Direction != OperationalLimitDirectionKind.AbsoluteValue,
					l => Warn(l, $"Current limit direction {l.OperationalLimitType.Direction} is not supported -- ignoring current limit"))
				.ToList();

			// AcceptableDuration is ignored
			if (limits.FirstOrDefault(l => l.OperationalLimitType.AcceptableDuration.HasValue) is CurrentLimit withDuration)
				Warn(withDuration, "Duration of current limit is not supported -- ignoring duration");

			if (!limits.Any())
				return none;

			if (limits.Common(l => ValueToUse(l)) is double limit)
			{
				return limit;
			}
			else
			{
				Warn(segment, "Multiple different current limits is not supported -- ignoring current limits");
				return none;
			}


			double? ValueToUse(CurrentLimit limit)
			{
				foreach (var source in _options.OperationalLimitSources)
				{
					switch (source)
					{
						case CimOperationalLimitSource.Value:
							if (limit.Value.HasValue)
								return limit
									.RequireValue(l => l.Value, nameof(limit.Value))
									.Amperes;
							break;

						case CimOperationalLimitSource.NormalValue:
							if (limit.NormalValue.HasValue)
								return limit
									.RequireValue(l => l.NormalValue, nameof(limit.NormalValue))
									.Amperes;
							break;
					}
				}

				return null;
			}
		}

		/// <summary>
		/// Throws an exception if any equipment in the sequence has a numer of terminals
		/// different from <paramref name="requiredCount"/>
		/// </summary>
		private void RequireTerminalCount(IEnumerable<ConductingEquipment> equipment, int requiredCount)
		{
			foreach (var e in equipment)
				RequireTerminalCount(e, requiredCount);
		}

		/// <summary>
		/// Throws an exception if <paramref name="equipment"/> a numer of terminals
		/// different from <paramref name="requiredCount"/>
		/// </summary>
		private void RequireTerminalCount(ConductingEquipment equipment, int requiredCount)
		{
			if (equipment.Terminals.Count != requiredCount)
				Complain(equipment, $"Found {equipment.Terminals.Count} terminals -- expected {requiredCount}");
		}

		/// <summary>
		/// Connects the two named buses by adding an ideal line
		/// </summary>
		private void Connect(string busId1, string busId2)
		{
			Network.AddLine(busId1, busId2, impedance: 0, imax: double.MaxValue, vmax: double.MaxValue);
		}

		/// <summary>
		/// Adds a warning message
		/// </summary>
		/// <param name="subject">A CIM object to which the message applies</param>
		/// <param name="warningMessage">The warning message</param>
		private void Warn(IdentifiedObject subject, string warningMessage)
		{
			if (!_warnings.ContainsKey(warningMessage))
				_warnings[warningMessage] = (0, subject);

			_warnings[warningMessage] = (_warnings[warningMessage].Item1 + 1, subject);
		}

		/// <summary>
		/// Throws an exception, reporting the given problem for the given CIM object
		/// </summary>
		private void Complain(IdentifiedObject cimObject, string problem)
		{
			throw new Exception($"{cimObject.Describe()}: {problem}");
		}

		/// <summary>
		/// Returns the value of <paramref name="func"/>. If an exception is thrown, 
		/// intercepts it and adds <paramref name="toAdd"/> to the end of the error message
		/// </summary>
		public static T AddToErrorMessage<T>(Func<T> func, string toAdd)
		{
			try
			{
				return func();
			}
			catch (Exception ex)
			{
				throw new Exception($"{ex.Message} {toAdd}");
			}
		}
	}

	/// <summary>
	/// Extension methods for (or used by) <see cref="CimNetworkConverter"/>
	/// </summary>
	public static class CimNetworkConverterExtensions
	{
		/// <summary>
		/// Returns the sum of the given reactive powers
		/// </summary>
		public static ReactivePower Sum(this IEnumerable<ReactivePower> source)
		{
			return ReactivePower.FromVoltamperesReactive(source.Sum(x => x.VoltamperesReactive));
		}
	}
}
