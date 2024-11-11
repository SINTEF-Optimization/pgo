using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sintef.Scoop.Kernel;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Base class for criteria that use a flow provider
	/// </summary>
	public abstract class FlowDependentCriterion : Criterion
	{
		/// <summary>
		/// The flow provider that is used to compute flows and delta flows.
		/// </summary>
		public IFlowProvider FlowProvider { get; }

		/// <summary>
		/// Initializes the criterion
		/// </summary>
		/// <param name="flowProvider">The flow provider to use</param>
		public FlowDependentCriterion(IFlowProvider flowProvider)
		{
			FlowProvider = flowProvider;
		}

		/// <summary>
		/// Creates a new criterion equivalent to this one, except that it uses the given 
		/// flow provider instead
		/// </summary>
		public abstract FlowDependentCriterion WithProvider(IFlowProvider flowProvider);

		/// <summary>
		/// Returns a description of the criterion
		/// </summary>
		/// <returns></returns>
		public override string ToString()
		{
			return base.ToString() + $" ({FlowProvider})";
		}
	}


	/// <summary>
	/// Extension methods related to <see cref="FlowDependentCriterion"/>
	/// </summary>
	public static partial class Extensions
	{
		/// <summary>
		/// Returns the flow provider used by the criteria in the given set.
		/// Throws an exception if there are more (or less) than one.
		/// </summary>
		/// <param name="criteriaSet"></param>
		/// <returns></returns>
		public static IFlowProvider FlowProvider(this ICriteriaSet criteriaSet)
		{
			var x = criteriaSet.Constraints
				.OfType<FlowDependentCriterion>()
				.Select(c => c.FlowProvider);

			var y = (criteriaSet.Objective as AggregateObjective).Components
			.OfType<FlowDependentCriterion>()
			.Select(c => c.FlowProvider);

			return x.Concat(y).Distinct().Single();
		}

		/// <summary>
		/// Creates a new criterion equivalent to this one, except that all criteria 
		/// (that use flows) use the given flow provider
		/// </summary>
		/// <param name="originalSet">The criteria set to copy from</param>
		/// <param name="provider">The provider to use</param>
		/// <returns></returns>
		public static CriteriaSet WithProvider(this ICriteriaSet originalSet, IFlowProvider provider)
		{
			CriteriaSet newCrit = new CriteriaSet();

			foreach (var constraint in originalSet.Constraints)
			{
				if (constraint is FlowDependentCriterion flowConstraint)
					newCrit.AddConstraint(flowConstraint.WithProvider(provider));
				else
					newCrit.AddConstraint(constraint);
			}

			AggregateObjective agg = new AggregateObjective();
			foreach (var (objective, weight) in (originalSet.Objective as AggregateObjective).WeightedComponents)
			{
				if (objective is FlowDependentCriterion flowObjective)
					agg.AddComponent(flowObjective.WithProvider(provider), weight);
				else
					agg.AddComponent(objective, weight);
			}
			newCrit.AddObjective(agg);

			return newCrit;
		}
	}

}
