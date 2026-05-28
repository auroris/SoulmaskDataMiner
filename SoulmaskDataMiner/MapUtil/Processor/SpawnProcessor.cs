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
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using SoulmaskDataMiner.Data;
using SoulmaskDataMiner.GameData;
using System.Text;

namespace SoulmaskDataMiner.MapUtil.Processor
{
	/// <summary>
	/// Map point of interest processor for spawner locations
	/// </summary>
	internal class SpawnProcessor : ProcessorBase
	{
		public SpawnProcessor(MapData mapData)
			: base(mapData)
		{
		}

		public void Process(
			MapPoiDatabase poiDatabase,
			IReadOnlyList<SpawnerObject> spawnerObjects,
			IReadOnlyList<FObjectExport> barracksObjects,
			EventMap eventsMap,
			Logger logger,
			ISet<NpcData> allBabies)
		{
			// Process barracks

			HashSet<string> barracksSpawnerNames = new();
			foreach (FObjectExport barracksObject in barracksObjects)
			{
				foreach (FPropertyTag property in barracksObject.ExportObject.Value.Properties)
				{
					switch (property.Name.Text)
					{
						case "SGQArray":
							{
								UScriptArray sgqArray = property.Tag!.GetValue<UScriptArray>()!;
								foreach (FPropertyTagType item in sgqArray.Properties)
								{
									barracksSpawnerNames.Add(item.GetValue<FPackageIndex>()!.Name);
								}
							}
							break;
						case "AssocJingBaoQiList":
							{
								UScriptArray sirenList = property.Tag!.GetValue<UScriptArray>()!;
								foreach (FPropertyTagType item in sirenList.Properties)
								{
									UObject? siren = item.GetValue<FPackageIndex>()!.Load();
									if (siren is null) continue;

									FPropertyTag? sirenSpawnerProp = siren.Properties.FirstOrDefault(p => p.Name.Text.Equals("WaiYuanShuaiGuaiQi"));
									if (sirenSpawnerProp is null) continue;

									barracksSpawnerNames.Add(sirenSpawnerProp.Tag!.GetValue<FPackageIndex>()!.Name);
								}
							}
							break;
					}
				}
			}

			// Process spawners

			Dictionary<string, SpawnDataCollection?> spawnDataCache = new();

			logger.Information($"Processing {spawnerObjects.Count} spawners...");
			foreach (SpawnerObject spawnerObject in spawnerObjects)
			{
				FObjectExport export = spawnerObject.Object.Export;
				UObject obj = export.ExportObject.Value;

				List<UBlueprintGeneratedClass> scgClasses = new();
				USceneComponent? rootComponent = null;
				float? spawnInterval = null, playerRadius = null, buildingRadius = null;
				int? spawnCount = null;
				bool isEventTrigger = false;
				int triggerEventId = 0;
				ECustomGameMode? gameMode = null;
				void searchProperties(UObject searchObj)
				{
					foreach (FPropertyTag property in searchObj.Properties)
					{
						switch (property.Name.Text)
						{
							case "SCGClass":
							case "TanChaYouZaiGuaiData":
								if (scgClasses.Count == 0)
								{
									UBlueprintGeneratedClass? scgClass = property.Tag?.GetValue<FPackageIndex>()?.Load<UBlueprintGeneratedClass>();
									if (scgClass is not null)
									{
										scgClasses.Add(scgClass);
									}
									else
									{
										logger.Debug($"[{export.ObjectName}] Spawner has explicitly set data to null.");
									}
								}
								break;
							case "SCGJianGeShiJian":
							case "ZhuiZongFaildTimeConf":
								spawnInterval = property.Tag!.GetValue<float>();
								break;
							case "ShuaGuaiQiWithRand":
								if (scgClasses.Count == 0)
								{
									UScriptArray? array = property.Tag?.GetValue<UScriptArray>();
									if (array is null) continue;

									foreach (FPropertyTagType item in array.Properties)
									{
										FStructFallback? sf = item.GetValue<FStructFallback>();
										if (sf is null) continue;

										FPropertyTag? scgProp = sf.Properties.FirstOrDefault(p => p.Name.Text.Equals("SCGClass"));
										if (scgProp is null) continue;

										UBlueprintGeneratedClass? scgClass = scgProp.Tag?.GetValue<FPackageIndex>()?.Load<UBlueprintGeneratedClass>();
										if (scgClass is not null)
										{
											scgClasses.Add(scgClass);
										}
										else
										{
											logger.Debug($"[{export.ObjectName}] Spawner has explicitly set data to null.");
										}
									}
								}
								break;
							case "SCGNum":
							case "MaxSCGNum":
								spawnCount = property.Tag!.GetValue<int>();
								break;
							case "WanJiaJuLi":
								playerRadius = property.Tag!.GetValue<float>();
								break;
							case "JianZhuJuLi":
								buildingRadius = property.Tag!.GetValue<float>();
								break;
							case "RootComponent":
								if (rootComponent is null)
								{
									rootComponent = property.Tag?.GetValue<FPackageIndex>()?.Load<USceneComponent>();
								}
								break;
							case "IsTriggerEventSpawnMonster":
								isEventTrigger = property.Tag!.GetValue<bool>();
								break;
							case "TriggetEventID":
								triggerEventId = property.Tag!.GetValue<int>();
								break;
							case "SpawnedCustomGameMode":
								if (DataUtil.TryParseEnum(property, out ECustomGameMode mode))
								{
									gameMode = mode;
								}
								break;
						}
					}
				}

				searchProperties(obj);
				if ((scgClasses.Count == 0 || rootComponent is null || !spawnInterval.HasValue) && obj.Class?.Load() is UBlueprintGeneratedClass objClass)
				{
					GameClassHierarchy.SearchInheritance(objClass, (current) =>
					{
						UObject? currentObj = current.ClassDefaultObject.Load();
						if (currentObj is null) return true;

						searchProperties(currentObj);
						return scgClasses.Count > 0 && rootComponent is not null;
					});
				}

				if (!spawnInterval.HasValue)
				{
					switch (spawnerObject.BaseClassName)
					{
						case "HShuaGuaiQiBase":
						default:
							spawnInterval = 10.0f;
							break;
						case "HShuaGuaiVolumeChuFaQi":
							spawnInterval = 3600.0f;
							break;
						case "HTanChaActor":
							spawnInterval = 600.0f;
							break;
					}
				}

				if (!spawnCount.HasValue)
				{
					spawnCount = 0;
				}
				if (!playerRadius.HasValue)
				{
					playerRadius = 0.0f;
				}
				if (!buildingRadius.HasValue)
				{
					buildingRadius = 0.0f;
				}

				if (barracksSpawnerNames.Contains(export.ObjectName.Text))
				{
					spawnInterval = -1.0f;
				}

				string spawnDataKey = string.Join(',', scgClasses.Select(c => c.Name));
				SpawnDataCollection? spawnDataCollection = null;
				if (!spawnDataCache.TryGetValue(spawnDataKey, out spawnDataCollection))
				{
					spawnDataCollection = SpawnDataUtil.LoadSpawnData(scgClasses, logger, export.ObjectName.Text, spawnerObject.Object.DefaultsObject);
					spawnDataCache.Add(spawnDataKey, spawnDataCollection);
				}
				if (spawnDataCollection is null)
				{
					continue;
				}

				void findBabies(SpawnData spawnData)
				{
					foreach (WeightedValue<NpcData> npc in spawnData.NpcData)
					{
						if (npc.Value.IsBaby)
						{
							allBabies.Add(npc.Value);
						}
					}
				}

				findBabies(spawnDataCollection.DefaultSpawnData);
				foreach (var pair in spawnDataCollection.GameModeSpawnData)
				{
					findBabies(pair.Value);
				}

				if (isEventTrigger)
				{
					foreach (var pair in eventsMap)
					{
						string? eventName = null;
						if (!pair.Value.TryGetValue(triggerEventId, out eventName) || gameMode.HasValue && gameMode.Value != pair.Key)
						{
							// Spawner does not exist in this game mode
							continue;
						}

						byte modeMask = pair.Key.CreateMask();
						CreateSpawnPoi(poiDatabase, export.ObjectName.Text, modeMask, spawnDataCollection.DefaultSpawnData, rootComponent, spawnInterval.Value, spawnCount.Value, playerRadius.Value, buildingRadius.Value, eventName, logger);

						// Record spawn data per event for later use when exporting event data
						List<SpawnData>? eventSpawnList;
						if (!poiDatabase.EventSpawnMap.TryGetValue(triggerEventId, out eventSpawnList))
						{
							eventSpawnList = new();
							poiDatabase.EventSpawnMap.Add(triggerEventId, eventSpawnList);
						}
						eventSpawnList.Add(spawnDataCollection.DefaultSpawnData);
					}
				}
				else
				{
					if (spawnDataCollection.GameModeSpawnData.Count > 0)
					{
						byte remainingModes = GameEnumExtensions.AllGameModesMask;
						foreach (var pair in spawnDataCollection.GameModeSpawnData)
						{
							if (gameMode.HasValue && gameMode.Value != pair.Key)
							{
								// Spawner does not exist in this game mode
								continue;
							}

							byte modeMask = pair.Key.CreateMask();
							remainingModes &= (byte)~modeMask;
							CreateSpawnPoi(poiDatabase, export.ObjectName.Text, modeMask, pair.Value, rootComponent, spawnInterval.Value, spawnCount.Value, playerRadius.Value, buildingRadius.Value, null, logger);
						}

						if (gameMode.HasValue) remainingModes &= (byte)gameMode.Value;
						CreateSpawnPoi(poiDatabase, export.ObjectName.Text, remainingModes, spawnDataCollection.DefaultSpawnData, rootComponent, spawnInterval.Value, spawnCount.Value, playerRadius.Value, buildingRadius.Value, null, logger);
					}
					else
					{
						byte? modeMask = gameMode.HasValue ? gameMode.Value.CreateMask() : null;
						CreateSpawnPoi(poiDatabase, export.ObjectName.Text, modeMask, spawnDataCollection.DefaultSpawnData, rootComponent, spawnInterval.Value, spawnCount.Value, playerRadius.Value, buildingRadius.Value, null, logger);
					}
				}
			}
		}

		private void CreateSpawnPoi(
			MapPoiDatabase poiDatabase,
			string spawnerName,
			byte? modeMask,
			SpawnData spawnData,
			USceneComponent? rootComponent,
			float spawnInterval,
			int spawnCount,
			float playerRadius,
			float buildingRadius,
			string? eventName,
			Logger logger)
		{
			string npcName = string.Join(", ", spawnData.NpcNames);
			string babyNpcName = string.Join(", ", spawnData.BabyNames);

			string poiName = npcName;

			FPropertyTag? locationProperty = rootComponent?.Properties.FirstOrDefault(p => p.Name.Text.Equals("RelativeLocation"));
			if (locationProperty is null)
			{
				logger.Warning($"[{spawnerName}] Failed to find location for spawn point");
				return;
			}

			FVector location = locationProperty.Tag!.GetValue<FVector>();

			NpcData firstNpc = spawnData.NpcData.First().Value;
			NpcCategory layerType = SpawnDataUtil.GetNpcCategory(firstNpc);
			ECharacterType characterType = firstNpc.CharacterType;

			SpawnLayerInfo layerInfo;
			SpawnLayerGroup group;
			string type;
			bool male, female;
			bool isAnimal;
			UTexture2D icon;

			void applyLayerTypeAndSex(bool onlyBabies)
			{
				layerInfo = poiDatabase.StaticData.SpawnLayerMap[layerType];
				group = SpawnLayerGroup.Npc;
				type = layerInfo.Name;
				male = false;
				female = false;
				isAnimal = false;
				icon = layerInfo.Icon;

				switch (layerType)
				{
					case NpcCategory.Animal:
						group = SpawnLayerGroup.Animal;
						isAnimal = true;
						poiName = npcName;
						if (npcName.Contains(','))
						{
							type = "(Multiple)";
						}
						else
						{
							type = npcName;
						}
						if (npcName.Length == 0)
						{
							type = "(No Name)";
							poiName = type;
						}
						break;
					case NpcCategory.Human:
						group = SpawnLayerGroup.Human;
						type = spawnData.ClanType.ToEn(poiDatabase.MapName);
						break;

					case NpcCategory.Alpaca:
					case NpcCategory.Bison:
					case NpcCategory.Boar:
					case NpcCategory.Camel:
					case NpcCategory.Capybara:
					case NpcCategory.Chicken:
					case NpcCategory.Donkey:
					case NpcCategory.Eagle:
					case NpcCategory.Elephant:
					case NpcCategory.Flamingo:
					case NpcCategory.Giraffe:
					case NpcCategory.Hippopotamus:
					case NpcCategory.Jaguar:
					case NpcCategory.Leopard:
					case NpcCategory.Lizard:
					case NpcCategory.Llama:
					case NpcCategory.Longhorn:
					case NpcCategory.Moose:
					case NpcCategory.Ostrich:
					case NpcCategory.Rhino:
					case NpcCategory.Tortoise:
					case NpcCategory.Turkey:
						group = SpawnLayerGroup.BabyAnimal;
						isAnimal = true;
						type = babyNpcName;
						poiName = babyNpcName;
						break;
				}

				if (eventName is not null)
				{
					group = SpawnLayerGroup.Event;
					icon = poiDatabase.StaticData.EventIcon;
					type = eventName;
				}

				foreach (WeightedValue<NpcData> npcData in spawnData.NpcData)
				{
					if (!onlyBabies && spawnData.IsMixedAge && npcData.Value.IsBaby) continue;
					if (onlyBabies && !npcData.Value.IsBaby) continue;

					EXingBieType sex = npcData.Value.Sex;
					if (sex == EXingBieType.CHARACTER_XINGBIE_NAN)
					{
						male = true;
					}
					else if (sex == EXingBieType.CHARACTER_XINGBIE_NV)
					{
						female = true;
					}
					else if (sex == EXingBieType.CHARACTER_XINGBIE_WEIZHI)
					{
						male = true;
						female = true;
					}
				}
			}
			applyLayerTypeAndSex(false);

			string levelText = spawnData.MinLevel == spawnData.MaxLevel ? spawnData.MinLevel.ToString() : $"{spawnData.MinLevel} - {spawnData.MaxLevel}";

			string? tribeStatus = null;
			if (spawnData.Statuses.Any())
			{
				tribeStatus = string.Join(", ", spawnData.Statuses.Select(wv => $"{wv.Value.ToEn()} ({wv.Weight:0%})"));
			}

			string? occupation = null;
			string? clanOccupations = null;
			if (spawnData.Occupations.Any())
			{
				occupation = string.Join(", ", spawnData.Occupations.Select(wv => $"{wv.Value.ToEn()} ({wv.Weight:0%})"));
				clanOccupations = $"[{string.Join(',', spawnData.Occupations.Select(o => (int)o.Value))}]";
			}

			int? clanType = spawnData.ClanType == EClanType.CLAN_TYPE_NONE ? null : (int)spawnData.ClanType;
			string? clanAreas = spawnData.ClanAreas.Any() ? string.Join('|', spawnData.ClanAreas) : null;

			string? equipment = SpawnDataUtil.SerializeEquipment(spawnData);

			string? lootId = null;
			string? lootMap = null;
			string? collectMap = null;

			void applyAnimalLoot(bool onlyBabies)
			{
				Dictionary<string, CollectionData> collectionMap = new();
				HashSet<string> collectionClasses = new();
				foreach (NpcData npc in spawnData.NpcData.Select(d => d.Value))
				{
					if (onlyBabies && !npc.IsBaby || !onlyBabies && npc.IsBaby)
					{
						continue;
					}
					if (collectionMap.ContainsKey(npc.CharacterClass.Name)) continue;

					GameClassHierarchy.SearchInheritance(npc.CharacterClass, (current) =>
					{
						if (collectionClasses.Contains(current.Name)) return true;

						if (poiDatabase.StaticData.Loot.CollectionMap.TryGetValue(current.Name, out CollectionData collectionData))
						{
							collectionClasses.Add(current.Name);
							collectionMap.Add(npc.CharacterClass.Name, collectionData);
							return true;
						}
						return false;
					});
				}

				bool isMultiAnimal = collectionMap.Count > 1;

				if (collectionMap.Count > 0)
				{
					StringBuilder collectMapBuilder = new("[");
					foreach (var pair in collectionMap)
					{
						collectMapBuilder.Append("{");

						NpcData npc = spawnData.NpcData.First(wv => wv.Value.CharacterClass.Name.Equals(pair.Key)).Value;

						if (isMultiAnimal)
						{
							collectMapBuilder.Append($"\"name\":\"{npc.Name}\",");
						}

						if (npc.IsBaby && !spawnData.IsMixedAge || onlyBabies)
						{
							if (pair.Value.Baby is not null) collectMapBuilder.Append($"\"base\":\"{pair.Value.Baby}\",");
							else if (pair.Value.Hit is not null) collectMapBuilder.Append($"\"base\":\"{pair.Value.Hit}\",");
						}
						else
						{
							if (pair.Value.Hit is not null) collectMapBuilder.Append($"\"base\":\"{pair.Value.Hit}\",");
							if (pair.Value.FinalHit is not null) collectMapBuilder.Append($"\"bonus\":\"{pair.Value.FinalHit}\",");
						}
						collectMapBuilder.Append($"\"amount\":{pair.Value.Amount}");
						collectMapBuilder.Append("},");
					}
					if (collectMapBuilder.Length > 1)
					{
						collectMapBuilder.Length -= 1; // Remove trailing comma
					}
					collectMapBuilder.Append("]");

					collectMap = collectMapBuilder.ToString();
				}
			}

			if (isAnimal)
			{
				applyAnimalLoot(false);
			}
			else
			{
				lootId = firstNpc.SpawnerLoot ?? firstNpc.CharacterLoot;
				lootMap = SpawnDataUtil.SerializeLootMap(spawnData, lootId);
				if (lootMap is not null) lootId = null;
			}

			string descriptionText;
			if (eventName is null)
			{
				descriptionText = $"Level {levelText}";
			}
			else
			{
				descriptionText = $"Level {levelText}<br />Event: {eventName}";
			}

			bool isWorldBoss = false;

			MapPoi? poi = null;
			if (characterType == ECharacterType.CHARACTER_GIANTBOSS && poiDatabase.TypeLookup.TryGetValue(ETanSuoDianType.ETSD_TYPE_WORLDBOSS, out List<MapPoi>? bossPois))
			{
				foreach (MapPoi bossPoi in bossPois)
				{
					if (!bossPoi.Location.HasValue) continue;

					FVector distance = location - bossPoi.Location.Value;
					if (distance.SizeSquared() < 400000000.0f) // 200 meters
					{
						poi = bossPoi;
						isWorldBoss = true;
						break;
					}
				}
			}

			if (poi is null)
			{
				poi = new()
				{
					GroupIndex = group,
					Type = type,
					Title = poiName,
					Name = layerInfo.Name,
					Description = descriptionText,
					Location = location,
					MapLocation = WorldToMap(location)
				};
			}

			poi.NpcCategory = layerType;
			poi.Male = male;
			poi.Female = female;
			poi.TribeStatus = tribeStatus;
			poi.Occupation = occupation;
			poi.ClanType = clanType;
			poi.ClanAreas = clanAreas;
			poi.ClanOccupations = clanOccupations;
			poi.Equipment = equipment;
			poi.SpawnCount = spawnCount;
			poi.SpawnCountMax = spawnData.SpawnCount;
			poi.SpawnInterval = spawnInterval;
			poi.PlayerExclusionRadius = playerRadius;
			poi.BuildingExclusionRadius = buildingRadius;
			poi.Icon = icon;
			poi.LootId = lootId;
			poi.LootMap = lootMap;
			poi.CollectMap = collectMap;

			if (modeMask.HasValue)
			{
				poi.GameModeMask = modeMask.Value;
			}

			poiDatabase.Spawners.Add(poi);

			if (isWorldBoss) return;

			if (spawnData.IsMixedAge)
			{
				WeightedValue<NpcData>[] babyData = spawnData.NpcData.Where(d => d.Value.IsBaby).ToArray();

				layerType = SpawnDataUtil.GetNpcCategory(babyData[0].Value);
				applyLayerTypeAndSex(true);

				SpawnDataUtil.CalculateLevels(babyData, false, out int minLevel, out int maxLevel);

				levelText = minLevel == maxLevel ? minLevel.ToString() : $"{minLevel} - {maxLevel}";

				collectMap = null;
				applyAnimalLoot(true);

				poi = new(poi)
				{
					GroupIndex = group,
					Type = type,
					Title = babyNpcName,
					Name = layerInfo.Name,
					Description = $"Level {levelText}",
					Male = male,
					Female = female,
					SpawnCountMax = babyData.Sum(b => b.Value.SpawnCount),
					Icon = layerInfo.Icon,
					CollectMap = collectMap
				};

				poiDatabase.Spawners.Add(poi);
			}
		}
	}
}
