using System;
using System.IO;
using System.Linq;
using System.Numerics;
using C5;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sintef.Pgo.Core.Test
{
	/// <summary>
	/// Tests for <see cref="CimConfigurationConverter"/>
	/// </summary>
	[TestClass]
	public class CimConfigurationConverterTests : CimTestFixture
	{
		[TestMethod]
		public void ConverterCreatesDemands()
		{
			ConvertNetwork();
			ReadAndConvertDiginConfiguration();

			var switchSettings = _configuration.SwitchSettings;

			Assert.AreEqual(2, switchSettings.OpenSwitches.Count());

			// All switches have a state
			CollectionAssert.AreEquivalent(_network.SwitchableLines.ToList(),
				switchSettings.OpenSwitches.Concat(switchSettings.ClosedSwitches).ToList());
		}
	}
}