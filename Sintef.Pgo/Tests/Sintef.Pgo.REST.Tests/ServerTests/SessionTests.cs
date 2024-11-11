using System;
using System.Threading;
using Sintef.Pgo.Core;
using Sintef.Pgo.Core.Test;
using Xunit;

namespace Sintef.Pgo.Server.Tests
{
	/// <summary>
	/// Tests for the Session class
	/// </summary>
	public class SessionTests
	{
		Session _session;

		public SessionTests()
		{
			var periodData = TestUtils.CreateModifiedBaranWuCase();
			var problem = new PgoProblem(periodData, new SimplifiedDistFlowProvider());
			var networkManager = new NetworkManager(problem.Network);

			_session = new Session("id", problem, networkManager);
		}

		[Fact]
		public void SummarizingWhileOptimizingSucceeds()
		{
			_ = _session.StartOptimization();

			DateTime start = DateTime.Now;

			for (int i = 0; i < 1000; ++i)
			{
				IPgoSolution sol = _session.GetBestSolutionClone();
				if (sol != null)
					_session.Summarize(sol);
				Thread.Sleep(1);

				if ((DateTime.Now - start).TotalSeconds > 2)
					break;
			}

			_session.StopOptimization().Wait();
		}

		[Fact]
		public void StartingOptimizationAndStoppingImmediatelySucceeds()
		{
			_ = _session.StartOptimization();

			_session.StopOptimization().Wait();
		}
	}
}
