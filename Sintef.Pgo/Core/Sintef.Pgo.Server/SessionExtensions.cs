using Sintef.Pgo.DataContracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Sintef.Pgo.Server
{
	/// <summary>
	/// Extension methods for ISession
	/// </summary>
	public static class SessionExtensions
	{
		/// <summary>
		/// Extracts a session status from a session.
		/// </summary>
		/// <param name="session"></param>
		public static SessionStatus GetStatus(this ISession session)
		{
			SessionStatus status = new SessionStatus()
			{
				Id = session.Id,
				NetworkId = session.NetworkId,
				OptimizationIsRunning = session.OptimizationIsRunning
			};

			if (session.BestSolution != null)
			{
				if (session.BestSolutionIsFeasible)
					status.BestSolutionValue = session.BestSolutionValue;
				else
					status.BestInfeasibleSolutionValue = session.BestSolutionValue;
			}

			status.SolutionIds = session.SolutionIds.ToList();

			return status;
		}
	}
}
