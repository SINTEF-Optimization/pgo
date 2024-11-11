using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Sintef.Pgo.REST.Extensions
{
	/// <summary>
	/// Extension methods for claims
	/// </summary>
	public static class ClaimMappingExtensions
	{
		/// <summary>
		/// Returns the value of the given claim type from the given collection of claims
		/// </summary>
		public static string GetValueOf(this ICollection<Claim> claimsCollection, string claimType)
		{
			return claimsCollection.FirstOrDefault(c => c.Type == claimType)?.Value;
		}
	}
}
