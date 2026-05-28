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

namespace SoulmaskDataMiner.IO
{
	/// <summary>
	/// Utility to assist with sanitizing strings for serialization
	/// </summary>
	internal static class SerializationUtil
	{
		/// <summary>
		/// Formats a string so that it is safe to insert into a CSV cell
		/// </summary>
		public static string? CsvStr(string? value)
		{
			if (value is null || value.Equals("None")) return null;
			return $"\"{value.Replace("\"", "\"\"")}\"";
		}

		/// <summary>
		/// Formats a string so that it is safe to insert into a SQL database cell
		/// </summary>
		public static string DbStr(string? value, bool treatNullAsEmpty = false)
		{
			if (value is null || value.Equals("None")) return treatNullAsEmpty ? "''" : "null";
			return $"'{value.Replace("\'", "\'\'")}'";
		}

		/// <summary>
		/// Formats a bool to be inserted into a SQL database cell
		/// </summary>
		public static string DbBool(bool value)
		{
			return value ? "true" : "false";
		}

		/// <summary>
		/// Formats a nullable value to be inserted into a SQL database cell
		/// </summary>
		public static string DbVal<T>(T? value) where T : struct
		{
			if (!value.HasValue) return "null";

			if (value.Value is bool b)
			{
				return DbBool(b);
			}

			return value.Value.ToString() ?? "null";
		}
	}
}
