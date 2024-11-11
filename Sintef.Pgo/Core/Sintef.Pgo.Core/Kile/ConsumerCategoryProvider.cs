using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sintef.Scoop.Utilities;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Provides data on what consumer category/ies the consumers in a network belong to.
	/// A consumer may belong to several categories, with a fraction given for each.
	/// The fractions for one consumer must sum to 1.
	/// </summary>
	public class ConsumerCategoryProvider
	{
		#region Private data members

		/// <summary>
		/// The network whose consumers we store data for
		/// </summary>
		private PowerNetwork _network;

		/// <summary>
		/// The consumer category data, per consumer
		/// </summary>
		private Dictionary<Bus, CategoryDistribution> _data;

		/// <summary>
		/// Contains all consumer categories
		/// </summary>
		private List<ConsumerCategory> _allCategories;

		#endregion

		/// <summary>
		/// Initializes a provider with no data
		/// </summary>
		/// <param name="network"></param>
		public ConsumerCategoryProvider(PowerNetwork network)
		{
			_network = network;
			_data = new Dictionary<Bus, CategoryDistribution>();
			_allCategories = Enum.GetValues(typeof(ConsumerCategory)).Cast<ConsumerCategory>().ToList();
		}

		/// <summary>
		/// Creates and returns a provider where each consumer is assigned to a random category, or a random distribution between
		/// two random categories.
		/// </summary>
		/// <param name="network">The network to consider</param>
		/// <param name="random">The random generator to use. If null, one is created</param>
		public static ConsumerCategoryProvider RandomFor(PowerNetwork network, Random random = null)
		{
			if (random == null)
				random = new Random();

			var provider = new ConsumerCategoryProvider(network);
			provider.Randomize(random);
			return provider;
		}

		/// <summary>
		/// Returns true if this provider has category data for the given consumer
		/// </summary>
		public bool HasDataFor(Bus consumer) => _data.ContainsKey(consumer);

		/// <summary>
		/// Clones the category data to use the bus objects of the given network.
		/// </summary>
		/// <param name="network"></param>
		/// <returns></returns>
		public ConsumerCategoryProvider CloneFor(PowerNetwork network)
		{
			ConsumerCategoryProvider ccc = new ConsumerCategoryProvider(network);
			foreach (var d in _data)
			{
				Bus nbus = network.GetBus(d.Key.Name);
				CategoryDistribution dist = new CategoryDistribution(d.Value);
				ccc._data[nbus] = dist;
			}
			return ccc;
		}

		/// <summary>
		/// Enumerates the categories that the given <paramref name="consumer"/> belongs to
		/// </summary>
		public IEnumerable<ConsumerCategory> Categories(Bus consumer)
		{
			if (_data.TryGetValue(consumer, out CategoryDistribution fractions))
				return fractions.Categories;

			throw new Exception($"No consumer category data is given for {consumer}");
		}

		/// <summary>
		/// Returns the fraction of the given consumer's power demand that is used
		/// in the given consumer category
		/// </summary>
		public double ConsumptionFraction(Bus consumer, ConsumerCategory category)
		{
			if (!consumer.IsConsumer)
				throw new ArgumentException($"{consumer} is not a consumer");

			if (_data.TryGetValue(consumer, out var fractions))
				return fractions.Fraction(category);

			throw new Exception($"No consumer category data is given for {consumer}");
		}

		/// <summary>
		/// Sets the fraction of the given consumer's power demand that is used
		/// in the given consumer category to the given value
		/// </summary>
		public void Set(Bus consumer, ConsumerCategory category, double fraction = 1.0)
		{
			if (!consumer.IsConsumer)
				throw new ArgumentException($"{consumer} is not a consumer");

			if (!_data.ContainsKey(consumer))
				_data.Add(consumer, new CategoryDistribution());

			_data[consumer].Set(category, fraction);
		}

		/// <summary>
		/// Assigns each consumer to a random category, or a random distribution between
		/// two random categories
		/// </summary>
		/// <param name="random">The random generator to use</param>
		/// <param name="probabilityOfSingleCategoryCustomers">The probability of customers having only a single category assigned.</param>
		public void Randomize(Random random, double probabilityOfSingleCategoryCustomers=0.5)
		{
			_data.Clear();

			foreach (var consumer in _network.Consumers)
			{
				if (random.NextDouble() < probabilityOfSingleCategoryCustomers)
				{
					Set(consumer, _allCategories.RandomElement(random));
				}
				else
				{
					double fraction = random.NextDouble();
					var category1 = _allCategories.RandomElement(random);
					var category2 = _allCategories.Except(category1).RandomElement(random);

					Set(consumer, category1, fraction);
					Set(consumer, category2, 1.0 - fraction);
				}
			}
		}

		/// <summary>
		/// Data for one consumer, giving a distribution over consumer categories
		/// </summary>
		private class CategoryDistribution
		{
			public IEnumerable<ConsumerCategory> Categories => _fractions.Keys;

			/// <summary>
			/// True if the category fractions sum to 1
			/// </summary>
			bool _isOk = false;

			/// <summary>
			/// The fraction for each consumer category
			/// </summary>
			Dictionary<ConsumerCategory, double> _fractions = new Dictionary<ConsumerCategory, double>();


			/// <summary>
			/// Default constructor
			/// </summary>
			public CategoryDistribution()
			{
			}


			/// <summary>
			/// Copy constructor
			/// </summary>
			/// <param name="other"></param>
			public CategoryDistribution(CategoryDistribution other)
			{
				_isOk = other._isOk;
				_fractions = new Dictionary<ConsumerCategory, double>(other._fractions);
			}


			/// <summary>
			/// Returns the fraction of the consumer's power demand that is used
			/// in the given consumer category
			/// </summary>
			public double Fraction(ConsumerCategory category)
			{
				if (_isOk)
					return _fractions[category];

				throw new Exception("The fractions for consumer categories do not add up to 1");
			}

			/// <summary>
			/// Records the fraction of the consumer's power demand that is used
			/// in the given consumer category
			/// </summary>
			public void Set(ConsumerCategory category, double fraction)
			{
				_fractions[category] = fraction;

				_isOk = _fractions.Values.Sum().EqualsWithTolerance(1.0, 1e-10);
			}
		}
	}

	/// <summary>
	/// A category of power consumers, for KILE calculations
	/// </summary>
	public enum ConsumerCategory
	{
		/// <summary>
		/// 'Jordbruk'
		/// </summary>
		Agriculture,

		/// <summary>
		/// 'Husholdning'
		/// </summary>
		Domestic,

		/// <summary>
		/// 'Industri'
		/// </summary>
		Industry,

		/// <summary>
		/// 'Handel og tjenester'
		/// </summary>
		Trade,

		/// <summary>
		/// 'Offentlig virksomhet'
		/// </summary>
		Public,

		/// <summary>
		/// 'Industri med eldrevne prosesser'
		/// </summary>
		ElectricIndustry
	}
}
