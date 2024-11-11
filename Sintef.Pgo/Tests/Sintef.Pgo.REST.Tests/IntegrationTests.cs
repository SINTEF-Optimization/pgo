using System;
using System.IO;
using System.Threading;
using Sintef.Pgo.Core;
using Sintef.Pgo.Core.Test;
using Sintef.Pgo.Server;
using Xunit;

namespace Sintef.Pgo.REST.Tests
{
	public class IntegrationTests
	{
		Server.Server _server;

		public IntegrationTests()
		{
			_server = Server.Server.Create();
		}

		/// We have adjusted the weights and units of the objective since measuring the expected scores on the test cases below.
		/// Mainly, we made the power loss (W -> MWh) and line capacity (A -> Ah) weighted by the length of the periods.
		/// In these test cases the periods are 24 hours, so we multiply by 24 to compensate.
		private static readonly double objectiveFactor = 24.0;

		[Fact]
		public void TestBaranWu() => TestCase(TestUtils.TestDataFile("baran-wu.json"), TestUtils.TestDataFile("baran-wu_forecast.json"), 1000, 202678 * objectiveFactor);

		[Fact]
		public void TestBWModified() => TestCase(TestUtils.TestDataFile("baran-wu-modified.json"), TestUtils.TestDataFile("baran-wu-modified_forecast.json"), 1000, 73847 * objectiveFactor);

		[Fact]
		public void TestBWModifiedMultiperiod() => TestCase(TestUtils.TestDataFile("baran-wu-modified.json"), TestUtils.TestDataFile("baran-wu-modified_forecast-multiperiod.json"), 1000, 140448 * objectiveFactor);

		[Fact]
		public void TestBW10510() => TestCase(TestUtils.TestDataFile("bw10-5-10.json"), TestUtils.TestDataFile("bw10-5-10_forecast.json"), 15000, 4800000 * objectiveFactor);

		[Fact]
		public void TestBW20310() => TestCase(TestUtils.TestDataFile("bw20-3-10.json"), TestUtils.TestDataFile("bw20-3-10_forecast.json"), 10000, 1.0 * objectiveFactor);


		private void TestCase(string caseFile, string forecastFile, int timeout, double targetObjective )
		{
			Session session;

			using (var problemDefinition = File.OpenRead(caseFile))
			{
				_server.LoadNetworkFromJson("network", problemDefinition);
			}

			string forecastFilename = Path.Combine(Path.GetDirectoryName(caseFile), $"{Path.GetFileNameWithoutExtension(caseFile)}_forecast.json");
			using (var forecastStream = File.OpenRead(forecastFilename))
			{
				session = _server.CreateJsonSession("0", "network", forecastStream, null) as Session;
			}

			// Start optimization (as a background task).
			var _ = session.StartOptimization();

			DateTime startTime = DateTime.Now;

			while (DateTime.Now - startTime < TimeSpan.FromMilliseconds(timeout))
			{
				Thread.Sleep(100);
				var solution = session.GetBestSolutionClone();
				Console.WriteLine($"{solution?.ObjectiveValue} {solution?.IsFeasible}");
				if (solution != null && solution.IsFeasible && solution.ObjectiveValue < targetObjective)
					break;
			}

			session.StopOptimization().Wait();

			var bestSolution = session.GetBestSolutionClone();

			var solutionObjective = bestSolution?.ObjectiveValue ?? double.PositiveInfinity;
			Console.WriteLine($"{solutionObjective} {targetObjective}");

			Assert.True(solutionObjective < targetObjective);
		}
	}
}
