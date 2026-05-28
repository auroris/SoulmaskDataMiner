// Copyright 2026 Crystal Ferrai
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace SoulmaskDataMiner.Data
{
	/// <summary>
	/// Represents a value and an associated weight
	/// </summary>
	/// <typeparam name="T">The type of the value</typeparam>
	internal class WeightedValue<T> where T : notnull
	{
		/// <summary>
		/// The value
		/// </summary>
		public T Value { get; }

		/// <summary>
		/// The weight of the value proportional to other weighted values
		/// </summary>
		public double Weight { get; private set; }

		public WeightedValue(T value, double weight)
		{
			Value = value;
			Weight = weight;
		}

		public override string ToString()
		{
			return $"{Value}: {Weight}";
		}

		/// <summary>
		/// Combines weights with matching values and calculates relative weight values.
		/// </summary>
		/// <param name="collection">The collection to reduce</param>
		/// <returns>
		/// A new collection where each value occurs only once, and the weight is a percentage of
		/// the total weight of all values.
		/// </returns>
		/// <remarks>
		/// Values will only be combined if their GetHashCode and Equals functions both indicate
		/// that they are the same value. This will work by default for primitive types. Complex
		/// types will need to implement these functions to ensure desired results.
		/// </remarks>
		public static IEnumerable<WeightedValue<T>> Reduce(IEnumerable<WeightedValue<T>> collection)
		{
			Dictionary<T, WeightedValue<T>> map = new();
			double totalWeight = 0.0;
			foreach (WeightedValue<T> item in collection)
			{
				if (item.Weight == 0.0) continue;

				totalWeight += item.Weight;

				WeightedValue<T>? current;
				if (!map.TryGetValue(item.Value, out current))
				{
					current = new(item.Value, 0.0);
					map.Add(item.Value, current);
				}
				current.Weight += item.Weight;
			}

			foreach (WeightedValue<T> current in map.Values)
			{
				current.Weight = current.Weight / totalWeight;
			}

			return map.Values;
		}
	}
}
