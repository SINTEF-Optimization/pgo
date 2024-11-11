using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Manages PowerNetworks at different level of aggregation. Offers functionality for aggregation and
	/// dis-aggregation of networks.
	/// </summary>
	public class NetworkManager
	{
		#region Public properties 

		/// <summary>
		/// The original network
		/// </summary>
		public PowerNetwork OriginalNetwork => AcyclicAggregator.OriginalNetwork;

		/// <summary>
		/// The acyclic network. This is an aggregation of the original network where
		/// all simple sequential and parallel lines have been aggregated.
		/// This network is supposed to be physically equivalent to the original network, and not be an approximation.
		/// </summary>
		public PowerNetwork AcyclicNetwork => AcyclicAggregator.AggregateNetwork;

		/// <summary>
		/// The aggregator that eliminates simple parallel and sequential lines
		/// </summary>
		public NetworkAggregation AcyclicAggregator { get; }

		#endregion

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="original">The original network. This must be completely constructed 
		/// i.e. it must not be modified after given to this constructor.
		/// </param>
		public NetworkManager(PowerNetwork original)
		{
			AcyclicAggregator = NetworkAggregation.MakeAcyclicAndConnected(original);
		}
	}
}
