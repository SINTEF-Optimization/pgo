using Newtonsoft.Json;
using Sintef.Scoop.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Sintef.Pgo.DataContracts
{
	/// <summary>
	/// Summarized information about a solution.
	/// </summary>
	public class SolutionInfo
	{
		/// <summary>
		/// True if the solution is feasible (legal), false if not
		/// </summary>
		[DefaultValue(false)]
		[JsonProperty(PropertyName = "is_feasible", DefaultValueHandling = DefaultValueHandling.Populate)]
		public bool IsFeasible { get; set; }

		/// <summary>
		/// True if the solution has been mathematically proven to be optimal, false if not.
		/// (Note that a solution may still be optimal, even if this has not been proven and this flag is false.
		/// </summary>
		[DefaultValue(false)]
		[JsonProperty(PropertyName = "is_optimal", DefaultValueHandling = DefaultValueHandling.Populate)]
		public bool IsOptimal { get; set; } = false;

		/// <summary>
		/// The objective value of the solution.
		/// </summary>
		[DefaultValue(double.PositiveInfinity)]
		[JsonProperty(PropertyName = "objective_value", DefaultValueHandling = DefaultValueHandling.Populate)]
		public double ObjectiveValue { get; set; }

		/// <summary>
		/// Total component values for the solution.
		/// </summary>
		[JsonProperty(PropertyName = "objective_components")]
		public List<ObjectiveComponentWithWeight> ObjectiveComponents { get; set; }

		/// <summary>
		/// Per-period information for the solution
		/// </summary>
		[JsonProperty(PropertyName = "period_information")]
		public List<PeriodInfo> PeriodInformation { get; set; }

		/// <summary>
		/// If the solution is infeasible, lists the contstraints that are violated
		/// </summary>
		[JsonProperty(PropertyName = "violations")]
		public List<ConstraintViolationInfo> ViolatedConstraints { get; set; } = new List<ConstraintViolationInfo>();

		/// <summary>
		/// Writes the solution info prettely to a string.
		/// </summary>
		/// <returns></returns>
		public override string ToString()
		{
			string result = $"Feasibility = {IsFeasible}\n\r";
			if (!IsFeasible)
			{
				result += "\tViolated constraints:\r\n";
				ViolatedConstraints.Do(c => result += $"\t\t{c}\r\n");
			}
			result += "\n\r";
			result += $"Optimality = {IsOptimal}\n\r";
			result += $"Objective value, total = {ObjectiveValue}\n\r";
			ObjectiveComponents.Do(c => result += $"\t{c.Name}: {c.Value}\n\r");

			if (PeriodInformation.Count > 1)
			{
				result += "\n\r";
				result += $"Period information:\n\r";
				PeriodInformation.Do(p => result += $"\t{p.ToString()}");
			}
			
			return result;
		}
	}

	/// <summary>
	/// Information about a violated constraint and how it has been violated.
	/// </summary>
	public class ConstraintViolationInfo
	{
		/// <summary>
		///  A short description/name of the constraint.
		/// </summary>
		[JsonProperty(PropertyName = "name")]
		public string Name { get; set; }

		/// <summary>
		/// A description of how the constraint has been violated.
		/// </summary>
		[JsonProperty(PropertyName = "description")]
		public string Description { get; set; }
	}

	/// <summary>
	/// Information about the weight of a single objective component for the whole solution.
	/// </summary>
	public class ObjectiveComponentWithWeight
	{
		/// <summary>
		/// Objective component name.
		/// </summary>
		[JsonProperty(PropertyName = "name")]
		public string Name;

		/// <summary>
		/// Value of the objective component.
		/// </summary>
		[JsonProperty(PropertyName = "value")]
		public double Value;

		/// <summary>
		/// Weight of the objective component in the aggregate objective.
		/// </summary>
		[JsonProperty(PropertyName = "weight")]
		public double Weight;
	}

	/// <summary>
	/// Per-period information about a solution.
	/// </summary>
	public class PeriodInfo
	{
		/// <summary>
		/// The period in question.
		/// </summary>
		[JsonProperty(PropertyName = "period")]
		public Period Period;

		/// <summary>
		/// The number of changed switches since the previous period.
		/// </summary>
		[JsonProperty(PropertyName = "changed_switches")]
		public int ChangedSwitches;

		/// <summary>
		/// Writes the period info prettely to a string.
		/// </summary>
		/// <returns></returns>
		public override string ToString() => $"Period = {Period.Id}, Number of changed switches = {ChangedSwitches}";
	}
}
