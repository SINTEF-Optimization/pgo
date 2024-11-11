using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sintef.Pgo.Cim;
using Sintef.Pgo.Core.IO;

namespace Sintef.Pgo.Core.Test
{
	/// <summary>
	/// Tests for <see cref="CimSolutionConverter"/>
	/// </summary>
	[TestClass]
	public class CimSolutionConverterTests : CimTestFixture
	{
		[TestMethod]
		public void ConverterConvertsASolution()
		{
			ConvertNetwork();
			ReadAndConvertDiginDemands();
			CreateEncodingAndSolution();

			// Convert solution to CIM
			var converter = new CimSolutionConverter(_networkConverter);
			var cimSolution = converter.ConvertToCim(_disaggregatedSolution);

			Assert.AreEqual(1, cimSolution.PeriodSolutions.Count);
			var periodSolution = cimSolution.PeriodSolutions.Single();

			// Each switch in the CIM network should be represented,
			// with the same class and MRID
			CollectionAssert.AreEquivalent(
				_networkConverter.CimNetwork.Switches.Select(x => (x.GetType(), x.MRID)).ToList(),
				periodSolution.Switches.Select(x => (x.GetType(), x.MRID)).ToList());

			// All switches should have the Open property.
			Assert.AreEqual(0, periodSolution.Switches.Count(s => s.Open is null));

			// Two should be true, since there are two cycles in the problem, that need to be broken.
			Assert.AreEqual(2, periodSolution.Switches.Count(s => s.Open == true));
		}
	}
}