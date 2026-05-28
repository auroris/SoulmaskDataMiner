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

using System.Runtime.CompilerServices;

namespace SoulmaskDataMiner.IO
{
	/// <summary>
	/// Helper for writing SQL table insert statements
	/// </summary>
	internal class SqlWriter : ISqlWriter
	{
		private readonly TextWriter mWriter;

		private WriterState mState;

		private string? mTableName;
		private int mRowCounter;

		public SqlWriter(TextWriter writer)
		{
			mWriter = writer;
			mState = WriterState.None;
		}

		/// <summary>
		/// Writes the start of the update file
		/// </summary>
		public void WriteStartFile()
		{
			EnsureState(WriterState.None);

			mWriter.WriteLine("set names utf8mb4;");
			mWriter.WriteLine("start transaction;");
			mWriter.WriteLine();

			mState = WriterState.InFile;
		}

		/// <summary>
		/// Writes the end of the update file
		/// </summary>
		public void WriteEndFile()
		{
			EnsureState(WriterState.InFile);

			mWriter.WriteLine("commit;");

			mState = WriterState.None;
		}

		/// <summary>
		/// Starts a new section
		/// </summary>
		/// <param name="sectionName">The name of the section to start</param>
		public void WriteStartSection(string sectionName)
		{
			EnsureState(WriterState.InFile);
			if (sectionName is null) throw new ArgumentNullException(nameof(sectionName));

			mWriter.WriteLine("/* ========================================================================== */");
			mWriter.WriteLine($"-- {sectionName}");
			mWriter.WriteLine();

			mState = WriterState.InSection;
		}

		/// <summary>
		/// Ends the current section
		/// </summary>
		public void WriteEndSection()
		{
			EnsureState(WriterState.InSection);

			mWriter.WriteLine();

			mState = WriterState.InFile;
		}

		/// <summary>
		/// Start inserting into a table
		/// </summary>
		/// <param name="tableName">The name of the table</param>
		public void WriteStartTable(string tableName)
		{
			EnsureState(WriterState.InSection);
			if (tableName is null) throw new ArgumentNullException(nameof(tableName));

			mWriter.WriteLine($"truncate table `{tableName}`;");

			mState = WriterState.InTable;
			mTableName = tableName;
			mRowCounter = 0;
		}

		/// <summary>
		/// Finishes inserting into a table
		/// </summary>
		public void WriteEndTable()
		{
			EnsureState(WriterState.InTable);

			if (mRowCounter > 0)
			{
				mWriter.WriteLine(";");
				mRowCounter = 0;
			}
			mTableName = null;

			mState = WriterState.InSection;
		}

		/// <summary>
		/// Write a row to the current table.
		/// </summary>
		/// <param name="data">The row data</param>
		/// <remarks>
		/// The row should be preformatted valid SQL consisting of a comma-delimited
		/// series of values that will be inserted into the table, and nothing else.
		/// </remarks>
		public void WriteRow(string data)
		{
			EnsureState(WriterState.InTable);
			if (data is null) throw new ArgumentNullException(nameof(data));

			if (mRowCounter == 999)
			{
				mWriter.WriteLine(";");
				mRowCounter = 0;
			}

			if (mRowCounter == 0)
			{
				mWriter.WriteLine($"insert into `{mTableName}` values ");
			}
			else
			{
				mWriter.WriteLine(",");
			}

			++mRowCounter;

			mWriter.Write($"({data})");
		}

		/// <summary>
		/// Writes an empty line
		/// </summary>
		public void WriteEmptyLine()
		{
			mWriter.WriteLine();
		}

		private void EnsureState(WriterState state, [CallerMemberName] string? functionName = null)
		{
			if (mState != state) throw new InvalidOperationException($"[{nameof(SqlWriter)}] {functionName}: Writer in incorrect state '{mState}'. Expected state '{state}'.");
		}

		private enum WriterState
		{
			None,
			InFile,
			InSection,
			InTable
		}
	}
}
