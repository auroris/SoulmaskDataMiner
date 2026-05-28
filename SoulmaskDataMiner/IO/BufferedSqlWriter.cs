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
	/// Thread-safe implementation of ISqlWriter that records calls for sequential playback
	/// </summary>
	internal class BufferedSqlWriter : ISqlWriter
	{
		private readonly List<Action<ISqlWriter>> mActions = new();

		public void WriteStartTable(string tableName)
		{
			lock (mActions)
			{
				mActions.Add(w => w.WriteStartTable(tableName));
			}
		}

		public void WriteEndTable()
		{
			lock (mActions)
			{
				mActions.Add(w => w.WriteEndTable());
			}
		}

		public void WriteRow(string data)
		{
			lock (mActions)
			{
				mActions.Add(w => w.WriteRow(data));
			}
		}

		public void WriteEmptyLine()
		{
			lock (mActions)
			{
				mActions.Add(w => w.WriteEmptyLine());
			}
		}

		/// <summary>
		/// Replays all recorded SQL statements sequentially to the destination writer
		/// </summary>
		public void Playback(ISqlWriter destination)
		{
			lock (mActions)
			{
				foreach (var action in mActions)
				{
					action(destination);
				}
			}
		}
	}
}
