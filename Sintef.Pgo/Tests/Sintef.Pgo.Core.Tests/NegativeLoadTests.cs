using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sintef.Scoop.Kernel;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core.Test
{
	/// <summary>
	/// Tests for consumers with negative net active load (local generation exceeds local load).
	/// </summary>
	[TestClass]
	public class NegativeLoadTests
	{
		#region Input

		[TestMethod]
		public void PowerDemandsAcceptNegativeActiveDemand()
		{
			var network = NetworkBuilder.Create("Producer[generatorVoltage=1000.0] -- Line1[r=1] -- Consumer[consumption=(100, 0)]").Network;
			var consumer = network.GetBus("Consumer");

			var demands = new PowerDemands(network);
			demands.SetPowerDemand(consumer, new Complex(-1000, 200));
			Assert.AreEqual(new Complex(-1000, 200), demands.PowerDemand(consumer));

			demands.SetActivePowerDemand(consumer, -5);
			Assert.AreEqual(-5, demands.ActivePowerDemand(consumer));
		}

		[TestMethod]
		public void NetworkBuilderAcceptsNegativeConsumption()
		{
			var builder = NetworkBuilder.Create("Producer[generatorVoltage=1000.0] -- Line1[r=1] -- Consumer[consumption=(-100, 10)]");

			Assert.AreEqual(new Complex(-100, 10), builder.Demands.PowerDemand(builder.Network.GetBus("Consumer")));
		}

		#endregion
	}
}
