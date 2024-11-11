using Sintef.Scoop.Kernel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Parameters for creating an optimizer
	/// </summary>
	public class OptimizerParameters : IOptimizerParameters
	{
		/// <summary>
		/// The algorithm type to use. The default is <see cref="AlgorithmType.RuinAndRecreate"/>
		/// </summary>
		public AlgorithmType Algorithm { get; set; } = AlgorithmType.RuinAndRecreate;

		/// <summary>
		/// Parameters for the meta-heuristic to use.
		/// </summary>
		[Browsable(false)] 
		public MetaHeuristicParameters MetaHeuristicParams { get; set; } = MetaHeuristicParameters.Standard();

		/// <summary>
		/// The type of solver used for construction of initial feasible solutions.
		/// </summary>
		public ConstructorType ConstructorType
		{
			get => MetaHeuristicParams.ConstructorType;
			set { MetaHeuristicParams.ConstructorType = value; }
		}

		/// <summary>
		/// The type of optimizer that is used in the local search and
		/// feasibility search phases of a meta-heuristic.
		/// </summary>
		[ReadOnly(false)]
		public OptimizerType OptimizerType
		{
			get => MetaHeuristicParams.OptimizerType;
			set { MetaHeuristicParams.OptimizerType = value; }
		}

		/// <summary>
		/// The value used for <see cref="ParallelNhDescent.MinimumParallelMoves"/>
		/// in the local search and feasibility search phases of a meta-heuristic.
		/// </summary>
		[ReadOnly(false)]
		public int MinimumParallelMoves
		{
			get => MetaHeuristicParams.MinimumParallelMoves;
			set { MetaHeuristicParams.MinimumParallelMoves = value; }
		}

		/// <summary>
		/// The type of neighborhood explorer that is used in the local search and
		/// feasibility search phases of a meta-heuristic.
		/// </summary>
		[ReadOnly(false)]
		public ExplorerType ExplorerType
		{
			get => MetaHeuristicParams.ExplorerType;
			set { MetaHeuristicParams.ExplorerType = value; }
		}

		/// <summary>
		/// The number of Descent iterations that pass before a new best solution is reported from Descent.
		/// A value of 0 means that improvements fire the BestSolutionFound event right away.
		/// A higher value can be used to limit the frequency that this event is fired in search phases where frequent
		/// improvements are found. Note that new best solutions are always reported, just a little later.
		/// The default value is 10.
		/// </summary>
		public int DescentReportingDelay
		{
			get => MetaHeuristicParams.DescentReportingDelay;
			set { MetaHeuristicParams.DescentReportingDelay = value; }
		}

		#region Properties specific for the SimpleMultiPeriodsolver

		/// <summary>
		/// The maximum number of passes used by a <see cref="SimpleMultiPeriodsolver"/>. 
		/// Ignored by other solvers.
		/// </summary>
		[Category("SimpleMPSolver")]
		public int SimpleMPSolverMaxIterations { get; set; } = -1;
		 
		/// <summary>
		/// Optional solver timeout. Currently used by <see cref="SimpleMultiPeriodsolver"/>. 
		/// Ignored by other solvers.
		/// </summary>
		[Description("Timeout for Multiperiod Solver"), Category("SimpleMPSolver")]
		public TimeSpan SimpleMPSolverSolverTimeOut { get; set; } = default(TimeSpan);

		
		#endregion

		/// <summary>
		/// Returns a clone of this object.
		/// Profiling/logging properties are not included.
		/// </summary>
		public object Clone()
		{
			return new OptimizerParameters()
			{
				Algorithm = Algorithm,
				MetaHeuristicParams = MetaHeuristicParams.Clone(),
				SimpleMPSolverMaxIterations = SimpleMPSolverMaxIterations,
				SimpleMPSolverSolverTimeOut = SimpleMPSolverSolverTimeOut,
			};
		}

		/// <summary>
		/// Returns a string description of the object
		/// </summary>
		public override string ToString()
		{
			return "Pgo OptimizerParameters";
		}
	}

	/// <summary>
	/// A type of algorithm for optimizing <see cref="PgoSolution"/>s
	/// </summary>
	public enum AlgorithmType
	{
		/// <summary>
		/// A random restart meta-heuristic, single period (<see cref="RandomRestartMetaHeuristic"/>).
		/// </summary>
		RandomRestart,

		/// <summary>
		/// A simple period-traversing iterative improvement, using random restart per period (<see cref="Core.SimpleMultiPeriodsolver"/>).
		/// </summary>
		SimpleMultiPeriodsolver,

		/// <summary>
		/// A ruin-and-recreate based meta-heuristic (<see cref="RuinAndRecreateMultiPeriod"/>).
		/// </summary>
		RuinAndRecreate,
	}
	
	/// <summary>
	/// An enumeration of different kinds of constructors that are available for 
	/// constructing radial and feasible single period solutions.
	/// </summary>
	public enum ConstructorType
	{
		/// <summary>
		/// A heuristic that in an iterative and random way creates first a radial 
		/// solution, and then (if this is infeasible) applies a local search repair.
		/// </summary>
		MakeRadialAndFeasibleHeuristic
	}
}
