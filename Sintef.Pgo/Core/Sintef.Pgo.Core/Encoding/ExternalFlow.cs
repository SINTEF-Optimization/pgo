using Sintef.Scoop.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// A flow that has been explicitly specified from some external source, 
	/// as voltage, current and power at each bus and line.
	/// </summary>
	public class ExternalFlow : IPowerFlow
	{
		/// <summary>
		/// Externally specified flow.
		/// </summary>
		public ExternalFlow() { }

		/// <summary>
		/// The network configuration this flow is for.
		/// </summary>
		public NetworkConfiguration NetworkConfig { get; set; }

		/// <summary>
		/// The demands used to calculate this flow.
		/// </summary>
		public PowerDemands Demands { get; set; }

		/// <summary>
		/// The status of the flow computation.
		/// </summary>
		public FlowStatus Status { get; set; }

		/// <summary>
		/// Optional status message to explain the status of the flow computation.
		/// </summary>
		public string StatusDetails { get; set; }

		/// <summary>
		/// Clone the external flow and use another network configuration.
		/// </summary>
		/// <param name="configuration"></param>
		/// <returns></returns>
		public IPowerFlow Clone(NetworkConfiguration configuration)
		{
			return new ExternalFlow
			{
				NetworkConfig = configuration,
				Demands = Demands,
				Status = Status,
				StatusDetails = StatusDetails,
				Currents = Currents,
				PowerFlows = PowerFlows,
				Voltages = Voltages,
			};
		}

		/// <summary>
		/// Table of lines to currents.
		/// </summary>
		public Dictionary<Line, Complex> Currents { get; set; } = 
		new Dictionary<Line, Complex>();

		/// <summary>
		/// Return the current from the Currents table
		/// </summary>
		/// <param name="line"></param>
		/// <returns></returns>
		public Complex Current(Line line) => Currents[line];

		/// <summary>
		/// Table of bus/line to power flow.
		/// </summary>
		public Dictionary<(Bus, Line), Complex> PowerFlows { get; set; } = new Dictionary<(Bus, Line), Complex>();

		/// <summary>
		/// Returns the power flow from the PowerFlows table.
		/// </summary>
		/// <param name="bus"></param>
		/// <param name="line"></param>
		/// <returns></returns>
		public Complex PowerFlow(Bus bus, Line line) => PowerFlows[(bus, line)];


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
					return NetworkConfig.PresentLinesAt(bus).Select(l => PowerFlow(bus, l)).ComplexSum();
				case BusTypes.PowerConsumer:
					return -Demands.PowerDemand(bus);
				default:
					throw new NotImplementedException("ExternalFlow.PowerInjection: Bus type not supported.");
			}
		}

		/// <summary>
		/// Table of bus voltages.
		/// </summary>
		public Dictionary<Bus, Complex> Voltages { get; set; } = new Dictionary<Bus, Complex>();

		/// <summary>
		/// Returns the voltage from the Voltages table.
		/// </summary>
		/// <param name="bus"></param>
		/// <returns></returns>
		public Complex Voltage(Bus bus) => Voltages[bus];
	}
}
