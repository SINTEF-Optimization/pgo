using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Sintef.Scoop.Kernel;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// A base class for flow representations with variables associated to nodes.
	/// Represents all physical variables (P, Q, V, Delta).
	/// Sub classes can reflect the various approximation of the physics
	/// equations that were used in constructing the flow.
	/// </summary>
	public abstract class NodeBasedFlow : IPowerFlow
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
		/// The physical network that the flow is for
		/// </summary>
		protected PowerNetwork Network => NetworkConfig.Network;

		#endregion

		#region Private and protected data members

		/// <summary>
		/// For each bus, the up-stream bus.
		/// </summary>
		public Dictionary<Bus, Bus> UpstreamBuses { get; set; }

		#region Physics values per bus

		/// <summary>
		/// Power injection per node. TODO specify unit
		/// </summary>
		protected Dictionary<Bus, Complex> _S;

		/// <summary>
		/// Voltage level in each node. TODO specify unit/reference
		/// </summary>
		protected Dictionary<Bus, Complex> _U;

		#endregion

		#endregion

		#region Construction

		/// <summary>
		/// Constructor
		/// </summary>
		protected NodeBasedFlow(NetworkConfiguration networkConfig, PowerDemands demands)
		{
			NetworkConfig = networkConfig;
			Demands = demands;

			_S = new Dictionary<Bus, Complex>();
			_U = new Dictionary<Bus, Complex>();
			UpstreamBuses = new Dictionary<Bus, Bus>();
		}


		/// <summary>
		/// Copy constructor
		/// </summary>
		/// <param name="other"></param>
		/// <param name="configuration">The configuration to use in the new flow</param>
		public NodeBasedFlow(NodeBasedFlow other, NetworkConfiguration configuration)
		{
			NetworkConfig = configuration;
			Demands = other.Demands;

			_S = new Dictionary<Bus, Complex>(other._S);
			_U = new Dictionary<Bus, Complex>(other._U);
			UpstreamBuses = new Dictionary<Bus, Bus>(other.UpstreamBuses);
		}

		#endregion

		#region Public methods

		#region IPowerFlow implementation

		/// <summary>
		/// Returns the voltage at the given bus.
		/// </summary>
		public Complex Voltage(Bus bus)
		{
			if (_U.ContainsKey(bus))
				return _U[bus];
			else
				throw new System.Exception("NodeBasedFlow.Voltage called while solution not complete");
		}

		/// <summary>
		/// Computes the current flowing on the line.
		/// </summary>
		public abstract Complex Current(Line line);

		/// <summary>
		/// Returns the power injection of the given bus.
		/// 
		/// If the bus is a provider, this is computed by the solver,
		/// while if the bus is a consumer the power demand at the bus is returned.
		/// </summary>
		/// <param name="bus"></param>
		/// <returns></returns>
		public Complex PowerInjection(Bus bus)
		{
			if (bus.Type == BusTypes.PowerProvider)
			{
				if (_S.ContainsKey(bus))
					return _S[bus];
				else
					throw new System.Exception("NodeBasedFlow.PowerInjection called while solution not complete");
			}
			else
			{
				return -Demands.PowerDemand(bus);
			}
		}

		/// <summary>
		/// The power flow from the bus to the line.
		/// </summary>
		/// <param name="bus"></param>
		/// <param name="line"></param>
		/// <returns></returns>
		public abstract Complex PowerFlow(Bus bus, Line line);

		/// <summary>
		/// The power loss in the line.
		/// </summary>
		/// <param name="line"></param>
		/// <returns></returns>
		public abstract double PowerLoss(Line line);

		/// <summary>
		/// Returns a clone of this flow
		/// </summary>
		/// <param name="configuration">The configuration to use in the new flow</param>
		public abstract IPowerFlow Clone(NetworkConfiguration configuration);

		#endregion

		/// <summary>
		/// Sets the power injection at the given bus.
		/// </summary>
		public virtual void SetPowerInjection(Bus bus, Complex injection)
		{
			_S[bus] = injection;
		}

		/// <summary>
		/// Sets the voltage in the specified bus.
		/// </summary>
		public virtual void SetVoltage(Bus bus, Complex voltage)
		{
			_U[bus] = voltage;
		}

		#endregion
	}
}
