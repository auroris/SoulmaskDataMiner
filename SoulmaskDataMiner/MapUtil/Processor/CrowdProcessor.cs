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
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using SoulmaskDataMiner.GameData;

namespace SoulmaskDataMiner.MapUtil.Processor
{
	/// <summary>
	/// Map point of interest processor for crowd NPC locations
	/// </summary>
	internal class CrowdProcessor : ProcessorBase
	{
		public CrowdProcessor(MapData mapData)
			: base(mapData)
		{
		}

		public void Process(MapPoiDatabase poiDatabase, IReadOnlyList<ObjectWithDefaults> fireflyObjects, Logger logger)
		{
			ProcessFireflies(poiDatabase, fireflyObjects, logger);
		}

		private void ProcessFireflies(MapPoiDatabase poiDatabase, IReadOnlyList<ObjectWithDefaults> fireflyObjects, Logger logger)
		{
			logger.Information($"Processing {fireflyObjects.Count} firefly spawners...");

			foreach (ObjectWithDefaults fireflyObject in fireflyObjects)
			{
				FObjectExport export = fireflyObject.Export;
				UObject obj = export.ExportObject.Value;

				float appearTime = -1.0f, disappearTime = -1.0f, refreshTime = -1.0f;
				int maxCount = -1;
				FPackageIndex? lootItem = null;
				USceneComponent? rootComponent = null;
				FVector? locationOffset = null;
				void searchProperties(UObject searchObj)
				{
					foreach (FPropertyTag property in searchObj.Properties)
					{
						switch (property.Name.Text)
						{
							case "AppearTime":
								if (appearTime < 0.0f)
								{
									appearTime = property.Tag!.GetValue<float>();
								}
								break;
							case "DisappearTime":
								if (disappearTime < 0.0f)
								{
									disappearTime = property.Tag!.GetValue<float>();
								}
								break;
							case "RefreshTime":
								if (refreshTime < 0.0f)
								{
									refreshTime = property.Tag!.GetValue<float>();
								}
								break;
							case "RewardDaoJuMaxNums":
								if (maxCount < 0)
								{
									maxCount = property.Tag!.GetValue<int>();
								}
								break;
							case "InteractRewardDaoJuClass":
								if (lootItem is null)
								{
									lootItem = property.Tag?.GetValue<FPackageIndex>();
								}
								break;
							case "RootComponent":
								if (rootComponent is null)
								{
									rootComponent = property.Tag?.GetValue<FPackageIndex>()?.Load<USceneComponent>();
								}
								break;
							case "SpawnBoxLocationOffset":
								if (!locationOffset.HasValue)
								{
									locationOffset = property.Tag!.GetValue<FVector>();
								}
								break;
						}
					}
				}

				searchProperties(obj);
				if (obj.Class?.Load() is UBlueprintGeneratedClass objClass)
				{
					GameClassHierarchy.SearchInheritance(objClass, (current) =>
					{
						UObject? currentObj = current.ClassDefaultObject.Load();
						if (currentObj is null) return true;

						searchProperties(currentObj);
						return appearTime >= 0.0f && disappearTime >= 0.0f && refreshTime >= 0.0f && maxCount >= 0 && lootItem is not null && rootComponent is not null && locationOffset.HasValue;
					});
				}

				if (appearTime < 0.0f || disappearTime < 0.0f || refreshTime < 0.0f || maxCount < 0 || lootItem is null || rootComponent is null || !locationOffset.HasValue)
				{
					logger.Warning($"[{export.ObjectName}] Firefly spawner missing properties");
					continue;
				}

				FPropertyTag? locationProperty = rootComponent.Properties.FirstOrDefault(p => p.Name.Text.Equals("RelativeLocation"));
				if (locationProperty is null)
				{
					logger.Warning($"[{export.ObjectName}] Failed to find location for firefly spawner");
					continue;
				}

				string appearTimeText = TimeSpan.FromSeconds(appearTime).ToString(@"hh\:mm");
				string disappearTimeText = TimeSpan.FromSeconds(disappearTime).ToString(@"hh\:mm");

				FVector location = locationProperty.Tag!.GetValue<FVector>() + locationOffset.Value;
				MapPoi poi = new()
				{
					GroupIndex = SpawnLayerGroup.Npc,
					Type = "Fireflies",
					Title = "Firefly Spawner",
					Description = $"Appears from {appearTimeText} until {disappearTimeText}",
					Location = location,
					MapLocation = WorldToMap(location),
					SpawnCountMax = maxCount,
					SpawnInterval = refreshTime,
					Icon = poiDatabase.StaticData.SpawnLayerMap[NpcCategory.Firefly].Icon,
					LootItem = lootItem.Name
				};

				poiDatabase.Spawners.Add(poi);
			}
		}
	}
}
