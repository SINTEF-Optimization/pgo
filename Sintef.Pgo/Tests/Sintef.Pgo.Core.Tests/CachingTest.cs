using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Sintef.Pgo.Core.Test
{
	[TestClass]
	public class CachingTest
	{
		[TestMethod]
		public void PerPeriodValueCacheClientIsThreadSafe()
		{
			// Create a small problem over 10 periods
			NetworkBuilder builder = new NetworkBuilder();
			builder.Add("Gen -- line1[open] -o- line2[closed] -- Con");

			var periods = builder.RepeatedPeriodData(10);
			var solution = builder.Solution(periods, "problem");

			// Cache a value for each period
			PerPeriodValueCacheClient client = new PerPeriodValueCacheClient(period => Enumerable.Range(1, 1000).Sum(x => 1));

			for (int i = 0; i < 100; ++i)
			{
				// Change the solution in each period, so the cached data requires update
				for (int periodIndex = 0; periodIndex < 10; ++periodIndex)
					solution.SwapMove("line1", "line2", periodIndex).Apply(false);

				// Simultaneously request the cached data from multiple threads
				Parallel.ForEach(Enumerable.Range(1, 10), _ =>
				{
					double x = solution.Problem.Periods.Sum(p => client.Values(solution)[p]);
				});
			}
		}
	}
}
