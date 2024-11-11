using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sintef.Pgo.Server;
using Sintef.Pgo.REST.Extensions;

namespace Sintef.Pgo.REST.Controllers
{
	/// <summary>
	/// Common base class for PGO controllers
	/// </summary>
	public class PgoControllerBase : ControllerBase
	{
		/// <summary>
		/// The user ID of the current user, or null
		/// if there is no authenticated user
		/// </summary>
		/// <returns></returns>
		protected string UserId => User.GetUserId();

		/// <summary>
		/// The server we use
		/// </summary>
		private readonly IMultiUserServer _server;

		/// <summary>
		/// Initializes the controller
		/// </summary>
		/// <param name="server">The server to use</param>
		public PgoControllerBase(IMultiUserServer server)
		{
			_server = server;
		}

		/// <summary>
		/// Retrieves the server interface that represents the resources available
		/// to the current user and requested session type, in <paramref name="server"/>, and returns true if 
		/// successful.
		/// If unsuccessful, returns false and sets <paramref name="errorResult"/>
		/// to the result that the controller should return.
		/// </summary>
		protected bool FindServerForUser(out IServer server, out ActionResult errorResult)
		{
			if (UserId is not string userId)
			{
				server = null;
				errorResult = Unauthorized();
				return false;
			}

			var sessionType = Server.Server.SessionType.Json;
			if (Request.Path.StartsWithSegments("/api/cim"))
				sessionType = Server.Server.SessionType.Cim;

			server = _server.ServerFor(userId, sessionType);
			errorResult = null;
			return true;
		}

		/// <summary>
		/// Reads all the contents of the given form file into a memory stream
		/// </summary>
		protected static MemoryStream ReadToMemoryStream(IFormFile formFile)
		{
			MemoryStream memoryStream = new MemoryStream();

			using (var formFileStream = formFile.OpenReadStream())
			{
				formFileStream.CopyTo(memoryStream);
			}
			memoryStream.Seek(0, SeekOrigin.Begin);

			return memoryStream;
		}
	}
}
