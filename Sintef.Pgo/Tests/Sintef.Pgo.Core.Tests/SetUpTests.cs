using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sintef.Pgo.Core.Test
{
	[TestClass]
	public class SetUpTests
	{
		[TestMethod]
		public void CreateEmptyProblemAndSolution()
		{
			PowerNetwork powerNetwork = new PowerNetwork();
			PowerDemands demands = new PowerDemands(powerNetwork);
			PeriodData pd = new PeriodData(powerNetwork);
			PgoProblem problem = new PgoProblem(pd, new SimplifiedDistFlowProvider());
			PgoSolution solution = new PgoSolution(problem);
		}

		[TestMethod]
		public void CreateIEEE34ProblemAndSolution()
		{
			PeriodData perData = IEEE34NetworkMaker.IEEE34();
			PgoProblem problem = new PgoProblem(perData, new SimplifiedDistFlowProvider());
			PgoSolution solution = new PgoSolution(problem);
		}
	}
}
