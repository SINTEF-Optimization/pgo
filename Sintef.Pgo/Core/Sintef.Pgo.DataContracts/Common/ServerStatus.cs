using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sintef.Pgo.DataContracts
{
	/// <summary>
	/// Status information for a server
	/// </summary>
	public class ServerStatus
	{
		/// <summary>
		/// The IDs of the loaded networks.
		/// </summary>
		public List<string> Networks { get; set; }

		/// <summary>
		/// The status of each session in the server.
		/// </summary>
		public List<SessionStatus> Sessions { get; set; }

		/// <summary>
		/// Initializes a server status
		/// </summary>
		public ServerStatus()
		{ }
	}
}
