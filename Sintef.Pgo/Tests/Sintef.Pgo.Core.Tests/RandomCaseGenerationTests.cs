using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Text;
using Sintef.Scoop.Kernel;

namespace Sintef.Pgo.Core.Test
{
	/// <summary>
	/// Tests of the <see cref="RandomNetworkBuilder"/>.
	/// </summary>
	[TestClass]
	public class RandomCaseGenerationTests
	{
		/// <summary>
		/// Checks that the suppression of faults around providers works
		/// </summary>
		[TestMethod]
		public void SuppressionOfFaultsAroundProvidersWorks()
		{
			var random = new Random();

			//for (int i = 0; i < 100; ++i)
			{
				int seed = random.Next();
				//seed = 328195747;
				Console.WriteLine(seed);
				random = new Random(seed);


				PowerNetwork grid = RandomNetworkBuilder.Create(100, 4, 4, 50, 3, 10, 10, true, buildAroundEachProvider: true, random: random);
				var problem = TestUtils.ProblemWithRandomDemands(grid);
				OptimizerParameters pars = new OptimizerParameters()
				{
					Algorithm = AlgorithmType.RuinAndRecreate,
					SimpleMPSolverSolverTimeOut = TimeSpan.FromSeconds(1)
				};
				var solution = new PgoSolution(problem);
				IOptimizer solver = new ConfigOptimizerFactory(new NetworkManager(problem.Network)).CreateOptimizer(pars, solution, problem.CriteriaSet, new OptimizerEnvironment());

				//This will trigger an exception if there fault suppression is not 
				//implemented correctly:
				var newSol = new OptimizerRunner(solver, pars.SimpleMPSolverSolverTimeOut).Optimize(solution);

			}
		}
	}
}
