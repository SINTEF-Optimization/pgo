using Sintef.Pgo.Core;
using Sintef.Scoop.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sintef.Pgo.Core
{
	/// <summary>
	/// Extension methods working with enumerations
	/// </summary>
	public static class EnumerableExtensions
	{
		/// <summary>
		/// Projects each element of a sequence into a new form and returns the common value produced.
		/// If any element projects to <see langword="null"/> or elements project to different values,
		/// an exception is thrown instead.
		/// </summary>
		/// <typeparam name="TSource">The type of the sequence</typeparam>
		/// <typeparam name="TResult">The type of the result</typeparam>
		/// <param name="source">The source sequence</param>
		/// <param name="selector">Used to project each element to a value</param>
		/// <param name="description">A description of the set of values, for use in the exception message</param>
		/// <returns>The common value produced</returns>
		/// <exception cref="Exception">Thrown if any element projects to <see langword="null"/> or elements project to different values</exception>
		public static TResult Common<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> selector, string description)
			where TResult : class
		{
			return source
				.Select(selector)
				.Common(description);
		}

		/// <summary>
		/// Returns the common value among the elements of <paramref name="source"/>.
		/// If <paramref name="source"/> contains <see langword="null"/> or two different elements,
		/// an exception is thrown instead.
		/// </summary>
		/// <typeparam name="T">The type of elements in the sequence</typeparam>
		/// <param name="source">The source sequence</param>
		/// <param name="description">A description of the set of elements, for use in the exception message</param>
		/// <returns>The common value produced</returns>
		/// <exception cref="Exception">Thrown if any element is <see langword="null"/> or there are two different elements</exception>
		public static T Common<T>(this IEnumerable<T> source, string description)
			where T : class
		{
			if (source.Contains(null))
				throw new Exception($"Found null among {description}");

			var groups = source.GroupBy(x => x);
			if (groups.Count() > 1)
				throw new Exception($"Found different values among {description}");

			return groups.Single().Key;
		}

		/// <summary>
		/// Projects each element of a sequence into a new form and returns the common value produced.
		/// If any element projects to <see langword="null"/> or elements project to different values,
		/// returns null.
		/// </summary>
		/// <typeparam name="TSource">The type of the sequence</typeparam>
		/// <typeparam name="TResult">The type of the result</typeparam>
		/// <param name="source">The source sequence</param>
		/// <param name="selector">Used to project each element to a value</param>
		/// <returns>The common value produced, or null of there are different values</returns>
		public static TResult Common<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> selector)
			where TResult : class
		{
			return source
				.Select(selector)
				.Common();
		}

		/// <summary>
		/// Returns the common value among the elements of <paramref name="source"/>.
		/// If <paramref name="source"/> contains <see langword="null"/> or two different elements,
		/// returns null.
		/// </summary>
		/// <typeparam name="T">The type of elements in the sequence</typeparam>
		/// <param name="source">The source sequence</param>
		public static T Common<T>(this IEnumerable<T> source)
			where T : class
		{
			if (source.Contains(null))
				return null;

			var groups = source.GroupBy(x => x);
			if (groups.Count() != 1)
				return null;

			return groups.Single().Key;
		}

		/// <summary>
		/// Projects each element of a sequence into a new form and returns the common value produced.
		/// If any element projects to <see langword="null"/> or elements project to different values,
		/// an exception is thrown instead.
		/// </summary>
		/// <typeparam name="TSource">The type of the sequence</typeparam>
		/// <typeparam name="TResult">The type of the result</typeparam>
		/// <param name="source">The source sequence</param>
		/// <param name="selector">Used to project each element to a value</param>
		/// <param name="description">A description of the set of values, for use in the exception message</param>
		/// <returns>The common value produced</returns>
		/// <exception cref="Exception">Thrown if any element projects to <see langword="null"/> or elements project to different values</exception>
		public static TResult Common<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult?> selector, string description)
			where TResult : struct
		{
			return source
				.Select(selector)
				.Common(description);
		}

		/// <summary>
		/// Returns the common value among the elements of <paramref name="source"/>.
		/// If <paramref name="source"/> contains <see langword="null"/> or two different elements,
		/// an exception is thrown instead.
		/// </summary>
		/// <typeparam name="T">The type of elements in the sequence</typeparam>
		/// <param name="source">The source sequence</param>
		/// <param name="description">A description of the set of elements, for use in the exception message</param>
		/// <returns>The common value produced</returns>
		/// <exception cref="Exception">Thrown if any element is <see langword="null"/> or there are two different elements</exception>
		public static T Common<T>(this IEnumerable<T?> source, string description)
			where T : struct
		{
			if (source.Contains(null))
				throw new Exception($"Found null among {description}");

			var groups = source.GroupBy(x => x);
			if (groups.Count() > 1)
				throw new Exception($"Found different values among {description}");

			return groups.Single().Key.Value;
		}

		/// <summary>
		/// Projects each element of a sequence into a new form and returns the common value produced.
		/// If any element projects to <see langword="null"/> or elements project to different values,
		/// returns null.
		/// </summary>
		/// <typeparam name="TSource">The type of the sequence</typeparam>
		/// <typeparam name="TResult">The type of the result</typeparam>
		/// <param name="source">The source sequence</param>
		/// <param name="selector">Used to project each element to a value</param>
		/// <returns>The common value produced, or null of there are different values</returns>
		public static TResult? Common<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult?> selector)
			where TResult : struct
		{
			return source
				.Select(selector)
				.Common();
		}

		/// <summary>
		/// Returns the common value among the elements of <paramref name="source"/>.
		/// If <paramref name="source"/> contains <see langword="null"/> or two different elements,
		/// returns null.
		/// </summary>
		/// <typeparam name="T">The type of elements in the sequence</typeparam>
		/// <param name="source">The source sequence</param>
		public static T? Common<T>(this IEnumerable<T?> source)
			where T : struct
		{
			if (source.Contains(null))
				return null;

			var groups = source.GroupBy(x => x);
			if (groups.Count() != 1)
				return null;

			return groups.Single().Key;
		}

		/// <summary>
		/// Filters a sequence based on a predicate. Elements that satsify the predicate, are eliminated.
		/// Also, a specified action is executed for each elements that is eliminated.
		/// </summary>
		/// <typeparam name="T">The type of elements in the sequence</typeparam>
		/// <param name="source">The source sequence</param>
		/// <param name="predicate">The predicate satisfied by elements to be eliminated</param>
		/// <param name="action">An action executed on each eliminated element</param>
		/// <returns></returns>
		public static IEnumerable<T> Eliminate<T>(this IEnumerable<T> source, Func<T, bool> predicate, Action<T> action)
			where T : class
		{
			foreach (var item in source)
			{
				if (predicate(item))
					// Eliminate
					action.Invoke(item);
				else
					// Keep
					yield return item;
			}
		}

		/// <summary>
		/// As <see cref="Enumerable.Select{TSource, TResult}(IEnumerable{TSource}, Func{TSource, TResult})"/>, 
		/// but any selected value that is null, is ignored
		/// </summary>
		public static IEnumerable<TResult> SelectUnlessNull<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> selector)
			where TResult : class
		{
			return Enumerable.Select(source, selector).Except((TResult)null);
		}

		/// <summary>
		/// As <see cref="Enumerable.SelectMany{TSource, TResult}(IEnumerable{TSource}, Func{TSource, IEnumerable{TResult}})"/>,
		/// but when <paramref name="selector"/> yields a sequence that is null, it is ignored, as is any selected value that is null.
		/// </summary>
		public static IEnumerable<TResult> SelectManyUnlessNull<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, IEnumerable<TResult>> selector)
			where TResult : class
		{
			return Enumerable.SelectMany(source.Select(selector).Except((IEnumerable<TResult>)null), x => x).Except((TResult)null);
		}
	}
}

namespace Sintef.Scoop.Linq.SelectUnlessNull
{
	/// <summary>
	/// Versions of LINQ extension methods that ignore null
	/// </summary>
	internal static class EnumerableExtensions
	{
		/// <summary>
		/// As <see cref="Enumerable.Select{TSource, TResult}(IEnumerable{TSource}, Func{TSource, TResult})"/>, 
		/// but any selected value that is null, is ignored
		/// </summary>
		public static IEnumerable<TResult> Select<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> selector)
			where TResult : class
		{
			return source.SelectUnlessNull(selector);
		}

		/// <summary>
		/// As <see cref="Enumerable.SelectMany{TSource, TResult}(IEnumerable{TSource}, Func{TSource, IEnumerable{TResult}})"/>, 
		/// but any projected sequence that is null, is ignored, as is any selected value that is null.
		/// </summary>
		public static IEnumerable<TResult> SelectMany<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, IEnumerable<TResult>> selector)
			where TResult : class
		{
			return source.SelectManyUnlessNull(selector);
		}
	}
}
