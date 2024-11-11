using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;
using System.Diagnostics;

namespace Sintef.Pgo.Core.Test
{
	/// <summary>
	/// Tools for creating synthetic test instances.
	/// </summary>
	public class SyntheticProblemCreator
	{
		/*
		/// <summary>
		/// Creates a synthetic problem, based on a given original single period problem.
		/// First, the a set of copies of the original (with modified loads), are added with a number of random
		/// switchable lines to the original (and to each other).
		/// Seconds, this whole problem is duplicated for a number of time periods.
		/// </summary>
		/// <param name="problem">The single period problem to base the synthetic problem on</param>
		/// <param name="netDuplicationFactor">The number of duplicate networks that are added.</param>
		/// <param name="numberOfJoints">The number of switches joining each duplicate to the rest.</param>
		/// <param name="numberOfTimeSteps">The number of time steps (set to 1 for single period problems).</param>
		/// <param name="chosenFlowApproximation">The flow approximtion to use</param>
		/// <param name="reportingFlowApproximation">The flow approximtion to use for reporting</param>
		/// <returns>An "empty" initial solution containing the created problem, and the id of the session that was created for the problem</returns>
		public static (PgoSolution initialSol, string sessionID) CreateSessionWithSyntheticCase(PeriodData problem, 
			 int netDuplicationFactor, int numberOfJoints, int numberOfTimeSteps, 
			 FlowApproximation chosenFlowApproximation, FlowApproximation reportingFlowApproximation, Server server)
		{
			PgoProblem mPop = CreateSyntheticCase(problem, netDuplicationFactor, numberOfJoints, numberOfTimeSteps,
				chosenFlowApproximation, reportingFlowApproximation);
			var network = mPop.Network;
			server.AddNetwork("net", network);
			var session = server.CreateSession("mySyntheticSinglePeriodProblem", "net", mPop);
			var solution = session.ConstructEmptySolution();
			return (solution, session.ID);
		}
		*/

		/// <summary>
		/// Creates a synthetic problem, based on a given original problem .
		/// First, the a set of copies of the original (with modified loads), are added with a number of random
		/// switchable lines to the original (and to each other).
		/// Second, this whole problem is duplicated for a number of time periods.
		/// </summary>
		/// <param name="periodData">The description of the period sub problem to base the synthetic problem on</param>
		/// <param name="netDuplicationFactor">The number of duplicate networks that are added.</param>
		/// <param name="numberOfJoints">The number of switches joining each duplicate to the rest.</param>
		/// <param name="numberOfTimeSteps">The number of time steps (set to 1 for single period problems).</param>
		/// <param name="chosenFlowApproximation">The flow approximtion to use</param>
		/// <param name="reportingFlowApproximation">The flow approximtion to use for reporting</param>
		/// <returns>The created single- or multi-period problem</returns>
		public static PgoProblem CreateSyntheticCase(PeriodData periodData, 
			int netDuplicationFactor, int numberOfJoints, int numberOfTimeSteps, 
			FlowApproximation chosenFlowApproximation, FlowApproximation reportingFlowApproximation)
		{
			// Create a new single period problem
			PeriodData periodDataModified = periodData;

			// First, make a larger network.
			if (netDuplicationFactor > 1)
			{
				Random random = new Random();
				PowerNetwork newNetWork = periodData.Network.Clone();
				PowerDemands demandsInOriginal = periodData.Demands;
				PowerDemands newDemands = new PowerDemands(newNetWork);
				for (int i = 1; i < netDuplicationFactor; i++)
				{
					string namePostFix = $"_c{i}";
					newNetWork.JoinWithRandomSwitches(periodData.Network, numberOfJoints, namePostFix, random);
					demandsInOriginal.Demands.Do(kvp =>
					{
						Complex newDemand = PowerDemands.GetModifiedDemand(kvp.Value, random);
						newDemands.SetPowerDemand(newNetWork.GetBus(kvp.Key.Name + namePostFix), newDemand);
					});
				}

				periodDataModified = new PeriodData(newNetWork, newDemands, periodDataModified.Period);
			}

			// Create feasible (radial) single period input solution
			PeriodSolution radialInputSol = Utils.ConstructFeasibleSolution(periodDataModified, chosenFlowApproximation);

			if (radialInputSol == null)
				throw new Exception("Could not construct feasible solution.");

			// Create the new multi-period problem
			return CreateMultiPeriodProblem(periodDataModified, radialInputSol.NetConfig, null, numberOfTimeSteps, 
				chosenFlowApproximation, reportingFlowApproximation);
		}

		/// <summary>
		/// Creates a synthetic multi-period problem by randomly choosing some variation of demands and switching costs.
		/// The criteria set for the problem is the same as that for inputSol.Encoding.
		/// </summary>
		/// <param name="inputPeriodData">The single period problem that we use as a starting point</param>
		/// <param name="numPeriods"></param>
		private static PgoProblem CreateMultiPeriodProblem(PeriodData inputPeriodData,
			NetworkConfiguration startConfig, NetworkConfiguration targetEndConfig, int numPeriods, 
			FlowApproximation chosenFlowApproximation, FlowApproximation reportingFlowApproximation)
		{
			PowerNetwork network = inputPeriodData.Network;
			PowerDemands inputDemands = inputPeriodData.Demands;
			Random rand = new Random();
			DateTime startTime = inputPeriodData.Period.StartTime;
			DateTime endTime = inputPeriodData.Period.EndTime;
				TimeSpan periodLength = endTime - startTime;

			List<PeriodData> periodDatas = new List<PeriodData> { inputPeriodData };

			for (int i = 1; i < numPeriods; i++)
			{
				endTime += periodLength;
				startTime += periodLength;

				PowerDemands demands = inputDemands.Clone();

				//Modify demands
				network.Consumers.Do(c => demands.SetPowerDemand(c, PowerDemands.GetModifiedDemand(demands.PowerDemand(c), rand)));

				periodDatas.Add(new PeriodData(network, demands, new Period(startTime, endTime, i)));
			}

			var flowProvider = Utils.CreateFlowProvider(chosenFlowApproximation);
			var reportingFlowProvider = Utils.CreateFlowProvider(reportingFlowApproximation);
			var criteria = PgoProblem.CreateDefaultCriteriaset(flowProvider, network, periodDatas);
			return new PgoProblem(periodDatas, flowProvider, "Synthetic MultiPeriod", startConfig, targetEndConfig, criteria);
		}

	}
}
