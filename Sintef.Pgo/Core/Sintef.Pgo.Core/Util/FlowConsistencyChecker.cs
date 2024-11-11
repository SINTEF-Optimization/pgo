using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Calculates how well a given flow satisfies the power equations.
	/// </summary>
	public class FlowConsistencyChecker
	{
		/// <summary>
		/// The flow
		/// </summary>
		private IPowerFlow _flow;

		/// <summary>
		/// Statistics
		/// </summary>
		private IEnumerable<Statistic> _stats;
		private IEnumerable<Statistic> _statsWithTransformers;

		/// <summary>
		/// Initializes the checker
		/// </summary>
		/// <param name="flow">The flow to check</param>
		public FlowConsistencyChecker(IPowerFlow flow)
		{
			PowerNetwork network = flow.NetworkConfig.Network;
			_flow = flow;

			// Check that open lines have no current

			var noCurrentStat = new Statistic("NoCurrent", "I_open = 0");

			foreach (var line in flow.NetworkConfig.OpenLines)
			{
				noCurrentStat.Add(line, flow.Current(line), 0);
			}

			// Check Ohm's law for each line
			// The law is really I = (V1 - V2)/z, but we rearrange it to
			// avoid numerical errors due to V1 and V2 normally being very similar

			var currentStat = new Statistic("Current", "V1 = V2 + zI");

			foreach (var line in flow.NetworkConfig.PresentLines.Where(l => !l.IsTransformerConnection))
			{
				var v1 = flow.Voltage(line.Node1);
				var v2 = flow.Voltage(line.Node2);
				var z = line.Impedance;

				currentStat.Add(line, v1, v2 + flow.Current(line) * z);
			}


			var transformerVoltageStat = new Statistic("Transformer voltage", "V2=Transformer(V1)");
			foreach (var line in network.Lines.Where(l => l.IsTransformerConnection))
			{
				if (!(flow.NetworkConfig.TransformerModeForOutputLine(line) is Transformer.Mode mode)) continue;
				var inputVoltage = flow.Voltage(mode.InputBus);
				var outputVoltage = mode.OutputVoltage(inputVoltage);
				var flowValue = flow.Voltage(mode.OutputBus);
				transformerVoltageStat.Add(line, flowValue, outputVoltage);
			}


			// Check that (real) power loss is calculated correctly (for non-transformer lines)

			var lossStat = new Statistic("PowerLoss", "Loss = r Re(I I*)");
			foreach (var line in network.Lines.Where(l => !l.IsTransformerConnection))
			{
				var i = flow.Current(line);
				double loss = line.Resistance * (i * Complex.Conjugate(i)).Real;

				lossStat.Add(line, flow.PowerLoss(line), loss);
			}

			// Check that real power loss is calculated correctly for transformers
			var transformerLossStat = new Statistic("PowerLossTransformer", "TransformerLoss = Re( S*(1-c) )");
			foreach (var line in network.Lines.Where(l => l.IsTransformerConnection))
			{
				if (!(flow.NetworkConfig.TransformerModeForOutputLine(line) is Transformer.Mode mode)) continue;
				var S = flow.PowerFlow(line.Transformer.Bus, line);
				var flowValue = flow.PowerLoss(line);
				var eqnValue = (S * (1 - mode.PowerFactor)).Real;
				transformerLossStat.Add(line, flowValue, eqnValue);
			}

			// Check power balance over each line

			var lineBalanceStat = new Statistic("LineBalance", "S_out = S_in - z I I*");
			foreach (var line in network.Lines.Where(l => !l.IsTransformerConnection))
			{
				var i = flow.Current(line);
				var loss = line.Impedance * i * Complex.Conjugate(i);

				var sIn = flow.PowerFlow(line.Node1, line);
				var sOut = -flow.PowerFlow(line.Node2, line);

				lineBalanceStat.Add(line, sOut, sIn - loss);
			}

			// Check power balance over each transformer line
			var transformerLineBalance = new Statistic("TransformerLineBalance", "S_out = S_in*c");
			foreach (var line in network.Lines.Where(l => l.IsTransformerConnection))
			{
				if (!(flow.NetworkConfig.TransformerModeForOutputLine(line) is Transformer.Mode mode)) continue;
				var S = flow.PowerFlow(line.Transformer.Bus, line);
				var lhs = -flow.PowerFlow(mode.OutputBus, line);
				var rhs = S * mode.PowerFactor;
				transformerLineBalance.Add(line, lhs, rhs);
			}

			// Check power balance at each bus

			var nodeBalanceStat = new Statistic("NodeBalance", "S_in_upstream + S_injected = S_consumed + SUM (S_out_downstream)");

			foreach (var bus in network.Buses)
			{
				Line upstream = flow.NetworkConfig.UpstreamLine(bus);

				var powerIn = new Complex();
				if (upstream != null)
					powerIn += flow.GetPowerEjection(upstream, bus);

				var powerOut = bus.IncidentLines.Except(upstream).Select(line => flow.PowerFlow(bus, line)).ComplexSum();

				var injected = flow.PowerInjection(bus);
				if (injected.Real > 0)
					powerIn += injected;
				else
					powerOut -= injected; // Consumed

				nodeBalanceStat.Add(bus, powerIn, powerOut);
			}


			// Collect the result

			_stats = new List<Statistic>
			{
				noCurrentStat, currentStat, lossStat, lineBalanceStat, nodeBalanceStat
			};
			_statsWithTransformers = new List<Statistic>
			{
				noCurrentStat, currentStat, lossStat, lineBalanceStat, nodeBalanceStat,
				transformerLossStat, transformerVoltageStat, transformerLineBalance,
			};
		}

		/// <summary>
		/// Returns a string that summarizes how well the given flow is a solution to the
		/// power equations, ignoring transformers.
		/// </summary>
		public string Summary => _stats.Select(s => s.Summary).Concatenate("; ");

		/// <summary>
		/// Returns a string that summarizes how well the given flow is a solution to the
		/// power equations, with separate statistics for transformers.
		/// </summary>
		public string SummaryWithTransformers => _statsWithTransformers.Select(s => s.Summary).Concatenate("; ");

		/// <summary>
		/// Statistics for one equation
		/// </summary>
		private class Statistic
		{
			/// <summary>
			/// The name of the equation
			/// </summary>
			private string _name;

			/// <summary>
			/// The formula of the equation
			/// </summary>
			private string _formula;

			/// <summary>
			/// The squared sum of the observed residues
			/// </summary>
			private double _sumSquare;

			/// <summary>
			/// The largest observed magnitude of a residue
			/// </summary>
			private double _maxMagnitude;

			/// <summary>
			/// The location (bus/line) of the observed residue with the largest magnitude
			/// </summary>
			private object _maxLocation;

			/// <summary>
			/// The number of observations on the equation
			/// </summary>
			private int _count;

			/// <summary>
			/// The largest relative error observed
			/// </summary>
			private double _largestRelativeError;

			/// <summary>
			/// The root mean square of observed residues
			/// </summary>
			private double Rms => Math.Sqrt(_sumSquare / _count);

			/// <summary>
			/// Initializes a statistic
			/// </summary>
			/// <param name="name">The name of the statistic</param>
			/// <param name="formula">The formula for the equation that should hold</param>
			public Statistic(string name, string formula)
			{
				_name = name;
				_formula = formula;
			}

			/// <summary>
			/// Returns a string summarizing how well the equation is fulfilled
			/// </summary>
			public string Summary
			{
				get
				{
					if (_largestRelativeError < 1e-12)
						return $"{_name}: ok";

					return $"{_name}: rms {Format(Rms)}, max {Format(_maxMagnitude)} at {_maxLocation}";
				}
			}

			/// <summary>
			/// Adds an observation of the equation
			/// </summary>
			/// <param name="location">The bus/line that the observation is for</param>
			/// <param name="leftValue">The value of the left side of the equation</param>
			/// <param name="rightValue">The value of the right side of the equation</param>
			public void Add(object location, Complex leftValue, Complex rightValue)
			{
				double magnitude = (rightValue - leftValue).Magnitude;
				if (magnitude.IsNanOrInfinity())
					throw new Exception("Value is not a real number");

				_sumSquare += magnitude * magnitude;

				if (magnitude > _maxMagnitude)
				{
					_maxMagnitude = magnitude;
					_maxLocation = location;
				}

				if (magnitude > 0)
				{
					double largestMagnitude = Math.Max(rightValue.Magnitude, leftValue.Magnitude);
					double relativeError = magnitude / largestMagnitude;

					_largestRelativeError = Math.Max(_largestRelativeError, relativeError);
				}

				++_count;
			}

			/// <summary>
			/// Formats the given value in a shortish and predictable way
			/// </summary>
			private string Format(double value)
			{
				return value.ToString("G4", CultureInfo.InvariantCulture.NumberFormat);
			}
		}
	}
}
