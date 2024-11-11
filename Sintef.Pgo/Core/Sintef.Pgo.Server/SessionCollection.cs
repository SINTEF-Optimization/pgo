using System;
using System.Collections.Generic;
using System.Linq;

using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Server
{
	/// <summary>
	/// A structure for managing a set of sessions.
	/// </summary>
	public class SessionCollection
	{
		private readonly Dictionary<string, ISession> _objects = new Dictionary<string, ISession>();

		/// <summary>
		/// Returns all sessions in the collection.
		/// </summary>
		public IEnumerable<ISession> Items => _objects.Values;

		/// <summary>
		/// Adds a session to the collection.
		/// </summary>
		/// <param name="session"></param>
		public void Add(ISession session)
		{
			_objects.Add(session.Id, session);
		}

		/// <summary>
		/// Returns true, if a session with id <paramref name="id"/> exists, false otherwise.
		/// </summary>
		/// <param name="id">The identifier of the session to check for.</param>
		/// <returns></returns>
		public bool Exists(string id)
		{
			return _objects.Keys.Any(key => key == id);
		}

		/// <summary>
		/// Returns the session if it exists in the collection, null otherwise.
		/// </summary>
		/// <param name="id">The identifier of the session to retrieve.</param>
		/// <returns></returns>
		public ISession ItemOrDefault(string id)
		{
			return _objects.ItemOrDefault(id);
		}

		/// <summary>
		///  Deletes the session with the given id, if it exists. If not, the function does nothing.
		/// </summary>
		/// <param name="id"></param>
		public bool Delete(string id)
		{
			return _objects.Remove(id);
		}
	}
}