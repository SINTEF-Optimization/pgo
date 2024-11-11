using Sintef.Pgo.Core;

namespace Sintef.Pgo.Api.Tests
{
	/// <summary>
	/// Tests for optimizing in a session in the .NET API
	/// </summary>
	[TestClass]
	public class OptimizationTests : ApiTestFixture
	{
		ISession _session = null!;

		[TestInitialize]
		public new void Setup()
		{
			NetworkBuilder b = new();
			b.Add("G[generatorVoltage=100] -- L1[closed] -- C[consumption=(1,0)]");
			b.Add("G -- L2[closed] -- C");

			var (powerGrid, demand, singlePeriodSettings) = TestDataFrom(b);

			_server.AddNetwork(_jsonNetworkId, powerGrid);

			_session = AddJsonSession("id", _jsonNetworkId, demand, singlePeriodSettings);
		}

		[TestMethod]
		public void OptimizationCanBeStartedAndStopped()
		{
			Assert.IsFalse(_session.Status.OptimizationIsRunning);

			_session.StartOptimization();

			Assert.IsTrue(_session.Status.OptimizationIsRunning);

			_session.StopOptimization();

			Assert.IsFalse(_session.Status.OptimizationIsRunning);
		}

		[TestMethod]
		public void StartingOrStoppingOptimizationTwiceIsOk()
		{
			_session.StartOptimization();
			_session.StartOptimization();
			Assert.IsTrue(_session.Status.OptimizationIsRunning);

			_session.StopOptimization();
			_session.StopOptimization();
			Assert.IsFalse(_session.Status.OptimizationIsRunning);
		}

		[TestMethod]
		public void OptimizationProducesABetterSolution()
		{
			Assert.IsNull(_session.Status.BestSolutionValue);
			Assert.IsFalse(_session.BestSolutionInfo.IsFeasible);

			Optimize(_session);

			Assert.IsNull(_session.Status.BestInfeasibleSolutionValue);
			Assert.IsNotNull(_session.Status.BestSolutionValue);
			Assert.IsTrue(_session.BestSolutionInfo.IsFeasible);
		}

		[TestMethod]
		public void OptimizationReportsNewBestObjectiveValues()
		{
			string log = "";
			_session.BestSolutionFound += (s, e) => log += $"New best in session {((ISession)s!).Id}";

			Optimize(_session);

			Assert.AreEqual("New best in session id", log);
		}

		[TestMethod]
		public void RemovingASessionStopsTheOptimizer()
		{
			// Hook up tasks that will complete when optimization starts/stops
			var session = (Server.Session)((Impl.Session)_session).InternalSession;

			var started = new TaskCompletionSource<object>();
			var stopped = new TaskCompletionSource<object>();
			session.OptimizationStarted += (s, e) => { started.SetResult(new()); };
			session.OptimizationStopped += (s, e) => { stopped.SetResult(new()); };

			_session.StartOptimization();

			// Verify that optimization started
			Assert.IsTrue(started.Task.Wait(TimeSpan.FromSeconds(5)), "Optimization did not start");

			Assert.IsTrue(_session.Status.OptimizationIsRunning);

			_server.RemoveSession(_session);

			// Verify that optimization stopped
			Assert.IsTrue(stopped.Task.Wait(TimeSpan.FromSeconds(5)), "Optimization did not stop");
		}

		[TestMethod]
		public void OptimizingAnEmptyNetworkWorks()
		{
			_server.AddNetwork("empty", new PowerGrid() { Nodes = new(), Lines = new(), SchemaVersion = 1 });
			var session = _server.AddSession("empty", new SessionParameters()
			{
				NetworkId = "empty",
				Demand = new()
				{
					SchemaVersion = 1,
					Periods = new() {
						new DataContracts.Period() {
							Id="1", 
							StartTime=DateTime.Now, 
							EndTime = DateTime.Now.AddDays(1)
						}
					},
					Loads = new()
				},
			});

			session.StartOptimization();
			Thread.Sleep(200);
			session.StopOptimization();
		}
	}
}