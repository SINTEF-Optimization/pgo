using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Interface for accessing the properties of a power flow, such as bus voltages, line currents
	/// and power losses.
	/// 
	/// In principle, a power flow can be defined by the bus voltages and line currents alone, and
	/// the remaining quantities can be derived. However, this interface also exposes methods for accessing
	/// derived quantities, such as power loss. 
	/// This enables an implementing class whose flow is not exact, to present derived quantities
	/// that are not consistent with the voltages and currents, but give a better
	/// result in the context that the approximation is used.
	/// </summary>
	public interface IPowerFlow
	{
		/// <summary>
		/// The network configuration this flow is for
		/// </summary>
		NetworkConfiguration NetworkConfig { get; }

		/// <summary>
		/// The demands this flow is for
		/// </summary>
		PowerDemands Demands { get; }

		/// <summary>
		/// The status of the flow
		/// </summary>
		FlowStatus Status { get; }

		/// <summary>
		/// A string describing the flow's status in more detail
		/// </summary>
		string StatusDetails { get; }

		/// <summary>
		/// Returns the voltage at the given bus. In V.
		/// </summary>
		Complex Voltage(Bus bus);

		/// <summary>
		/// Returns the current flowing in the given line, from Node1 to Node2. In A.
		/// </summary>
		/// <param name="line">The line whose current to return</param>
		/// <returns>The signed current on the line.</returns>
		Complex Current(Line line);

		/// <summary>
		/// Returns the power injection at the bus. Positive injection is power produced
		/// at the bus and injected into the rest of the network.
		/// If the injection has a negative real part, the bus consumes power.
		/// In VA.
		/// </summary>
		Complex PowerInjection(Bus bus);

		/// <summary>
		/// Returns the power that flows from the given <paramref name="bus"/>
		/// into the given <paramref name="line"/>.
		/// If the bus actually receives power from the line, the result
		/// ('s real part) will be negative. The unit is VA.
		/// </summary>
		/// <param name="bus">The bus from which the power flows into the line</param>
		/// <param name="line">The line into which the power flows</param>
		Complex PowerFlow(Bus bus, Line line);


		/// <summary>
		/// Returns a clone of this flow
		/// </summary>
		/// <param name="configuration">The configuration to use in the new flow</param>
		IPowerFlow Clone(NetworkConfiguration configuration);
	}

	/// <summary>
	/// Power flow extension methods.
	/// </summary>
	public static class PowerFlowExtensionMethods
	{
		/// <summary>
		/// Returns the (real) power loss on the line, in absolute terms. In W. Always non-negative.
		/// 
		/// This is the real part of the power loss, because reactive power "loss" is not typically 
		/// an interesting quantity.
		/// </summary>
		/// <param name="flow"></param>
		/// <param name="line">The line to get the power loss for</param>
		public static double PowerLoss(this IPowerFlow flow, Line line)
		{
			if (flow.NetworkConfig.TransformerModeForOutputLine(line) is Transformer.Mode mode)
			{
				var loss = flow.PowerFlow(line.Transformer.Bus, line) * (1 - mode.PowerFactor);
				return loss.Real;
			}
			else
			{
				var current = flow.Current(line);
				return line.Resistance * (current * Complex.Conjugate(current)).Real;
			}
		}
	}


	/// <summary>
	/// Status for the result of a flow calculation.
	/// Note that the order of the values is meaningful: Smaller values represent
	/// a worse situation than larger values.
	/// </summary>
	public enum FlowStatus
	{
		/// <summary>
		/// No flow could be calculated. Normally this is because the configuration is not radial.
		/// </summary>
		None = 0,

		/// <summary>
		/// A flow calculation was attempted, but failed. The flow that was produced may contain very
		/// inconsistent values. This code is used e.g. if IteratedDistFlow diverges.
		/// </summary>
		Failed = 1,

		/// <summary>
		/// The flow is approximate.
		/// </summary>
		Approximate = 2,

		/// <summary>
		/// The flow is exact (up to numerical tolerances)
		/// </summary>
		Exact = 3,
	}

	/// <summary>
	/// Extension methods for IPowerFlow
	/// </summary>
	public static class IFlowExtensions
	{
		/// <summary>
		/// Returns the magnitude of the current in the given <paramref name="line"/>, in A.
		/// This value is independent of line direction.
		/// </summary>
		public static double CurrentMagnitude(this IPowerFlow flow, Line line)
		{
			return flow.Current(line).Magnitude;
		}

		/// <summary>
		/// Computes the power ejection from the "end" end of the line, as consistent with the flow
		/// approximation. In VA.
		/// </summary>
		/// <param name="flow"></param>
		/// <param name="line">The line</param>
		/// <param name="end">The end of the line to consider</param>
		/// <returns>The power ejected from the line at the end</returns>
		public static Complex GetPowerEjection(this IPowerFlow flow, Line line, Bus end)
		{
			return -flow.PowerFlow(end, line);
		}

		/// <summary>
		/// Writes the voltages, currents and powers of the flow to the console
		/// </summary>
		internal static void Write(this IPowerFlow flow)
		{
			flow.Write(flow.NetworkConfig.Network, flow.Demands);
		}

		/// <summary>
		/// Writes the voltages, currents and powers of the flow to the console
		/// </summary>
		/// <param name="flow"></param>
		/// <param name="network">The network the flow is for</param>
		/// <param name="demands">The demands the flow is for. If null, consumer demands
		///   are not written.</param>
		public static void Write(this IPowerFlow flow, PowerNetwork network, PowerDemands demands = null)
		{
			var configuration = flow.NetworkConfig;

			IOrderedEnumerable<Line> lines = network.Lines
				.OrderBy(l => configuration.ProviderForBus(l.Node1)?.Index ?? int.MaxValue)
				.ThenBy(l => configuration.DistanceToProvider(l));

			IOrderedEnumerable<Bus> buses = network.Buses
				.OrderBy(b => configuration.ProviderForBus(b)?.Index ?? int.MaxValue)
				.ThenBy(b => configuration.DistanceToProvider(b));


			TableFormatter table = new TableFormatter("Bus", "Voltage", "Produced/consumed");
			foreach (var bus in buses)
			{
				string power = "";
				if (bus.IsProvider)
					power = $"{flow.PowerInjection(bus)}VA";
				if (bus.IsConsumer && demands != null)
				{
					power = $"{demands.PowerDemand(bus)}VA";
				}

				string voltage = "---";
				if (!bus.IsTransformer)
					voltage = $"{flow.Voltage(bus)}V";

				table.AddLine(bus.Name, voltage, power);
			}
			table.Show();
			Console.WriteLine();


			table = new TableFormatter("Line", "Current", "Real power loss");
			foreach (var line in lines)
			{
				table.AddLine(line.Name, $"{flow.Current(line)}A", $"{flow.PowerLoss(line)}W");
			}
			table.Show();
			Console.WriteLine();


			Console.WriteLine("Power flows");
			table = new TableFormatter() { MarginWidth = 1 };
			foreach (var line in lines)
			{
				Bus injector;
				Bus ejector;
				if (flow.PowerFlow(line.Node1, line).Real > 0)
				{
					injector = line.Node1;
					ejector = line.Node2;
				}
				else
				{
					injector = line.Node2;
					ejector = line.Node1;
				}

				table.AddLine(injector.Name, "->", line.Name, ": ", $"{flow.PowerFlow(injector, line)}VA");
				table.AddLine(line.Name, "->", ejector.Name, ": ", $"{-flow.PowerFlow(ejector, line)}VA");
			}
			table.Show();
			Console.WriteLine();
		}
	}
}
