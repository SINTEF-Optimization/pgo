using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sintef.Pgo.Core.Test
{
	/// <summary>
	/// Test case based on IEEE 34 Node Test feeder from 92, see
	/// http://sites.ieee.org/pes-testfeeders/resources/ .
	/// 
	/// We do not use the actual information other than the
	/// network topology, because we just want a test topology to be here.
	/// 
	/// Once we converge on file formats we should redo some tests with real data,
	/// but for now this conveniently lets us ignore the practical problems.
	/// 
	/// For the sake of interest I have placed a substation at node 800, 
	/// and another at node 888, ignoring all the other stuff in the original case.
	/// 
	/// The indices of the original problem do not survive here, so the topology is all we have
	/// after creation.
	/// 
	/// The switchable lines are 814-850, 830-854 and 858-834 (this last is always going to be infeasible).
	/// </summary>
	public static class IEEE34NetworkMaker
	{
		public static PeriodData IEEE34()
		{
			PowerNetwork network = IO.PgoJsonParser.ParseNetworkFromJsonFile(TestUtils.TestDataFile("ieee34.json"));
			PeriodData periodData = IO.PgoJsonParser.ParseDemandsFromJsonFile(network, TestUtils.TestDataFile("ieee34_forecast.json"))[0];
			return periodData;
		}

		public static PeriodData IEEE34WithDifferentGenVoltages()
		{
			PowerNetwork network = IO.PgoJsonParser.ParseNetworkFromJsonFile(TestUtils.TestDataFile("ieee34.json"));
			Bus lastProvider = network.Providers.Last();
			lastProvider.GeneratorVoltage *= 0.999;
			PeriodData periodData = IO.PgoJsonParser.ParseDemandsFromJsonFile(network, TestUtils.TestDataFile("ieee34_forecast.json"))[0];
			return periodData;
		}
	}
}
