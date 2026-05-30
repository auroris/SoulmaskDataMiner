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
	/// One column of a miner output table. Carries the column name plus a per-row
	/// formatter for each output format (CSV cell and SQL cell), so a single column
	/// definition drives both writers.
	/// </summary>
	/// <typeparam name="T">The row type</typeparam>
	internal sealed class TableColumn<T>
	{
		public string Name { get; }

		/// <summary>
		/// Produces the CSV cell text for <paramref name="row"/>. Returns null to
		/// emit an empty cell.
		/// </summary>
		public Func<T, string?> CsvOf { get; }

		/// <summary>
		/// Produces the SQL value literal for <paramref name="row"/>. Must return
		/// valid SQL (e.g. quoted string, numeric literal, or "null").
		/// </summary>
		public Func<T, string> SqlOf { get; }

		public TableColumn(string name, Func<T, string?> csvOf, Func<T, string> sqlOf)
		{
			Name = name;
			CsvOf = csvOf;
			SqlOf = sqlOf;
		}
	}

	/// <summary>
	/// Factory helpers for <see cref="TableColumn{T}"/> covering the common cell
	/// kinds (string, int, float, bool) so each miner declares its columns once.
	/// </summary>
	internal static class TableColumn
	{
		/// <summary>
		/// String column. CSV cell is CsvStr-escaped (quoted, null/"None" -> empty).
		/// SQL cell is DbStr-escaped (quoted, null/"None" -> "null" or "''").
		/// </summary>
		public static TableColumn<T> Str<T>(string name, Func<T, string?> get, bool treatNullAsEmpty = false)
		{
			return new TableColumn<T>(
				name,
				r => SerializationUtil.CsvStr(get(r)),
				r => SerializationUtil.DbStr(get(r), treatNullAsEmpty));
		}

		/// <summary>
		/// Integer column.
		/// </summary>
		public static TableColumn<T> Int<T>(string name, Func<T, int> get)
		{
			return new TableColumn<T>(
				name,
				r => get(r).ToString(),
				r => get(r).ToString());
		}

		/// <summary>
		/// Nullable integer column. CSV emits the value or an empty cell;
		/// SQL emits the value or the literal "null".
		/// </summary>
		public static TableColumn<T> NullInt<T>(string name, Func<T, int?> get)
		{
			return new TableColumn<T>(
				name,
				r => get(r)?.ToString(),
				r => SerializationUtil.DbVal(get(r)));
		}

		/// <summary>
		/// Float column. Both CSV and SQL use the same format string (default "G").
		/// </summary>
		public static TableColumn<T> Float<T>(string name, Func<T, float> get, string? format = null)
		{
			return new TableColumn<T>(
				name,
				r => format is null ? get(r).ToString() : get(r).ToString(format),
				r => format is null ? get(r).ToString() : get(r).ToString(format));
		}

		/// <summary>
		/// Boolean column. CSV emits lowercase "true"/"false"; SQL uses DbBool.
		/// </summary>
		public static TableColumn<T> Bool<T>(string name, Func<T, bool> get)
		{
			return new TableColumn<T>(
				name,
				r => get(r).ToString().ToLowerInvariant(),
				r => SerializationUtil.DbBool(get(r)));
		}

	}
}
