using Sintef.Pgo.DataContracts;
using Sintef.Scoop.Utilities;
using System.Collections.Generic;
using System.Security.Claims;

namespace Sintef.Pgo.REST
{
	/// <summary>
	/// Interface for the user quota provider service
	/// </summary>
	public interface IUserQoutaProvider
	{
		/// <summary>
		/// Returns the quotas for the user with the given ID
		/// </summary>
		UserQuotas QuotasFor(string userId);

		/// <summary>
		/// Sets the quotas for the user with the given ID.
		/// </summary>
		void SetQuotasFor(string userId, UserQuotas quotas);
	}

	/// <summary>
	/// The standard user quota provider service implementation
	/// </summary>
	public class UserQoutaProvider : IUserQoutaProvider
	{	
		private Dictionary<string, UserQuotas> _quotas = new();

		private UserQuotas _defaultQuotas;

		/// <summary>
		/// Initializes a provider with the given default quotas
		/// </summary>
		public UserQoutaProvider(UserQuotas defaultQuotas)
		{
			_defaultQuotas = defaultQuotas;
		}

		/// <inheritdoc/>
		public UserQuotas QuotasFor(string userId)
		{
			return _quotas.ItemOrAdd(userId, () => _defaultQuotas);
		}

		/// <inheritdoc/>
		public void SetQuotasFor(string userId, UserQuotas quotas)
		{
			_quotas[userId] = quotas;
		}
	}
}
