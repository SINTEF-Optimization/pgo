namespace Sintef.Pgo.REST.Models
{
	internal static class PgoClaimTypes
	{
		public const string UserId = "http://sintef.no/pgo-user-id";

		public const string NetworkLimit = "http://sintef.no/pgo-quotas/network-limit";
		public const string SessionLimit = "http://sintef.no/pgo-quotas/session-limit";
		public const string OptimizationTimeout = "http://sintef.no/pgo-quotas/optimization-timeout";
		public const string SessionTimeout = "http://sintef.no/pgo-quotas/session-timeout";
		public const string NetworkMaxSize = "http://sintef.no/pgo-quotas/network-max-size";
	}
}
