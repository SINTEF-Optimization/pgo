namespace Sintef.Pgo.DataContracts
{
	/// <summary>
	/// Parameters for creating a session from data in PGO's JSON format
	/// </summary>
	public class SessionParameters
	{
		/// <summary>
		/// The ID of the power network to use in the session.
		/// 
		/// The network must have been created from data in PGO's JSON format.
		/// </summary>
		public string NetworkId { get; set; }

		/// <summary>
		/// The periods and consumer demands per period
		/// </summary>
		public Demand Demand { get; set; }

		/// <summary>
		/// The network configuration before the first period.
		/// 
		/// Optional. If not given, no stability objective will be used to minimize changes wrt. the start configuration.
		/// </summary>
		public SinglePeriodSettings StartConfiguration { get; set; }

		/// <summary>
		/// If true, <see cref="Demand"/> does not need to contain load data for all consumer nodes, and zero load is assumed
		/// for consumer nodes that are omitted.
		/// If false, load data must be given for all consumers.
		/// </summary>
		public bool AllowUnspecifiedConsumerDemands { get; set; } = false;
	}
}