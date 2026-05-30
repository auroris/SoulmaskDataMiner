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
using SoulmaskDataMiner.IO;
using System.Reflection;
using System.Text;

namespace SoulmaskDataMiner.Miners
{
	/// <summary>
	/// Base class for all data miners
	/// </summary>
	internal abstract class MinerBase : IDataMiner
	{
		public virtual string Name => GetType().GetCustomAttribute<MinerNameAttribute>()?.Name
			?? throw new InvalidOperationException($"Miner type {GetType().Name} is missing a [MinerName] attribute.");

		public abstract bool Run(IProviderManager providerManager, Config config, Logger logger, ISqlWriter sqlWriter);

		/// <summary>
		/// Formats a string so that it is safe to insert into a CSV cell
		/// </summary>
		protected static string? CsvStr(string? value)
		{
			return SerializationUtil.CsvStr(value);
		}

		/// <summary>
		/// Formats a string so that it is safe to insert into a SQL database cell
		/// </summary>
		protected static string DbStr(string? value, bool treatNullAsEmpty = false)
		{
			return SerializationUtil.DbStr(value, treatNullAsEmpty);
		}

		/// <summary>
		/// Formats a bool to be inserted into a SQL database cell
		/// </summary>
		protected static string DbBool(bool value)
		{
			return SerializationUtil.DbBool(value);
		}

		/// <summary>
		/// Formats a nullable value to be inserted into a SQL database cell
		/// </summary>
		protected static string DbVal<T>(Nullable<T> value) where T : struct
		{
			return SerializationUtil.DbVal(value);
		}

		/// <summary>
		/// Writes one logical miner output table — CSV, SQL, and icons — from
		/// a single <see cref="MinerTable{T}"/> declaration. CSV is always written;
		/// SQL and icons are written iff their respective fields are set on the spec.
		/// </summary>
		/// <remarks>
		/// Rows are materialized once so the CSV, SQL, and icon passes all see the
		/// same sequence (and don't pay for repeated enumeration of a lazy source).
		/// </remarks>
		protected void WriteTable<T>(
			IEnumerable<T> rows,
			MinerTable<T> table,
			Config config,
			Logger logger,
			ISqlWriter sqlWriter)
		{
			List<T> materialized = rows as List<T> ?? rows.ToList();

			WriteCsvTable(materialized, table.Columns, config, logger, table.CsvFileName);
			if (table.SqlTableName is not null)
			{
				WriteSqlTable(materialized, table.Columns, table.SqlTableName, sqlWriter);
			}
			if (table.IconSelector is not null)
			{
				WriteIcons(materialized, table.IconSelector, config, logger, table.IconSubdir, table.DeduplicateIcons);
			}
		}

		/// <summary>
		/// Writes <paramref name="rows"/> as a CSV file under the miner's output
		/// directory, with the column header derived from <paramref name="columns"/>.
		/// All CSV output uses UTF-8 with BOM.
		/// </summary>
		/// <param name="fileName">
		/// CSV file name (without directory). Defaults to <c>{Name}.csv</c>.
		/// </param>
		protected void WriteCsvTable<T>(
			IEnumerable<T> rows,
			IReadOnlyList<TableColumn<T>> columns,
			Config config,
			Logger logger,
			string? fileName = null)
		{
			string file = fileName ?? $"{Name}.csv";
			string outPath = Path.Combine(config.OutputDirectory, Name, file);
			using FileStream stream = IOUtil.CreateFile(outPath, logger);
			using StreamWriter writer = new(stream, Encoding.UTF8);

			writer.WriteLine(string.Join(",", columns.Select(c => c.Name)));
			foreach (T row in rows)
			{
				writer.WriteLine(string.Join(",", columns.Select(c => c.CsvOf(row))));
			}
		}

		/// <summary>
		/// Writes <paramref name="rows"/> as a SQL insert table named
		/// <paramref name="tableName"/>, with cells formatted by
		/// <paramref name="columns"/>.
		/// </summary>
		protected void WriteSqlTable<T>(
			IEnumerable<T> rows,
			IReadOnlyList<TableColumn<T>> columns,
			string tableName,
			ISqlWriter sqlWriter)
		{
			sqlWriter.WriteStartTable(tableName);
			foreach (T row in rows)
			{
				sqlWriter.WriteRow(string.Join(", ", columns.Select(c => c.SqlOf(row))));
			}
			sqlWriter.WriteEndTable();
		}

		/// <summary>
		/// Exports the icon texture of each row to the miner's icon directory.
		/// Rows whose icon is null are skipped.
		/// </summary>
		/// <param name="deduplicate">
		/// When true, exports each unique texture name only once. Use this when
		/// many rows share the same icon asset (e.g. NaturalGift, WenShen).
		/// </param>
		/// <param name="subdir">
		/// Subdirectory under the miner's output directory. Defaults to "icons".
		/// </param>
		protected void WriteIcons<T>(
			IEnumerable<T> rows,
			Func<T, UTexture2D?> iconSelector,
			Config config,
			Logger logger,
			string subdir = "icons",
			bool deduplicate = false)
		{
			string outDir = Path.Combine(config.OutputDirectory, Name, subdir);
			HashSet<string>? seen = deduplicate ? new HashSet<string>() : null;
			foreach (T row in rows)
			{
				UTexture2D? icon = iconSelector(row);
				if (icon is null) continue;
				if (seen is not null && !seen.Add(icon.Name)) continue;
				TextureExporter.ExportTexture(config, icon, false, logger, outDir);
			}
		}
	}
}
