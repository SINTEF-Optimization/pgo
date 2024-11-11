using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Enumeration of transformer operation types.
	/// </summary>
	public enum TransformerOperationType
	{
		/// <summary>
		/// The transformer will convert voltage with a fixed ratio.
		/// </summary>
		FixedRatio,

		/// <summary>
		/// Given allowable input, the transformer will always produce the given output voltage.
		/// </summary>
		Automatic
	}

	/// <summary>
	/// Representation of the relevant data for a transformer in the network.
	/// </summary>
	public class Transformer
	{
		/// <summary>
		/// The transformer's unique name
		/// </summary>
		public string Name { get; }

		/// <summary>
		/// The central bus that represents the transformer in the network
		/// </summary>
		public Bus Bus { get; internal set; }

		/// <summary>
		/// The defined modes for this transformer.
		/// </summary>
		public IEnumerable<Mode> Modes => _modes;

		/// <summary>
		/// The endpoints of the terminal.
		/// </summary>
		public IEnumerable<Bus> Terminals => _terminalsToVoltages.Keys;

		/// <summary>
		/// Returns an enumerable of all terminals along with the associated expexted voltages.
		/// </summary>
		public IEnumerable<(Bus terminal, double voltage)> TerminalVoltages => _terminalsToVoltages.Select(kvp => (kvp.Key, kvp.Value));

		private List<Mode> _modes = new List<Mode>();
		private Dictionary<Bus, double> _terminalsToVoltages;

		/// <summary>
		/// Creates a transformer on the given terminals with the given expected voltages
		/// </summary>
		/// <param name="terminalsWithVoltages">The buses representing transformer connections, each with the corresponding voltage (in V)</param>
		/// <param name="name">The tranformer's name</param>
		public Transformer(IEnumerable<(Bus terminal, double voltage)> terminalsWithVoltages, string name)
		{
			Name = name;

			_terminalsToVoltages = terminalsWithVoltages.ToDictionary(tup => tup.terminal, tup => tup.voltage);
		}

		/// <summary>
		/// Adds a mode according to the given specification, using bus names to identify input and output.
		/// </summary>
		/// <param name="inputBusName">Name of input side connection bus</param>
		/// <param name="operation">The type of operation (auto or fixed ratio)</param>
		/// <param name="outputBusName">Name of output side connection bus</param>
		/// <param name="voltageRatio">The voltage ratio (input voltage/output voltage). Optional. Only applicable if <paramref name="operation"/> is <see cref="TransformerOperationType.FixedRatio"/>.</param>
		/// <param name="powerFactor">The power factor, determining loss. Output power = intput power * powerFactor</param>
		/// <param name="bidirectional">Whether this mode is valid to use with output and input swapped.</param>
		public void AddMode(string inputBusName, string outputBusName, TransformerOperationType operation, double voltageRatio = 1.0, double powerFactor = 1.0, bool bidirectional = false)
		{
			Bus inputBus = Terminals.Single(b => b.Name == inputBusName);
			Bus outputBus = Terminals.Single(b => b.Name == outputBusName);
			if (inputBus == outputBus)
				throw new ArgumentException("Cannot add mode between same buses");

			_modes.Add(new Mode(this, inputBus, outputBus, operation, voltageRatio, powerFactor, derivedFromBidirectionalMode: false));

			if (bidirectional)
				_modes.Add(new Mode(this, outputBus, inputBus, operation, 1.0 / voltageRatio, powerFactor, derivedFromBidirectionalMode: true));
		}

		/// <summary>
		/// Copy over modes from the original transformer.
		/// </summary>
		/// <param name="original"></param>
		/// <param name="getBus"></param>
		internal void CopyModesFrom(Transformer original, Func<string, Bus> getBus)
		{
			foreach (var mode in original._modes)
			{
				_modes.Add(mode.CloneWithNamedBusReferences(this, getBus));
			}
		}

		/// <summary>
		/// The expected voltage for the given terminal bus.
		/// </summary>
		public double ExpectedVoltageFor(Bus b) => _terminalsToVoltages[b];

		/// <summary>
		/// Returns the mode that applies for the given input line and output line.
		/// </summary>
		public Mode ModeFor(Line inputLine, Line outputLine)
		{
			var inputBus = inputLine.OtherEnd(Bus);
			var outputBus = outputLine.OtherEnd(Bus);

			return Modes.SingleOrDefault(m => m.InputBus == inputBus && m.OutputBus == outputBus);
		}

		/// <summary>
		/// Returns true if there exists a mode which has the given line as input line, 
		/// and one mode exists for each possible output bus for that input.
		/// </summary>
		/// <param name="line"></param>
		/// <returns></returns>
		public bool HasValidModesForInputLine(Line line)
		{
			var inputBus = line.OtherEnd(Bus);
			var inputExists = Modes.Any(m => m.InputBus == inputBus);
			var hasAllOutputs = Terminals.Where(t => t != inputBus).All(t => Modes.Any(m => m.OutputBus == t));
			return inputExists && hasAllOutputs;
		}

		/// <summary>
		/// Describes an allowable operational mode of the transformer, where power flows from left
		/// to right in the following diagram:
		///   InputBus ---(line 1)--- Transformer ---(line 2)--- OutputBus
		/// Given the injection into line 1 from InputBus the TransformerMode describes
		/// how the ejection into OutputBus is computed.
		/// </summary>
		public class Mode
		{

			private double _ratio;

			/// <summary>
			/// Creates a mode 
			/// </summary>
			/// <param name="inputBus">The input bus in this mode.</param>
			/// <param name="outputBus">The output bus in this mode</param>
			/// <param name="operation">The operation style in this mode.</param>
			/// <param name="voltageRatio">The voltage ratio for the mode (only relevant if <paramref name="operation"/> == <see cref="TransformerOperationType.FixedRatio"/>. Will be ignored otherwize).</param>
			/// <param name="powerFactor">The power factor in this mode.</param>
			/// <param name="transformer">The transformer for which the mode is defined.</param>
			/// <param name="derivedFromBidirectionalMode">This mode is the inverse copy of another mode that was added as a bidirectional mode.</param>
			internal Mode(Transformer transformer, Bus inputBus, Bus outputBus, TransformerOperationType operation, double voltageRatio, double powerFactor, bool derivedFromBidirectionalMode = false)
			{
				Transformer = transformer;
				InputBus = inputBus;
				OutputBus = outputBus;
				Operation = operation;
				DerivedFromBidirectionalMode = derivedFromBidirectionalMode;
				if (powerFactor > 1 || powerFactor < 0)
					throw new ArgumentOutOfRangeException("powerFactor must be in [0.0, 1.0]");
				PowerFactor = powerFactor;

				switch (operation)
				{
					case TransformerOperationType.Automatic:
						break;
					case TransformerOperationType.FixedRatio:
						Ratio = voltageRatio;

						break;
				}
			}

			/// <summary>
			/// The transformer that this mode belongs to.
			/// </summary>
			public Transformer Transformer { get; }

			/// <summary>
			/// The bus that will act as input in this mode.
			/// </summary>
			public Bus InputBus { get; }

			/// <summary>
			/// The bus that will act as output in this mode.
			/// </summary>
			public Bus OutputBus { get; }

			/// <summary>
			/// The style of operation in this mode.
			/// </summary>
			public TransformerOperationType Operation { get; }

			/// <summary>
			/// The transformer voltage ratio in FixedRatio operation mode.
			/// Equals the input voltage divided by the output voltage.
			/// </summary>
			public double Ratio
			{
				get
				{
					if (Operation != TransformerOperationType.FixedRatio)
						throw new InvalidOperationException("Ratio not valid for non-fixed transformer");
					return _ratio;
				}
				private set
				{
					_ratio = value;
				}
			}

			/// <summary>
			/// The estimate of the loss in the transformer. PowerFactor * InputPower = OutputPower.
			/// </summary>
			public double PowerFactor { get; }

			/// <summary>
			/// This mode is the inverse copy of another mode that was added as a bidirectional mode.
			/// </summary>
			public bool DerivedFromBidirectionalMode { get; }

			/// <summary>
			/// 
			/// </summary>
			/// <param name="inVoltage"></param>
			/// <returns></returns>
			public Complex OutputVoltage(Complex inVoltage)
			{
				switch (Operation)
				{
					case TransformerOperationType.Automatic:
						return inVoltage / inVoltage.Magnitude * Transformer.ExpectedVoltageFor(OutputBus);
					case TransformerOperationType.FixedRatio:
						return inVoltage / Ratio;
					default:
						throw new Exception("Unknown TransformerOperationType");
				}
			}

			/// <summary>
			/// Checks buses, power factor and operation.
			/// </summary>
			/// <returns></returns>
			public override bool Equals(object other)
			{
				Mode om = other as Mode;
				return om.InputBus.Name == InputBus.Name && om.OutputBus.Name == OutputBus.Name && om.PowerFactor == PowerFactor && om.Operation == Operation;
			}

			/// <summary>
			/// Based on hash codes for buses, power factor and operation
			/// </summary>
			/// <returns></returns>
			public override int GetHashCode()
			{
				return ((int)(PowerFactor * 7)) + InputBus.GetHashCode() * 7 + OutputBus.GetHashCode() * 11 + ((int)Operation) * 13;
			}

			internal Mode CloneWithNamedBusReferences(Transformer t, Func<string, Bus> getBus)
			{
				return new Mode(t, getBus(InputBus.Name), getBus(OutputBus.Name), Operation, _ratio, PowerFactor, DerivedFromBidirectionalMode);
			}
		}
	}


}