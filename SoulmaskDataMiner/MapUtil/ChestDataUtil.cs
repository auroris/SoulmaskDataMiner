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
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using SoulmaskDataMiner.Data;
using SoulmaskDataMiner.GameData;

namespace SoulmaskDataMiner.MapUtil
{
	/// <summary>
	/// Helper for extracting data from loot chest classes
	/// </summary>
	internal static class ChestDataUtil
	{
		public static ChestData? LoadChestData(string objectName, UObject chestObject, Logger logger)
		{
			string? name = null;
			int respawnTime = -1;
			float respawnExclusionRadius = -1.0f;
			string? lootId = null;
			string? openTip = null;
			FPackageIndex? lootItem = null;
			List<ChestWeatherRule> specialWeatherRules = new();
			USceneComponent? rootComponent = null;
			HashSet<ECustomGameMode> availableGameModes = new();
			Dictionary<ECustomGameMode, string> gameModeLootIds = new();
			void searchProperties(UObject searchObj)
			{
				UScriptArray? openCheckList = null;

				foreach (FPropertyTag property in searchObj.Properties)
				{
					switch (property.Name.Text)
					{
						case "ShuaXinTime":
							if (respawnTime < 0)
							{
								respawnTime = property.Tag!.GetValue<int>();
							}
							break;
						case "CheckNearlyPlayerFanWei":
							if (respawnExclusionRadius < 0.0f)
							{
								respawnExclusionRadius = property.Tag!.GetValue<float>();
							}
							break;
						case "AliveCustomeGameMode":
							{
								UScriptArray? gameModeArray = property.Tag?.GetValue<UScriptArray>();
								if (gameModeArray is not null)
								{
									foreach (FPropertyTagType item in gameModeArray.Properties)
									{
										if (DataUtil.TryParseEnum(item, out ECustomGameMode gameMode))
										{
											availableGameModes.Add(gameMode);
										}
										else
										{
											logger.Warning($"[{objectName}] Chest specifies unrecognized game mode: {item.GetValue<FName>().Text}");
										}
									}
								}
							}
							break;
						case "DifferentGameModeDropID":
							{
								UScriptMap? gameModeLootMap = property.Tag?.GetValue<UScriptMap>();
								if (gameModeLootMap is not null)
								{
									foreach (var lootPair in gameModeLootMap.Properties)
									{
										ECustomGameMode mode;
										string loot;
										if (DataUtil.TryParseEnum(lootPair.Key, out mode))
										{
											loot = lootPair.Value!.GetValue<FName>().Text;
											gameModeLootIds.TryAdd(mode, loot);
										}
										else
										{
											logger.Warning($"[{objectName}] Chest specifies unrecognized game mode: {lootPair.Key.GetValue<FName>().Text}");
										}
									}
								}
							}
							break;
						case "BaoXiangDiaoLuoID":
							if (lootId is null)
							{
								lootId = property.Tag!.GetValue<FName>().Text;
							}
							break;
						case "JianZhuDisplayName":
							if (name is null)
							{
								name = DataUtil.ReadTextProperty(property);
							}
							break;
						case "OpenCheckDaoJuData":
							if (openTip is null)
							{
								openCheckList = property.Tag?.GetValue<UScriptArray>();
							}
							break;
						case "KaiQiJiaoHuDaoJuClass":
							if (lootItem is null)
							{
								lootItem = property.Tag?.GetValue<FPackageIndex>();
							}
							break;
						case "SpecialWeatherRule":
							{
								FStructFallback? rule = property.Tag?.GetValue<FStructFallback>();
								if (rule is not null)
								{
									specialWeatherRules.Add(ChestWeatherRule.Read(rule, logger));
								}
							}
							break;
						case "RootComponent":
							if (rootComponent is null)
							{
								rootComponent = property.Tag?.GetValue<FPackageIndex>()?.Load<USceneComponent>();
							}
							break;
					}
				}

				if (openCheckList is not null)
				{
					List<string> openTips = new();
					foreach (FPropertyTagType property in openCheckList.Properties)
					{
						FStructFallback? openCheckObj = property.GetValue<FStructFallback>();
						if (openCheckObj is null) continue;

						FPropertyTag? openTipProperty = openCheckObj.Properties.FirstOrDefault(p => p.Name.Text.Equals("NotOpenTips"));
						if (openTipProperty is null) continue;

						openTips.Add(DataUtil.ReadTextProperty(openTipProperty)!);
					}
					openTip = string.Join("<br />", openTips);
				}
			}

			searchProperties(chestObject);
			if ((respawnTime < 0 || respawnExclusionRadius < 0.0f || lootId is null || name is null || openTip is null || rootComponent is null || specialWeatherRules.Count > 0) && chestObject.Class?.Load() is UBlueprintGeneratedClass objClass)
			{
				GameClassHierarchy.SearchInheritance(objClass, (current) =>
				{
					UObject? currentObj = current.ClassDefaultObject.Load();
					if (currentObj is null) return true;

					searchProperties(currentObj);
					return respawnTime >= 0 && respawnExclusionRadius >= 0.0f && lootId is not null && name is not null && openTip is not null && rootComponent is not null && specialWeatherRules.Count == 0;
				});
			}

			if (respawnTime < 0)
			{
				respawnTime = 0; // Default from HJianZhuBaoXiang
			}
			if (respawnExclusionRadius < 0.0f)
			{
				respawnExclusionRadius = 2000.0f; // Default from HJianZhuBaoXiang
			}

			if (lootId is null && lootItem is null || name is null)
			{
				return null;
			}

			ChestWeatherRule? weatherRule = ChestWeatherRule.Flatten(specialWeatherRules);

			return new(name, respawnTime, respawnExclusionRadius, lootId, openTip, lootItem, weatherRule, rootComponent, availableGameModes, gameModeLootIds);
		}
	}

	internal class ChestData
	{
		public string Name { get; }
		public int RespawnTime { get; }
		public float RespawnExclusionRadius { get; }
		public string? LootId { get; }
		public string? OpenTip { get; }
		public FPackageIndex? LootItem { get; }
		public ChestWeatherRule? WeatherRule { get; }
		public USceneComponent? RootComponent { get; }
		public IReadOnlySet<ECustomGameMode> AvailableGameModes { get; }
		public IReadOnlyDictionary<ECustomGameMode, string> GameModeLootIds { get; }

		public ChestData(
			string name,
			int respawnTime,
			float respawnExclusionRadius,
			string? lootId,
			string? openTip,
			FPackageIndex? lootItem,
			ChestWeatherRule? weatherRule,
			USceneComponent? rootComponent,
			IReadOnlySet<ECustomGameMode> availableGameModes,
			IReadOnlyDictionary<ECustomGameMode, string> gameModeLootIds)
		{
			Name = name;
			RespawnTime = respawnTime;
			RespawnExclusionRadius = respawnExclusionRadius;
			LootId = lootId;
			OpenTip = openTip;
			LootItem = lootItem;
			WeatherRule = weatherRule;
			RootComponent = rootComponent;
			AvailableGameModes = availableGameModes;
			GameModeLootIds = gameModeLootIds;
		}
	}

	internal class ChestWeatherRule
	{
		private FieldSetFlags mFieldSetFlags;

		public bool IsEnabled { get; set; }
		public List<ETianQiAreaType> WeatherTypes { get; }
		public float SpawnDelay { get; set; }
		public float SpawnChance { get; set; }
		public float Duration { get; set; }
		public float BuildingExclusionRadius { get; set; }
		public float BuildingExclusionCheckInterval { get; set; }

		private ChestWeatherRule()
		{
			mFieldSetFlags = 0;

			// Defaults from HJianZhuConditionBaoXiang and FBaoXiangWeatherRefreshRule
			IsEnabled = false;
			WeatherTypes = new();
			SpawnDelay = 0.0f;
			SpawnChance = 0.5f;
			Duration = 0.0f;
			BuildingExclusionRadius = 0.0f;
			BuildingExclusionCheckInterval = 1800.0f;
		}

		private ChestWeatherRule(ChestWeatherRule other)
		{
			mFieldSetFlags = other.mFieldSetFlags;
			IsEnabled = other.IsEnabled;
			WeatherTypes = other.WeatherTypes.ToList();
			SpawnDelay = other.SpawnDelay;
			SpawnChance = other.SpawnChance;
			Duration = other.Duration;
			BuildingExclusionRadius = other.BuildingExclusionRadius;
			BuildingExclusionCheckInterval = other.BuildingExclusionCheckInterval;
		}

		public static ChestWeatherRule Read(FStructFallback data, Logger logger)
		{
			ChestWeatherRule result = new();

			foreach (FPropertyTag property in data.Properties)
			{
				switch (property.Name.Text)
				{
					case "bEnableSpecialWeatherRule":
						result.IsEnabled = property.Tag!.GetValue<bool>();
						result.mFieldSetFlags |= FieldSetFlags.IsEnabled;
						break;
					case "SpecialWeatherType":
						{
							UScriptArray? weatherTypes = property.Tag?.GetValue<UScriptArray>();
							if (weatherTypes is null)
							{
								logger.Warning("Failed to read SpecialWeatherType property from SpecialWeatherRule");
								break;
							}
							foreach (FPropertyTagType weatherTypeProperty in weatherTypes.Properties)
							{
								if (DataUtil.TryParseEnum(weatherTypeProperty, out ETianQiAreaType value))
								{
									result.WeatherTypes.Add(value);
								}
							}
							result.mFieldSetFlags |= FieldSetFlags.WeatherTypes;
						}
						break;
					case "RefreshEnterTime":
						result.SpawnDelay = property.Tag!.GetValue<float>();
						result.mFieldSetFlags |= FieldSetFlags.SpawnDelay;
						break;
					case "RefreshDutation":
						result.Duration = property.Tag!.GetValue<float>();
						result.mFieldSetFlags |= FieldSetFlags.Duration;
						break;
					case "RefreshSingleChance":
						result.SpawnChance = property.Tag!.GetValue<float>();
						result.mFieldSetFlags |= FieldSetFlags.SpawnChance;
						break;
					case "RefreshCheckBuildingRange":
						result.BuildingExclusionRadius = property.Tag!.GetValue<float>();
						result.mFieldSetFlags |= FieldSetFlags.BuildingExclusionRadius;
						break;
					case "AroundHasBuildingRefreshInterval":
						result.BuildingExclusionCheckInterval = property.Tag!.GetValue<float>();
						result.mFieldSetFlags |= FieldSetFlags.BuildingExclusionCheckInterval;
						break;
				}
			}

			return result;
		}

		public static ChestWeatherRule? Flatten(IReadOnlyList<ChestWeatherRule> rules)
		{
			if (rules.Count == 0)
			{
				return null;
			}

			ChestWeatherRule result = new(rules[rules.Count - 1]);

			for (int i = rules.Count - 2; i >= 0; --i)
			{
				ChestWeatherRule rule = rules[i];
				if ((rule.mFieldSetFlags & FieldSetFlags.IsEnabled) != 0)
				{
					result.IsEnabled = rule.IsEnabled;
				}
				if ((rule.mFieldSetFlags & FieldSetFlags.WeatherTypes) != 0)
				{
					result.WeatherTypes.Clear();
					result.WeatherTypes.AddRange(rule.WeatherTypes);
				}
				if ((rule.mFieldSetFlags & FieldSetFlags.SpawnDelay) != 0)
				{
					result.SpawnDelay = rule.SpawnDelay;
				}
				if ((rule.mFieldSetFlags & FieldSetFlags.SpawnChance) != 0)
				{
					result.SpawnChance = rule.SpawnChance;
				}
				if ((rule.mFieldSetFlags & FieldSetFlags.Duration) != 0)
				{
					result.Duration = rule.Duration;
				}
				if ((rule.mFieldSetFlags & FieldSetFlags.BuildingExclusionRadius) != 0)
				{
					result.BuildingExclusionRadius = rule.BuildingExclusionRadius;
				}
				if ((rule.mFieldSetFlags & FieldSetFlags.BuildingExclusionCheckInterval) != 0)
				{
					result.BuildingExclusionCheckInterval = rule.BuildingExclusionCheckInterval;
				}
				result.mFieldSetFlags |= rule.mFieldSetFlags;
			}

			return result;
		}

		[Flags]
		private enum FieldSetFlags
		{
			IsEnabled = 0x01,
			WeatherTypes = 0x02,
			SpawnDelay = 0x04,
			SpawnChance = 0x08,
			Duration = 0x10,
			BuildingExclusionRadius = 0x20,
			BuildingExclusionCheckInterval = 0x40
		}
	}
}
