using Sintef.Scoop.Kernel;
using System;

namespace Sintef.Pgo.Core
{

	/// <summary>
	/// An encoding for the flow sub problem. I.e., given a network
	/// and switch settings, generate optimal flow.
	/// </summary>
	public class FlowProblem : Encoding
	{
		/// <summary>
		/// The network configuration.
		/// </summary>
		public NetworkConfiguration NetworkConfig { get; }

		/// <summary>
		/// The power demands for each consumer bus in <see cref="NetworkConfiguration.Network"/>.
		/// </summary>
		public PowerDemands Demands { get; private set; }

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="config">The network configuration that modify the network.</param>
		/// <param name="demands">The power demands for each consumer bus in <see cref="NetworkConfiguration.Network"/>. </param>
		public FlowProblem(NetworkConfiguration config, PowerDemands demands)
		{
			NetworkConfig = config;
			Demands = demands;
		}
	}
}
