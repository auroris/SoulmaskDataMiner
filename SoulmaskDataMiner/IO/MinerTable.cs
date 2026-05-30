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

using CUE4Parse.UE4.Assets.Exports.Texture;

namespace SoulmaskDataMiner.IO
{
	/// <summary>
	/// One logical output table from a miner. Bundles the CSV file name, the optional
	/// SQL table name, the shared column list, and the optional icon selector so a
	/// single declaration drives all output codepaths for one table.
	/// </summary>
	/// <remarks>
	/// CSV and SQL share <see cref="Columns"/> — they always have the same shape.
	/// Set <see cref="SqlTableName"/> to null for CSV-only tables (e.g. LogMiner).
	/// Set <see cref="IconSelector"/> to null when the table has no associated icons.
	/// </remarks>
	/// <typeparam name="T">The row type</typeparam>
	internal sealed class MinerTable<T>
	{
		/// <summary>CSV file name (without directory). e.g. "Attribute.csv".</summary>
		public string CsvFileName { get; }

		/// <summary>SQL table name, or null to skip SQL emission.</summary>
		public string? SqlTableName { get; }

		/// <summary>Column definitions used for both the CSV header/rows and SQL row values.</summary>
		public IReadOnlyList<TableColumn<T>> Columns { get; }

		/// <summary>Selector for an icon to export per row, or null to skip icon export.</summary>
		public Func<T, UTexture2D?>? IconSelector { get; }

		/// <summary>Subdirectory under the miner's output directory for icons. Defaults to "icons".</summary>
		public string IconSubdir { get; init; } = "icons";

		/// <summary>If true, each unique texture name is exported once even if many rows reference it.</summary>
		public bool DeduplicateIcons { get; init; }

		public MinerTable(
			string csvFileName,
			string? sqlTableName,
			IReadOnlyList<TableColumn<T>> columns,
			Func<T, UTexture2D?>? iconSelector = null)
		{
			CsvFileName = csvFileName;
			SqlTableName = sqlTableName;
			Columns = columns;
			IconSelector = iconSelector;
		}
	}
}
