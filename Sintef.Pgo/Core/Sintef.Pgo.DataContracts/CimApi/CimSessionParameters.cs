using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace Sintef.Pgo.DataContracts
{
	/// <summary>
	/// Parameters for creating a session from CIM objects
	/// </summary>
	public class CimSessionParameters
	{
		/// <summary>
		/// The ID of the power network to use in the session.
		/// 
		/// The network must have been created from CIM data.
		/// </summary>
		public string NetworkId { get; set; }

		/// <summary>
		/// The periods, with consumer demands
		/// </summary>
		public List<CimPeriodAndDemands> PeriodsAndDemands { get; set; }

		/// <summary>
		/// The network configuration at the beginning of the planning period.
		/// 
		/// Optional. If not given, no stability objective will be used to minimize changes wrt. the start configuration.
		/// </summary>
		public CimConfiguration StartConfiguration { get; set; }
	}

	/// <summary>
	/// Parameters for creating a session from CIM data on JSON-LD format.
	/// </summary>
	public class CimJsonLdSessionParameters
	{
		/// <summary>
		/// The ID of the power network to use in the session
		/// </summary>
		[JsonRequired]
		public string NetworkId { get; set; }

		/// <summary>
		/// The periods, with consumer demands
		/// </summary>
		[JsonRequired]
		public List<CimJsonLdPeriodAndDemands> PeriodsAndDemands { get; set; }

		/// <summary>
		/// The network configuration at the beginning of the planning period.
		/// 
		/// This is a JSON-LD graph object containing a collection of CIM objects. 
		/// For information on which CIM objects to include and how they are used in the PGO internal model,
		/// see the documentation of <see cref="CimConfiguration"/>.
		/// 
		/// Optional. If not given, no stability objective will be used to minimize changes wrt. the start configuration.
		/// </summary>
		public JObject StartConfiguration { get; set; }
	}
}