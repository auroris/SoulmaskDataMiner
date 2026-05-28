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

using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Component;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.UObject;
using SoulmaskDataMiner.GameData;

namespace SoulmaskDataMiner.MapUtil.Processor
{
	/// <summary>
	/// Map point of interest processor for mine platform locations
	/// </summary>
	internal class MinePlatformProcessor : ProcessorBase
	{
		public MinePlatformProcessor(MapData mapData)
			: base(mapData)
		{
		}

		public void Process(MapPoiDatabase poiDatabase, IReadOnlyList<FObjectExport> minePlatformObjects, Logger logger)
		{
			logger.Information($"Processing {minePlatformObjects.Count} mining platforms...");

			foreach (FObjectExport minePlatformObject in minePlatformObjects)
			{
				string? lootId = null;
				float interval = 0.0f;
				USceneComponent? rootComponent = null;

				void searchObj(UObject obj)
				{
					foreach (FPropertyTag property in obj.Properties)
					{
						switch (property.Name.Text)
						{
							case "ChanChuDaoJuBaoName":
								if (lootId is null) lootId = property.Tag?.GetValue<FName>().Text;
								break;
							case "ChanChuJianGeTime":
								if (interval == 0.0f) interval = property.Tag!.GetValue<float>();
								break;
							case "RootComponent":
								if (rootComponent is null) rootComponent = property.Tag?.GetValue<FPackageIndex>()?.Load<USceneComponent>();
								break;
						}
					}
				}

				UObject worldObj = minePlatformObject.ExportObject.Value;
				searchObj(worldObj);

				GameClassHierarchy.SearchInheritance((UClass)worldObj.Class!.Load()!, (current) =>
				{
					UObject? curObj = current.ClassDefaultObject.Load();
					if (curObj is null) return false;

					searchObj(curObj);

					return lootId is not null && interval != 0.0f && rootComponent is not null;
				});

				if (lootId is null || interval == 0.0f || rootComponent is null)
				{
					logger.Warning("Mining platform properties not found");
					continue;
				}

				FPropertyTag? locationProperty = rootComponent?.Properties.FirstOrDefault(p => p.Name.Text.Equals("RelativeLocation"));
				if (locationProperty is null)
				{
					logger.Warning("Failed to locate mining platform");
					continue;
				}

				FVector location = locationProperty.Tag!.GetValue<FVector>();

				MapPoi poi = new()
				{
					GroupIndex = SpawnLayerGroup.PointOfInterest,
					Type = "Mining Platform",
					Title = "Mining Platform",
					Description = $"Interval: {interval} seconds",
					LootId = lootId,
					Location = location,
					MapLocation = WorldToMap(location),
					Icon = poiDatabase.StaticData.MinePlatformIcon
				};

				poiDatabase.MinePlatforms.Add(poi);
			}
		}
	}
}
