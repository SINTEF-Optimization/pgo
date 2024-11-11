using Xunit;
using Xunit.Abstractions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Linq;
using System.Collections.Generic;
using Sintef.Pgo.DataContracts;
using Sintef.Pgo.Core;

namespace Sintef.Pgo.REST.Tests
{
	public class ObjectiveWeightTests : LiveServerFixture
	{
		public ObjectiveWeightTests(ITestOutputHelper output) : base(output) { }

		[Fact]
		public void GetObjectiveReturnsObjectives()
		{
			SetupStandardSession();
			var weights = Client.GetObjectiveWeights(SessionId);
			Assert.True(weights.Count > 0);
		}

		[Fact]
		public void SetObjectiveModifiesObjectives()
		{
			SetupStandardSession();
			var weights = Client.GetObjectiveWeights(SessionId);
			weights[0].Weight += 1;
			Client.SetObjectiveWeights(SessionId, weights);

			var weightsset = weights.ToDictionary(x => x.ObjectiveName, x => x.Weight);
			var weightsreceived = Client.GetObjectiveWeights(SessionId).ToDictionary(x => x.ObjectiveName, x => x.Weight);
			
			AssertEquivalent(weightsset, weightsreceived);
		}

		[Fact]
		public void CannotSetWeightsForUnknownSession()
		{
			SetupStandardSession();
			var weights = Client.GetObjectiveWeights(SessionId);
			AssertException(() => Client.SetObjectiveWeights("???", weights), "Not found");
		}

		[Fact]
		public void CannotChangeWeightsWhileOptimizing()
		{
			SetupStandardSession();
			Client.StartOptimizing(SessionId);
			var weights = Client.GetObjectiveWeights(SessionId);
			weights[0].Weight += 1;
			AssertException(() => Client.SetObjectiveWeights(SessionId, weights), "An attempt was made to set objective weights while the optimisation was running.");
			Client.StopOptimizing(SessionId);
		}

		[Fact]
		public void CannotSetNegativeObjectiveWeight()
		{
			SetupStandardSession();
			var weights = Client.GetObjectiveWeights(SessionId);
			weights[0].Weight = -1;
			
			AssertException(() => Client.SetObjectiveWeights(SessionId, weights),
				"An error occurred setting objective weights: An attempt was made to set a negative weight for objective 'Total loss (MWh)'");
		}

		[Fact]
		public void CannotSetUnknownObjective()
		{
			SetupStandardSession();
			AssertException(() => Client.SetObjectiveWeights(SessionId, new List<ObjectiveWeight>
			{
				new ObjectiveWeight
				{
					ObjectiveName = "wrong name",
					Weight = -1,
				}
			}), "An error occurred setting objective weights: An attempt was made to set a weight for unknown objective 'wrong name'");
		}

		[Fact]
		public void ObjectiveWeightsChangesSolution()
		{
			// This network will feed from Gen1 if weighting loss, but feeds from Gen2 if weighting IMax.
			var network = NetworkBuilder.Create(
				"Gen1[generatorVoltage=100] -- Line1[open;r=0;iMax=0] -- Consumer[consumption=(1,0)]",
				"Gen2[generatorVoltage=100] -- Line2[open;r=100;iMax=1e9] -- Consumer"
			);

			SetupSession(network);

			SetOnlyObjective(SessionId, "Total loss (MWh)");
			Optimize(requiredObjectiveValue: 0);

			AssertSingleClosedSwitch(Client.GetSolution(SessionId, "best").PeriodSettings[0], "Line1");
			var solutionInfo1 = Client.GetBestSolutionInfo(SessionId);
			Assert.True(solutionInfo1.IsFeasible);
			var value1 = solutionInfo1.ObjectiveValue;
			Assert.True(value1 == 0); // There is no loss, and we only weight the loss.

			SetOnlyObjective(SessionId, "IMax violation (Ah)");
			AssertSingleClosedSwitch(Client.GetSolution(SessionId, "best").PeriodSettings[0], "Line1");
			var solutionInfo2 = Client.GetBestSolutionInfo(SessionId);
			Assert.True(solutionInfo2.IsFeasible);
			var value2 = solutionInfo2.ObjectiveValue;

			Assert.True(value2 > value1); // We switched objective to IMAX, so the cost for the same solution has now increased.

			Optimize(requiredObjectiveValue: value2 * 0.99);

			AssertSingleClosedSwitch(Client.GetSolution(SessionId, "best").PeriodSettings[0], "Line2");
			var solutionInfo3 = Client.GetBestSolutionInfo(SessionId);
			Assert.True(solutionInfo3.IsFeasible);
			var value3 = solutionInfo3.ObjectiveValue;

			Assert.True(value3 < value2); // The optimization has now switched to feeding from Line2/Gen2, so the cost has decreased again.
		}

		private void AssertSingleClosedSwitch(SinglePeriodSettings singlePeriodSettings, string switchClosed)
		{
			Assert.Contains(singlePeriodSettings.SwitchSettings, s => s.Id == switchClosed);
			Assert.True(singlePeriodSettings.SwitchSettings.All(s => (s.Id == switchClosed) == !s.Open));
		}

		private void SetOnlyObjective(string sessionId, string objectiveName)
		{
			var weights = Client.GetObjectiveWeights(sessionId);
			Assert.Contains(weights, w => w.ObjectiveName == objectiveName);
			foreach (var w in weights)
				if (w.ObjectiveName == objectiveName)
					w.Weight = 1.0;
				else
					w.Weight = 0.0;

			Client.SetObjectiveWeights(sessionId, weights);
		}
	}

}