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
	[MinerName("Building"), RequiredBaseClasses("HJianZhuBase")]
	internal class BuildingMiner : SubclassMinerBase
	{
		protected override string NameProperty => "JianZhuDisplayName";

		protected override string? IconProperty => "JianZhuIcon";

		private const string BaseClass_Building = "HJianZhuBase";

		// Schema
		// create table `building` (
		//   `class` varchar(255) not null,
		//   `name` varchar(127) not null,
		//   `path` varchar(511) not null,
		//   `icon` varchar(255)
		// )
		private static readonly MinerTable<ObjectInfo> sTable = new(
			csvFileName: "Building.csv",
			sqlTableName: "building",
			columns:
			[
				TableColumn.Str<ObjectInfo>("class", b => b.ClassName),
				TableColumn.Str<ObjectInfo>("name", b => b.Name),
				TableColumn.Str<ObjectInfo>("path", b => b.FullPath),
				TableColumn.Str<ObjectInfo>("icon", b => b.Icon?.Name),
			],
			iconSelector: b => b.Icon);

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
			WriteTable(buildings, sTable, config, logger, sqlWriter);
			return true;
		}
	}
}
