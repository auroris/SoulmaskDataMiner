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
	/// Helper for writing SQL table insert statements
	/// </summary>
	internal interface ISqlWriter
	{
		/// <summary>
		/// Start inserting into a table
		/// </summary>
		/// <param name="tableName">The name of the table</param>
		void WriteStartTable(string tableName);

		/// <summary>
		/// Finishes inserting into a table
		/// </summary>
		void WriteEndTable();

		/// <summary>
		/// Write a row to the current table.
		/// </summary>
		/// <param name="data">The row data</param>
		/// <remarks>
		/// The row should be preformatted valid SQL consisting of a comma-delimited
		/// series of values that will be inserted into the table, and nothing else.
		/// </remarks>
		void WriteRow(string data);

		/// <summary>
		/// Writes an empty line
		/// </summary>
		void WriteEmptyLine();
	}
}
