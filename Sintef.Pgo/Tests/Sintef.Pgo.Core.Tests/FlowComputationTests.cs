using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sintef.Pgo.Core.Test
{
	[TestClass]
	public class FlowComputationTestsIEEE34
	{
		[TestMethod]
		public void VerifyFailureWithoutSwitches()
		{
			var flowProvider = new SimplifiedDistFlowProvider();
			PgoProblem problem = new PgoProblem(IEEE34NetworkMaker.IEEE34(), flowProvider);
			PgoSolution solution = new PgoSolution(problem);
			PeriodSolution perSol = solution.SinglePeriodSolutions.Single();
			bool result = solution.ComputeFlow(perSol.Period, flowProvider);

			Assert.IsFalse(result);
		}

		[TestMethod]
		public void VerifyFailureWith858To834Switch()
		{
			var flowProvider = new SimplifiedDistFlowProvider();
			PgoProblem problem = new PgoProblem(IEEE34NetworkMaker.IEEE34(), flowProvider);
			PgoSolution solution = new PgoSolution(problem);
			PeriodSolution perSol = solution.SinglePeriodSolutions.Single();

			perSol.SetSwitch("858", "834", true);

			bool result = solution.ComputeFlow(perSol.Period, flowProvider);

			Assert.IsFalse(result);
		}
	}
}
