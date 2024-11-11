using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sintef.Scoop.Kernel;

namespace Sintef.Pgo.Core.Test
{
	/// <summary>
	/// Tests for full optimization based on CIM data
	/// </summary>
	[TestClass]
	public class CimOptimizationTests : CimTestFixture
	{
		[TestInitialize]
		public new void Setup()
		{
			base.Setup();

			ConvertNetwork(lineImpedanceScaleFactor: 0.001);
			ReadAndConvertDiginDemands();

			CreateEncodingAndSolution();
		}

		[TestMethod]
		public void FlowCanBeComputedForAggregatedSolution()
		{
			var flow = _aggregateSolution.SinglePeriodSolutions.Single().Flow(_flowProvider);
			var provider = _aggregateNetwork.Providers.Single();

			// The total demand for consumers is    (10567000,   2084480)
			Complex expectedProduction = new Complex(10568186.7, 2084756.1);

			Assert.IsTrue((expectedProduction - flow.PowerInjection(provider)).Magnitude < 0.2);
		}

		[TestMethod]
		public void SolutionCanBeOptimized()
		{
			var solution = new PgoSolution(_originalEncoding);
			double initialValue = solution.ObjectiveValue;
			Console.WriteLine($"Initial feasibility: {solution.IsFeasible}");
			Console.WriteLine($"Initial objective: {initialValue}");

			// Create optimizer

			OptimizerParameters parameters = new OptimizerParameters()
			{
				Algorithm = AlgorithmType.RuinAndRecreate,
				OptimizerType = OptimizerType.Descent,
				ExplorerType = ExplorerType.DeltaExplorer
			};

			IOptimizer solver = new ConfigOptimizerFactory().CreateOptimizer(parameters, solution, _originalEncoding.CriteriaSet, new OptimizerEnvironment());

			// Attach to event to count iterations

			var eventManager = new EventHandlerManager();
			eventManager.StartWatching(solver);

			int iterations = 0;
			eventManager.IterationHandlers.Add((s, e) => ++iterations);

			// Optimize

			solution = new OptimizerRunner(solver, maxTime: TimeSpan.FromSeconds(0.5)).Optimize(solution) as PgoSolution;

			Assert.IsTrue(solution.IsFeasible);
			double finalValue = solution.ObjectiveValue;
			Console.WriteLine($"Final objective: {finalValue}");
			Console.WriteLine($"Iterations: {iterations}");

			Assert.IsTrue(iterations > 0);
		}
	}
}