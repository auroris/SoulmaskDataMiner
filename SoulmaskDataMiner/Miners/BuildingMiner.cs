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

using SoulmaskDataMiner.IO;

namespace SoulmaskDataMiner.Miners
{
	/// <summary>
	/// Gathers information about building (JianZhu) classes, providing a
	/// class-name to display-name lookup for downstream tooling.
	/// </summary>
	internal class BuildingMiner : SubclassMinerBase
	{
		public override string Name => "Building";

		protected override string NameProperty => "JianZhuDisplayName";

		protected override string? IconProperty => "JianZhuIcon";

		private const string BaseClass_Building = "HJianZhuBase";

		public override bool Run(IProviderManager providerManager, Config config, Logger logger, ISqlWriter sqlWriter)
		{
			List<ObjectInfo> buildings = new();
			foreach (ObjectInfo info in FindObjects(BaseClass_Building.AsEnumerable()))
			{
				if (info.Name is null)
				{
					logger.Debug($"Skipping building {info.ClassName} because it has no display name");
					continue;
				}
				buildings.Add(info);
			}

			if (buildings.Count == 0)
			{
				logger.Warning("No buildings found.");
				return false;
			}

			logger.Information($"Found {buildings.Count} buildings");

			WriteCsv(buildings, config, logger);
			WriteSql(buildings, sqlWriter, logger);
			WriteTextures(buildings, config, logger);

			return true;
		}

		private void WriteCsv(IEnumerable<ObjectInfo> buildings, Config config, Logger logger)
		{
			string outPath = Path.Combine(config.OutputDirectory, Name, $"{Name}.csv");
			using FileStream outFile = IOUtil.CreateFile(outPath, logger);
			using StreamWriter writer = new(outFile);

			writer.WriteLine("class,name,path,icon");
			foreach (ObjectInfo b in buildings)
			{
				writer.WriteLine($"{CsvStr(b.ClassName)},{CsvStr(b.Name)},{CsvStr(b.FullPath)},{b.Icon?.Name}");
			}
		}

		private void WriteSql(IEnumerable<ObjectInfo> buildings, ISqlWriter sqlWriter, Logger logger)
		{
			// Schema
			// create table `building` (
			//   `class` varchar(255) not null,
			//   `name` varchar(127) not null,
			//   `path` varchar(511) not null,
			//   `icon` varchar(255)
			// )

			sqlWriter.WriteStartTable("building");
			foreach (ObjectInfo b in buildings)
			{
				sqlWriter.WriteRow($"{DbStr(b.ClassName)}, {DbStr(b.Name)}, {DbStr(b.FullPath)}, {DbStr(b.Icon?.Name)}");
			}
			sqlWriter.WriteEndTable();
		}

		private void WriteTextures(IEnumerable<ObjectInfo> buildings, Config config, Logger logger)
		{
			string outDir = Path.Combine(config.OutputDirectory, Name, "icons");
			foreach (ObjectInfo b in buildings)
			{
				if (b.Icon is null) continue;
				TextureExporter.ExportTexture(b.Icon, false, logger, outDir);
			}
		}
	}
}
