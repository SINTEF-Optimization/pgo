using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sintef.Pgo.DataContracts
{
	/// <summary>
	/// Information about the quotas available to a user
	/// </summary>
	public record UserQuotas
	{
		/// <summary>
		/// The maximum number of networks the user may have loaded
		/// </summary>
		public int NetworkLimit { get; init; }

		/// <summary>
		/// The maximum number of sessions the user may have open
		/// </summary>
		public int SessionLimit { get; init; }

		/// <summary>
		/// Optimization is stopped automatically after running for this 
		/// length of time (unless stopped earlier by the user)
		/// </summary>
		public TimeSpan OptimizationTimeout { get; init; }

		/// <summary>
		/// Sessions are deleted automatically when they are not optimizing and 
		/// this interval has elapsed since 
		///  - session creation, or
		///  - the optimization was stopped, or
		///  - a solution was added or removed,
		/// whichever is latest.
		/// </summary>
		public TimeSpan SessionTimeout { get; init; }

		/// <summary>
		/// The maximum number of nodes allowed in a single network
		/// </summary>
		public int NetworkMaxSize { get; init; }

		/// <summary>
		/// Creates very restrictive quotas
		/// </summary>
		public static UserQuotas Restrictive() => new UserQuotas
		{
			NetworkLimit = 1,
			SessionLimit = 1,
			OptimizationTimeout = TimeSpan.FromMinutes(1),
			SessionTimeout = TimeSpan.FromMinutes(30),
			NetworkMaxSize = 10
		};

		/// <summary>
		/// Creates the quotas used in the demo web app
		/// </summary>
		public static UserQuotas ForDemo() => new UserQuotas
		{
			NetworkLimit = 2,
			SessionLimit = 2,
			OptimizationTimeout = TimeSpan.FromHours(12),
			SessionTimeout = TimeSpan.FromHours(24),
			NetworkMaxSize = 100
		};

		/// <summary>
		/// Creates unlimited quotas
		/// </summary>
		public static UserQuotas NoLimits() => new UserQuotas
		{
			NetworkLimit = int.MaxValue,
			SessionLimit = int.MaxValue,
			OptimizationTimeout = TimeSpan.FromDays(365),
			SessionTimeout = TimeSpan.FromDays(365),
			NetworkMaxSize = int.MaxValue,
		};
	}
}

	namespace System.Runtime.CompilerServices
	{
		internal static class IsExternalInit { }
	}
