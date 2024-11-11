using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Sintef.Scoop.Utilities;

using Sintef.Scoop.Kernel;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Represents all physical variables computed by the simplified DistFlow
	/// approximation. See documentation\DistFlow.tex
	/// for details.
	/// </summary>
	public class RadialFlow : IPowerFlow
	{
		#region Public properties

		/// <summary>
		/// The network configuration this flow is for
		/// </summary>
		public NetworkConfiguration NetworkConfig { get; }

		/// <summary>
		/// The demands this flow is for
		/// </summary>
		public PowerDemands Demands { get; }

		/// <summary>
		/// The status of the flow
		/// </summary>
		public FlowStatus Status { get; internal set; }

		/// <summary>
		/// A string describing the flow's status in more detail
		/// </summary>
		public string StatusDetails { get; internal set; }

		/// <summary>
		/// If true, line power loss is ignored when calculating the power ejected from
		/// a line; the ejected power equals the injected power.
		/// Power loss in transformer lines is also ignored.
		/// This is consistent with the <see cref="SimplifiedDistFlowProvider"/> approximation.
		/// 
		/// If false, the current-dependent power loss is subtracted when
		/// calculating the ejected power.
		/// Also, the power-dependent transformer loss is subtracted in
		/// transformer output lines.
		/// This is appropriate for a fully consistent flow (e.g. created by <see cref="IteratedDistFlowProvider"/>)
		/// </summary>
		public bool IgnoreLinePowerLoss { get; set; }

		#endregion

		#region Private data members

		/// <summary>
		/// Complex voltage for all buses. Unit V.
		/// </summary>
		private Dictionary<Bus, Complex> _V;

		/// <summary>
		/// Complex nominal voltage for all buses. Unit V.
		/// </summary>
		private Dictionary<Bus, Complex> _V0;

		/// <summary>
		/// For generator buses, the power injected by the generator.
		/// Unit VA.
		/// </summary>
		private Dictionary<Bus, Complex> _SGen;

		/// <summary>
		/// Complex power injected into each line, 
		/// in the downstream direction, from the upstream bus.
		/// Unit VA.
		/// </summary>
		private Dictionary<Line, Complex> _S;

		/// <summary>
		/// The current flowing in the line, in the downstream direction.
		/// Unit A.
		/// </summary>
		private Dictionary<Line, Complex> _I;

		/// <summary>
		/// If not null, this dictionary identifies the upstream end of each line.
		/// This is necessary when the network configuration is not fully radial, in 
		/// particular for a flow that is the result of disaggregation over parallel lines.
		/// </summary>
		private Dictionary<Line, Bus> _upstreamEnd;

		#endregion

		#region Construction

		/// <summary>
		/// New, empty radial flow for the given network configuration and demands.
		/// </summary>
		/// <param name="networkConfig"></param>
		/// <param name="demands"></param>
		/// <param name="ignoreLinePowerLoss"></param>
		/// <param name="upstreamEnd"></param>
		public RadialFlow(NetworkConfiguration networkConfig, PowerDemands demands, bool ignoreLinePowerLoss = true, Dictionary<Line, Bus> upstreamEnd = null)
		{
			NetworkConfig = networkConfig;
			Demands = demands;
			IgnoreLinePowerLoss = ignoreLinePowerLoss;
			_upstreamEnd = upstreamEnd;

			_V = new Dictionary<Bus, Complex>();
			_V0 = new Dictionary<Bus, Complex>();
			_SGen = new Dictionary<Bus, Complex>();
			_S = new Dictionary<Line, Complex>();
			_I = new Dictionary<Line, Complex>();
		}

		/// <summary>
		/// Creates a copy of the given flow
		/// </summary>
		/// <param name="other">The flow to copy</param>
		/// <param name="configuration">The configuration to use in the new flow</param>
		public RadialFlow(RadialFlow other, NetworkConfiguration configuration)
		{
			NetworkConfig = configuration;
			Demands = other.Demands;
			IgnoreLinePowerLoss = other.IgnoreLinePowerLoss;
			Status = other.Status;
			StatusDetails = other.StatusDetails;

			_V = new Dictionary<Bus, Complex>(other._V);
			_V0 = new Dictionary<Bus, Complex>(other._V0);
			_SGen = new Dictionary<Bus, Complex>(other._SGen);
			_S = new Dictionary<Line, Complex>(other._S);
			_I = new Dictionary<Line, Complex>(other._I);
		}

		#endregion

		#region Public methods

		/// <summary>
		/// Checks that the given flow is equal to this one, within the given tolerance.
		/// </summary>
		/// <param name="other"></param>
		/// <param name="tolerance"></param>
		/// <returns></returns>
		public bool PhysicallyEqualWithTolerance(RadialFlow other, double tolerance)
		{
			return _V.All(bv => bv.Value.ComplexEqualsWithTolerance(other._V[bv.Key], tolerance)) &&
				 _SGen.All(bs => bs.Value.ComplexEqualsWithTolerance(other._SGen[bs.Key], tolerance)) &&
					_S.All(bi => bi.Value.ComplexEqualsWithTolerance(other._S[bi.Key], tolerance)) &&
					_I.All(bi => bi.Value.ComplexEqualsWithTolerance(other._I[bi.Key], tolerance));
		}



		#region IPowerFlow implementation

		/// <summary>
		/// Returns the power injection at the bus. Positive injection is power produced
		/// at the bus and injected into the rest of the network.
		/// If the injection has a negative real part, the bus consumes power.
		/// In VA.
		/// </summary>
		/// <param name="bus">The bus in question.</param>
		public Complex PowerInjection(Bus bus)
		{
			switch (bus.Type)
			{
				case BusTypes.Connection:
				case BusTypes.PowerTransformer:
					return 0;
				case BusTypes.PowerProvider:
					return _SGen[bus];
				case BusTypes.PowerConsumer:
					return -Demands.PowerDemand(bus);
				default:
					throw new NotImplementedException("SimplifiedDistFlowFlow.PowerInjection: Bus type not supported.");
			}
		}

		/// <summary>
		/// Returns the power that flows from the given <paramref name="bus"/>
		/// into the given <paramref name="line"/>.
		/// If the bus actually receives power from the line, the result
		/// ('s real part) will be negative. The unit is VA.
		/// </summary>
		/// <param name="bus">The bus from which the power flows into the line</param>
		/// <param name="line">The line into which the power flows</param>
		public Complex PowerFlow(Bus bus, Line line)
		{
			if (IsOpen(line))
				return 0;

			if (bus == UpstreamEnd(line))
			{
				// Return injected power into line
				return _S.TryGetValue(line, out var value) ? value : Complex.Zero;
			}

			var ejectedPower = _S[line];

			if (!IgnoreLinePowerLoss)
			{
				if (line.IsTransformerConnection)
				{
					if (NetworkConfig.TransformerModeForOutputLine(line) is Transformer.Mode mode)
					{
						ejectedPower *= mode.PowerFactor;
					}
				}
				else
				{
					var current = Current(line);
					ejectedPower -= line.Impedance * (current * Complex.Conjugate(current));
				}
			}

			return -ejectedPower;
		}


		/// <summary>
		/// Computes the current flowing from Node1 to Node2 in the line. In A.
		/// </summary>
		public Complex Current(Line line)
		{
			if (IsOpen(line))
				return 0;

			var start = line.Node1;
			var end = line.Node2;

			if (UpstreamEnd(line) == start)
			{
				return _I.TryGetValue(line, out var value) ? value : Complex.Zero;
			}
			else
			{
				return -(_I.TryGetValue(line, out var value) ? value : Complex.Zero);
			}
		}



		/// <summary>
		/// The voltage at the bus. In V.
		/// </summary>
		/// <param name="bus">The bus in question.</param>
		public Complex Voltage(Bus bus) => _V.TryGetValue(bus, out var value) ? value : Complex.Zero;

		/// <summary>
		/// The nominal voltage at the bus. In V.
		/// </summary>
		/// <param name="bus">The bus in question.</param>
		public Complex NominalVoltage(Bus bus) => _V0.TryGetValue(bus, out var value) ? value : Complex.Zero;

		/// <summary>
		/// Returns a clone of this flow
		/// </summary>
		/// <param name="configuration">The configuration to use in the new flow</param>
		public IPowerFlow Clone(NetworkConfiguration configuration) => new RadialFlow(this, configuration);

		#endregion

		/// <summary>
		/// Get the current through a line in the direction away from the root node.
		/// </summary>
		/// <param name="line"></param>
		/// <returns></returns>
		public Complex DownstreamCurrent(Line line)
		{
			if (IsOpen(line))
				return 0;

			if (UpstreamEnd(line) == line.Node1)
				return Current(line);
			else
				return -Current(line);
		}

		#endregion

		#region Internal methods

		/// <summary>
		/// Sets the voltage in the given bus
		/// </summary>
		internal void SetVoltage(Bus bus, Complex voltage) => _V[bus] = voltage;

		/// <summary>
		/// Sets the voltage in the given bus
		/// </summary>
		/// <param name="bus"></param>
		/// <param name="voltage"></param>
		internal void SetNominalVoltage(Bus bus, Complex voltage) => _V0[bus] = voltage;

		/// <summary>
		/// Sets the current in the given line, in the downstream direction
		/// </summary>
		internal void SetDownstreamCurrent(Line line, Complex current)
		{
			_I[line] = current;
		}

		/// <summary>
		/// Sets the power generated at the given generator bus
		/// </summary>
		internal void SetGeneratedPower(Bus generator, Complex power)
		{
			if (generator.Type == BusTypes.PowerProvider)
				_SGen[generator] = power;
			else
				throw new Exception("Can only set power for generators");
		}

		/// <summary>
		/// Sets the power injected into the given line, 
		/// in the downstream direction, from the upstream bus
		/// </summary>
		internal void SetInjectedPower(Line line, Complex power)
		{
			_S[line] = power;
		}

		#endregion

		#region Private methods

		/// <summary>
		/// Returns the upstream end of the given line
		/// </summary>
		private Bus UpstreamEnd(Line line)
		{
			if (_upstreamEnd != null)
				return _upstreamEnd.TryGetValue(line, out var value) ? value : null;

			return NetworkConfig.UpstreamEnd(line);
		}

		private bool IsOpen(Line line)
		{
			return line.IsSwitchable && NetworkConfig.SwitchSettings.IsOpen(line);
		}

		#endregion
	}
}
