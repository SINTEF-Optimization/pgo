using Sintef.Pgo.DataContracts;
using Sintef.Pgo.REST.Models;
using System;
using System.Security.Claims;
using System.Xml;

namespace Sintef.Pgo.REST.Extensions
{
	/// <summary>
	/// Extension methods for <see cref="ClaimsPrincipal"/>
	/// </summary>
	public static class ClaimsPrincipalExtensions
	{
		/// <summary>
		/// Returns the user ID of the given user
		/// </summary>
		internal static string GetUserId(this ClaimsPrincipal user)
		{
			return user.FindFirst(PgoClaimTypes.UserId)?.Value;
		}

		/// <summary>
		/// Returns the quotas extracted from the clainms of the given user
		/// </summary>
		internal static UserQuotas GetQuotas(this ClaimsPrincipal user)
		{
			var quotas = UserQuotas.Restrictive();

			ParseIntClaim(PgoClaimTypes.NetworkLimit, v => quotas = quotas with { NetworkLimit = v });
			ParseIntClaim(PgoClaimTypes.SessionLimit, v => quotas = quotas with { SessionLimit = v });
			ParseTimeClaim(PgoClaimTypes.OptimizationTimeout, v => quotas = quotas with { OptimizationTimeout = v });
			ParseTimeClaim(PgoClaimTypes.SessionTimeout, v => quotas = quotas with { SessionTimeout = v });
			ParseIntClaim(PgoClaimTypes.NetworkMaxSize, v => quotas = quotas with { NetworkMaxSize = v });

			return quotas;


			//----------

			void ParseIntClaim(string claimName, Action<int> setAction)
			{
				if (user.FindFirst(claimName) is Claim claim && int.TryParse(claim.Value, out int limit))
					setAction(limit);
			}

			void ParseTimeClaim(string claimName, Action<TimeSpan> setAction)
			{
				if (user.FindFirst(claimName) is Claim claim)
				{
					try
					{
						setAction(XmlConvert.ToTimeSpan(claim.Value));
					}
					catch (Exception) { }
				}
			}
		}
	}
}
