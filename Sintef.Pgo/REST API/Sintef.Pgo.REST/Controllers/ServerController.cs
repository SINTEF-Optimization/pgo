using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sintef.Pgo.DataContracts;
using Sintef.Pgo.Server;

namespace Sintef.Pgo.REST.Controllers
{
	/// <summary>
	/// Controller for <see cref="Server"/> related tasks.
	/// </summary>
	[Route("api/server")]
	[Route("api/cim/server")]
	[ApiController]
	public class ServerController : PgoControllerBase
	{
		/// <summary>
		/// Initializes a ServerController instance.
		/// </summary>
		/// <param name="serverCollection">The server collection to use</param>
		public ServerController(IMultiUserServer serverCollection)
			: base(serverCollection)
		{
		}

		/// <summary>
		/// Returns the server's status
		/// </summary>
		[HttpGet("")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public ActionResult<ServerStatus> GetStatus()
		{
			if (!FindServerForUser(out var server, out var errorResult))
				return errorResult;

			return new ServerStatus()
			{
				Networks = server.NetworkIds.ToList(),
				Sessions = server.SessionIds.Select(id => server.GetSession(id).GetStatus()).ToList(),
			};
		}
	}
}
