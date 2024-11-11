using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core
{

	/// <summary>
	/// An optimizer factory that creates the overall optimizer for network configurations.
	/// </summary>
	public class ConfigOptimizerFactory : IOptimizerFactory
	{
		/// <summary>
		/// The factory to use for creating the inner optimizer
		/// </summary>
		public IOptimizerFactory InnerOptimizerFactory { get; set; } = new OptimizerFactory();

		#region Private data members

		/// <summary>
		/// The network manager that is passed to the ConfigOptimizer
		/// </summary>
		private NetworkManager _networkManager;

		#endregion

		#region Construction

		/// <summary>
		/// Constructor
		/// </summary>
		public ConfigOptimizerFactory(NetworkManager networkManager = null)
		{
			_networkManager = networkManager;
		}

		#endregion

		#region Public methods

		/// <summary>
		/// Creates a <see cref="ConfigOptimizer"/>.
		/// </summary>
		/// <param name="parameters">Optimization parameters which will determine the exact algorithms to use.</param>
		/// <param name="solution">Not used</param>
		/// <param name="criteria">Not used</param>
		/// <param name="environment"></param>
		/// <returns></returns>
		public IOptimizer CreateOptimizer(IOptimizerParameters parameters, ISolution solution, ICriteriaSet criteria,
			OptimizerEnvironment environment)
		{
			var networkManager = _networkManager;

			// If no network manager is given, create one
			if (networkManager == null)
			{
				networkManager = new NetworkManager((solution as PgoSolution).Problem.Network);
			}

			// Decode parameters

			OptimizerParameters innerSolverParameters = null;

			if (parameters is ConfigOptimizerParameters myParameters)
				innerSolverParameters = myParameters.InnerSolverParameters;
			else
			{
				myParameters = new ConfigOptimizerParameters();
				innerSolverParameters = parameters as OptimizerParameters;
			}


			// Check parameters for consistency
			if ((!myParameters.SubstitutesFlowProvider) && criteria.FlowProvider().FlowApproximation != FlowApproximation.SimplifiedDF
				&& innerSolverParameters.MetaHeuristicParams.ExplorerType != ExplorerType.ReverseExplorer)
				throw new Exception("The inner optimizer only works with Simplified DistFlow, unless a 'ReverseExplorer' is used to explore LS neighbourhoods");

			// Create config coptimizer

			ConfigOptimizer configOptimizer = new ConfigOptimizer(innerSolverParameters, networkManager, environment);

			configOptimizer.Aggregates = myParameters.Aggregates;
			configOptimizer.SubstitutesFlowProvider = myParameters.SubstitutesFlowProvider;

			return configOptimizer;
		}

		#endregion
	}

	/// <summary>
	/// Parameters for creating a <see cref="ConfigOptimizer"/>
	/// </summary>
	public class ConfigOptimizerParameters : IOptimizerParameters
	{
		/// <summary>
		/// Parameters for creating the inner solver
		/// </summary>
		public OptimizerParameters InnerSolverParameters { get; set; }

		/// <summary>
		/// If true, the optimizer will aggregate the problem it's given to be acyclic
		/// and connected before passing it to the inner optimizer.
		/// </summary>
		public bool Aggregates { get; set; } = true;

		/// <summary>
		/// If true, the optimizer will substitute the problem's flow solver with
		/// a <see cref="SimplifiedDistFlowProvider"/> before passing it to the inner optimizer.
		/// True by default.
		/// </summary>
		public bool SubstitutesFlowProvider { get; set; } = true;

		/// <summary>
		/// Returns a deep copy of this object
		/// </summary>
		public object Clone()
		{
			return new ConfigOptimizerParameters()
			{
				Aggregates = Aggregates,
				SubstitutesFlowProvider = SubstitutesFlowProvider,
				InnerSolverParameters = (OptimizerParameters)InnerSolverParameters.Clone()
			};
		}
	}
}
