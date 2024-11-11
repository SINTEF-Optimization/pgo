using Sintef.Pgo.DataContracts;
using System.Collections.Generic;
using System.Security.Claims;
using System.Xml;

namespace Sintef.Pgo.REST.Models
{
	/// <summary>
	/// Helper for building claims
	/// </summary>
	public class PgoClaimsBuilder
	{
		private string UserId { get; set; }
		private string NetworkLimit { get; set; }
		private string SessionLimit { get; set; }
		private string OptimizationTimeout { get; set; }
		private string SessionTimeout { get; set; }
		private string NetworkMaxSize { get; set; }

		/// <summary>
		/// Initializes a builder for the given user ID
		/// </summary>
		public PgoClaimsBuilder(string userId)
		{
			UserId = userId;
		}

		/// <summary>
		/// Configures the builder to create claims for the given quotas
		/// </summary>
		public PgoClaimsBuilder WithUserQuotas(UserQuotas userQuotas)
		{
			NetworkLimit = userQuotas.NetworkLimit.ToString();
			SessionLimit = userQuotas.SessionLimit.ToString();
			OptimizationTimeout = XmlConvert.ToString(userQuotas.OptimizationTimeout);
			SessionTimeout = XmlConvert.ToString(userQuotas.SessionTimeout);
			NetworkMaxSize = userQuotas.NetworkMaxSize.ToString();

			return this;
		}

		/// <summary>
		/// Builds the claims
		/// </summary>
		public List<Claim> Build()
		{
			var claims = new List<Claim>
			{
				new Claim(PgoClaimTypes.UserId, UserId),
				new Claim(PgoClaimTypes.NetworkLimit, NetworkLimit),
				new Claim(PgoClaimTypes.SessionLimit, SessionLimit),
				new Claim(PgoClaimTypes.OptimizationTimeout, OptimizationTimeout),
				new Claim(PgoClaimTypes.SessionTimeout, SessionTimeout),
				new Claim(PgoClaimTypes.NetworkMaxSize, NetworkMaxSize),
			};

			return claims;
		}
	}
}
