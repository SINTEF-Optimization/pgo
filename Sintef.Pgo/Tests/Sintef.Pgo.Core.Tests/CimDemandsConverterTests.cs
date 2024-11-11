using System;
using System.IO;
using System.Linq;
using System.Numerics;
using C5;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sintef.Pgo.Cim;
using Sintef.Pgo.DataContracts;

namespace Sintef.Pgo.Core.Test
{
	/// <summary>
	/// Tests for <see cref="CimDemandsConverter"/>
	/// </summary>
	[TestClass]
	public class CimDemandsConverterTests : CimBuilderTestFixture
	{
		[TestMethod]
		public void ConverterCreatesDemandsForEquivalentInjectionConsumers()
		{
			_networkBuilder.AddEquivalentInjection("consumer");
			_networkOptions.ConsumerSources.Add(CimConsumerSource.EquivalentInjections);
			ConvertNetwork();

			_demandsBuilder.AddEquivalentInjection("consumer", pWatt: 100, qVar: 5);
			ConvertDemands();

			Assert.AreEqual(new Complex(100, 5), _demands.Demands.Single().Value);
		}
	}
}