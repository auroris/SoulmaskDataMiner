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

using CUE4Parse.UE4.Objects.UObject;

namespace SoulmaskDataMiner.Data
{
	/// <summary>
	/// Wrapper around a <see cref="FPackageIndex"/> that can be used in hash sets and as dictionary keys
	/// </summary>
	internal class EquatablePackageIndex : IEquatable<EquatablePackageIndex>, IEquatable<FPackageIndex>
	{
		public FPackageIndex Value { get; }

		public EquatablePackageIndex(FPackageIndex value)
		{
			Value = value;
		}

		public override int GetHashCode()
		{
			return (Value.Name ?? string.Empty).GetHashCode();
		}

		public override bool Equals(object? obj)
		{
			if (obj is EquatablePackageIndex other)
			{
				return Equals(other);
			}
			else if (obj is FPackageIndex pi)
			{
				return Equals(pi);
			}
			return false;
		}

		public bool Equals(EquatablePackageIndex? other)
		{
			return other is not null && string.Equals(Value.Name, other.Value.Name);
		}

		public bool Equals(FPackageIndex? other)
		{
			return other is not null && string.Equals(Value.Name, other.Name);
		}

		public override string ToString()
		{
			return Value.ToString();
		}

		public static implicit operator EquatablePackageIndex(FPackageIndex value)
		{
			return new EquatablePackageIndex(value);
		}

		public static implicit operator FPackageIndex(EquatablePackageIndex value)
		{
			return value.Value;
		}

		public static bool operator ==(EquatablePackageIndex? a, EquatablePackageIndex? b)
		{
			if (ReferenceEquals(a, b)) return true;
			if (a is null || b is null) return false;
			return a.Equals(b);
		}

		public static bool operator !=(EquatablePackageIndex? a, EquatablePackageIndex? b)
		{
			return !(a == b);
		}

		public static bool operator ==(EquatablePackageIndex? a, FPackageIndex b)
		{
			if (a is null) return false;
			return a.Equals(b);
		}

		public static bool operator !=(EquatablePackageIndex? a, FPackageIndex b)
		{
			return !(a == b);
		}

		public static bool operator ==(FPackageIndex a, EquatablePackageIndex? b)
		{
			if (b is null) return false;
			return b.Equals(a);
		}

		public static bool operator !=(FPackageIndex a, EquatablePackageIndex? b)
		{
			return !(a == b);
		}
	}
}
