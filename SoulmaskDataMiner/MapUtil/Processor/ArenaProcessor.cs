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
	/// Map point of interest processor for arena locations
	/// </summary>
	internal class ArenaProcessor : ProcessorBase
	{
		public ArenaProcessor(MapData mapData)
			: base(mapData)
		{
		}

		public void Process(MapPoiDatabase poiDatabase, IReadOnlyList<FObjectExport> arenaObjects, IReadOnlyDictionary<int, ArenaRewardData> rewardMap, Logger logger)
		{
			logger.Information($"Processing {arenaObjects.Count} arenas...");

			foreach (FObjectExport export in arenaObjects)
			{
				UObject obj = export.ExportObject.Value;

				USceneComponent? rootComponent = obj.Properties.FirstOrDefault(p => p.Name.Text.Equals("RootComponent"))?.Tag?.GetValue<FPackageIndex>()?.Load<USceneComponent>();
				FPropertyTag? locationProperty = rootComponent?.Properties.FirstOrDefault(p => p.Name.Text.Equals("RelativeLocation"));
				if (locationProperty is null)
				{
					logger.Warning($"Failed to locate arena {export.ObjectName}");
					continue;
				}
				FVector location = locationProperty.Tag!.GetValue<FVector>();

				List<ArenaSpawnerInfo>? spawnerInfos = null;
				List<ArenaWinCountInfo>? winCountInfos = null;

				void processArenaObject(UObject obj)
				{
					foreach (FPropertyTag property in obj.Properties)
					{
						switch (property.Name.Text)
						{
							case "ShuaGuaiQiList":
								{
									if (spawnerInfos is not null) break;
									spawnerInfos = new();

									UScriptArray? spawnerArray = property.Tag?.GetValue<UScriptArray>();
									if (spawnerArray is null)
									{
										logger.Warning($"Failed to parse spawner array in arena {obj.Name}");
										break;
									}

									foreach (FPropertyTagType spawnerProperty in spawnerArray.Properties)
									{
										FStructFallback? spawnerStruct = spawnerProperty.GetValue<FStructFallback>();
										if (spawnerStruct is null)
										{
											logger.Warning($"Failed to parse spawner array item in arena {obj.Name}");
											continue;
										}

										string? npcName = null;
										SpawnDataCollection? spawnData = null;
										int? rewardId = null;

										foreach (FPropertyTag spawnerItemProperty in spawnerStruct.Properties)
										{
											switch (spawnerItemProperty.Name.Text)
											{
												case "SGQClass":
													spawnData = SpawnDataUtil.LoadSpawnData(spawnerItemProperty, logger, "Arena Spawner");
													break;
												case "NpcNameTxt":
													npcName = DataUtil.ReadTextProperty(spawnerItemProperty);
													break;
												case "RewardID":
													rewardId = spawnerItemProperty.Tag?.GetValue<int>();
													break;
											}
										}

										if (npcName is null || spawnData is null || !rewardId.HasValue)
										{
											logger.Warning($"Failed to locate data for spawner array item in arena {obj.Name}");
											continue;
										}

										if (spawnData.GameModeSpawnData.Count > 0)
										{
											logger.Warning($"Arena {obj.Name} has game mode specific spawn data, which is not supported.");
										}

										spawnerInfos.Add(new() { NpcName = npcName, SpawnData = spawnData.DefaultSpawnData, RewardId = rewardId.Value });
									}
								}
								break;
							case "WinCountRewardConfigList":
								{
									if (winCountInfos is not null) break;
									winCountInfos = new();

									UScriptArray? winCountArray = property.Tag?.GetValue<UScriptArray>();
									if (winCountArray is null)
									{
										logger.Warning($"Failed to parse win count array in arena {obj.Name}");
										break;
									}

									foreach (FPropertyTagType winCountProperty in winCountArray.Properties)
									{
										FStructFallback? winCountStruct = winCountProperty.GetValue<FStructFallback>();
										if (winCountStruct is null)
										{
											logger.Warning($"Failed to parse win count array item in arena {obj.Name}");
											continue;
										}

										int? winCount = null;
										int? rewardId = null;

										foreach (FPropertyTag winCountItemProperty in winCountStruct.Properties)
										{
											switch (winCountItemProperty.Name.Text)
											{
												case "WinCount":
													winCount = winCountItemProperty.Tag?.GetValue<int>();
													break;
												case "RewardID":
													rewardId = winCountItemProperty.Tag?.GetValue<int>();
													break;
											}
										}

										if (!winCount.HasValue || !rewardId.HasValue)
										{
											logger.Warning($"Failed to locate data for win count array item in arena {obj.Name}");
											continue;
										}

										winCountInfos.Add(new() { WinCount = winCount.Value, RewardId = rewardId.Value });
									}
								}
								break;
						}
					}
				}

				processArenaObject(obj);
				if ((spawnerInfos is null || winCountInfos is null) && obj.Class?.Load() is UBlueprintGeneratedClass objClass)
				{
					GameClassHierarchy.SearchInheritance(objClass, (current) =>
					{
						UObject? currentObj = current.ClassDefaultObject.Load();
						if (currentObj is null) return true;

						processArenaObject(currentObj);
						return spawnerInfos is not null && winCountInfos is not null;
					});
				}

				if (spawnerInfos is null || winCountInfos is null)
				{
					logger.Warning($"Missing info for arena {export.ObjectName}");
					continue;
				}

				foreach (MapPoi arenaPoi in poiDatabase.ArenaPois)
				{
					if (!arenaPoi.Location.HasValue) continue;

					FVector distance = location - arenaPoi.Location.Value;
					if (distance.SizeSquared() < 400000000.0f) // 200 meters
					{
						StringBuilder builder = new("{");

						builder.Append("\"bosses\":[");
						foreach (ArenaSpawnerInfo spawnerInfo in spawnerInfos)
						{
							NpcData firstNpc = spawnerInfo.SpawnData.NpcData.First().Value;

							string? lootId = firstNpc.SpawnerLoot ?? firstNpc.CharacterLoot;
							string? equipmap = SpawnDataUtil.SerializeEquipment(spawnerInfo.SpawnData);

							ArenaRewardData? rewardData;
							if (!rewardMap.TryGetValue(spawnerInfo.RewardId, out rewardData))
							{
								logger.Debug($"Cannot find reward id '{spawnerInfo.RewardId}' from {obj.Name}");
								rewardData = null;
							}

							builder.Append("{");
							builder.Append($"\"name\":\"{spawnerInfo.NpcName}\"");
							builder.Append($",\"minlevel\":\"{spawnerInfo.SpawnData.MinLevel}\"");
							builder.Append($",\"maxlevel\":\"{spawnerInfo.SpawnData.MaxLevel}\"");
							if (rewardData is not null)
							{
								builder.Append($",\"reward\":\"{rewardData.LootId}\"");
							}
							if (lootId is not null && !lootId.Equals("None"))
							{
								builder.Append($",\"loot\":\"{lootId}\"");
							}
							if (equipmap is not null)
							{
								builder.Append($",\"equipmap\":{equipmap}");
							}
							builder.Append("},");
						}
						builder.Length -= 1; // Remove trailing comma
						builder.Append("]");

						builder.Append(",\"wins\":[");
						foreach (ArenaWinCountInfo winCountInfo in winCountInfos)
						{
							ArenaRewardData? rewardData;
							if (!rewardMap.TryGetValue(winCountInfo.RewardId, out rewardData))
							{
								logger.Debug($"Cannot find reward id '{winCountInfo.RewardId}' from {obj.Name}");
								rewardData = null;
							}

							builder.Append("{");
							builder.Append($"\"count\":\"{winCountInfo.WinCount}\"");
							if (rewardData is not null)
							{
								builder.Append($",\"reward\":\"{rewardData.LootId}\"");
							}
							builder.Append("},");
						}
						builder.Length -= 1; // Remove trailing comma
						builder.Append("]");

						builder.Append("}");

						arenaPoi.ArenaInfo = builder.ToString();
						break;
					}
				}
			}
		}

		private struct ArenaSpawnerInfo
		{
			public string NpcName;
			public SpawnData SpawnData;
			public int RewardId;
		}

		private struct ArenaWinCountInfo
		{
			public int WinCount;
			public int RewardId;
		}
	}
}
