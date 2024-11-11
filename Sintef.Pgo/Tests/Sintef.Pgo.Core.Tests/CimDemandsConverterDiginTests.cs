using System;
using System.IO;
using System.Linq;
using System.Numerics;
using C5;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sintef.Pgo.Core.Test
{
	/// <summary>
	/// Tests for <see cref="CimDemandsConverter"/>, based on the DIGIN dataset
	/// </summary>
	[TestClass]
	public class CimDemandsConverterDiginTests : CimTestFixture
	{
		[TestMethod]
		public void ConverterCreatesDemands()
		{
			ConvertNetwork();
			ReadAndConvertDiginDemands();

			Assert.AreEqual(new Complex(10567000, 2084480), _demands.Sum);

			// All consumers have a demand
			CollectionAssert.AreEquivalent(_network.Consumers.ToList(), _demands.Demands.Select(kv => kv.Key).ToList());
		}
	}
}