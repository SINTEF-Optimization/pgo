using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Server
{
	/// <summary>
	/// Interface for a PGO server that contains resources for multiple
	/// users
	/// </summary>
	public interface IMultiUserServer
	{
		/// <summary>
		/// Returns an interface that represents the resources 
		/// available to a single user for one session type.
		/// </summary>
		/// <param name="userId">The user's ID</param>
		IServer ServerFor(string userId, Server.SessionType sessionType);

		/// <summary>
		/// Clears all resources for all users
		/// </summary>
		void Clear();

		/// <summary>
		/// Deletes all sessions whose timeout has expired
		/// </summary>
		void CleanUpSessions();
	}

	/// <summary>
	/// A PGO server that contains resources for multiple
	/// users
	/// </summary>
	public class MultiUserServer : IMultiUserServer
	{
		/// <summary>
		/// The single-user servers, by user ID and session type
		/// </summary>
		Dictionary<(string, Server.SessionType), IServer> _servers;

		public MultiUserServer()
		{
			_servers = new();
		}

		/// <summary>
		/// Clears all single-user servers
		/// </summary>
		public void Clear()
		{
			lock (_servers)
				_servers.Clear();
		}

		/// <summary>
		/// Deletes all sessions whose timeout has expired
		/// </summary>
		public void CleanUpSessions()
		{
			lock (_servers)
			{
				foreach (var (id, server) in _servers)
					server.CleanUpSessions();
			}
		}

		/// <summary>
		/// Returns the server for the given user ID and session type.
		/// Creates a new server if none exists.
		/// </summary>
		public IServer ServerFor(string userId, Server.SessionType sessionType)
		{
			lock (_servers)
				return _servers.ItemOrAdd((userId,sessionType), () => Server.Create());
		}
	}
}
