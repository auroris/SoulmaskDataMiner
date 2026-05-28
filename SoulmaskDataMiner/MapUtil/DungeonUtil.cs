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

using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using SoulmaskDataMiner.Data;
using SoulmaskDataMiner.GameData;
using System.Diagnostics.CodeAnalysis;

namespace SoulmaskDataMiner.MapUtil
{
	/// <summary>
	/// Utility for gathering data about the game's procedural dungeons
	/// </summary>
	internal class DungeonUtil
	{
		private Dictionary<string, DungeonEntrance>? mEntranceMap;

		private readonly MapLevelData mMapLevelData;

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="mapLevelData">The level data for the map to load dungeon data for</param>
		public DungeonUtil(MapLevelData mapLevelData)
		{
			mMapLevelData = mapLevelData;
		}

		/// <summary>
		/// Loads data about procedural dungeons
		/// </summary>
		/// <returns>Dungeon data, mapped by entrance actor class names</returns>
		public IReadOnlyDictionary<string, DungeonData>? LoadDungeonData(Logger logger)
		{
			IReadOnlyDictionary<string, DungeonConfig>? configMap = LoadDungeonConfig(logger);
			if (configMap is null) return null;

			FindEntrances(configMap, logger);
			FindLevelObjects(configMap, logger);
			FindThemeObjects(configMap, logger);

			return configMap.ToDictionary(p => p.Value.Entrance.AssetName, p => CreateDungeonData(p.Value));
		}

		private IReadOnlyDictionary<string, DungeonConfig>? LoadDungeonConfig(Logger logger)
		{
			UScriptArray? dungeonArray = mMapLevelData.ConfigData.Properties.FirstOrDefault(p => p.Name.Text.Equals("DiXiaChengConfigList"))?.Tag?.GetValue<UScriptArray>();
			if (dungeonArray is null || dungeonArray.Properties.Count == 0) return null;

			Dictionary<string, DungeonConfig> result = new();

			foreach (FPropertyTagType item in dungeonArray.Properties)
			{
				string? name = null;
				DungeonConfig config = new();

				FStructFallback itemObj = item.GetValue<FStructFallback>()!;
				foreach (FPropertyTag property in itemObj.Properties)
				{
					switch (property.Name.Text)
					{
						case "Name":
							name = property.Tag!.GetValue<string>()!;
							break;
						case "Title":
							config.Title = DataUtil.ReadTextProperty(property)!;
							break;
						case "Desc":
							config.Description = DataUtil.ReadTextProperty(property)!;
							break;
						case "MaxCount":
							config.MaxCount = property.Tag!.GetValue<int>();
							break;
						case "MaxTimeSeconds":
							config.MaxTimeSeconds = property.Tag!.GetValue<float>();
							break;
						case "MaxPlayers":
							config.MaxPlayers = property.Tag!.GetValue<int>();
							break;
						case "SGFModuleDataBase":
							config.ModuleDatabase = property.Tag!.GetValue<FSoftObjectPath>();
							break;
						case "SGFTheme":
							config.ThemeAsset = property.Tag!.GetValue<FSoftObjectPath>();
							break;
						case "MaxRetryTimes":
							config.MaxRetryTimes = property.Tag!.GetValue<int>();
							break;
					}
				}

				if (name is null)
				{
					logger.Warning("Unable to read dungeon config");
					continue;
				}

				result.Add(name, config);
			}

			return result;
		}

		private void FindEntrances(IReadOnlyDictionary<string, DungeonConfig> configMap, Logger logger)
		{
			IReadOnlyDictionary<string, DungeonEntrance> entranceMap = BuildEntranceMap(logger);
			foreach (var pair in configMap)
			{
				if (entranceMap.TryGetValue(pair.Key, out DungeonEntrance entrance))
				{
					configMap[pair.Key].Entrance = entrance;
				}
				else
				{
					logger.Warning($"Unable to find an entrance for dungeon {pair.Key}");
				}
			}
		}

		private void FindLevelObjects(IReadOnlyDictionary<string, DungeonConfig> configMap, Logger logger)
		{
			foreach (var pair in configMap)
			{
				UObject moduleDB = pair.Value.ModuleDatabase.Load();
				UScriptArray? moduleArray = moduleDB.Properties.FirstOrDefault(p => p.Name.Text.Equals("Modules"))?.Tag?.GetValue<UScriptArray>();
				if (moduleArray is null)
				{
					logger.Warning($"Unable to load module list for dungeon {pair.Key}");
					continue;
				}

				Dictionary<string, ModuleLevelData> cachedModuleData = new();

				List<FSoftObjectPath> modulePaths = new();
				foreach (FPropertyTagType moduleProperty in moduleArray.Properties)
				{
					FStructFallback moduleObj = moduleProperty.GetValue<FStructFallback>()!;
					FPropertyTagType? levelProperty = moduleObj.Properties.FirstOrDefault(p => p.Name.Text.Equals("Level"))?.Tag;
					if (levelProperty is null)
					{
						logger.Warning($"Unable to load a module for dungeon {pair.Key}");
						continue;
					}

					FSoftObjectPath moduleLevelPath = levelProperty.GetValue<FSoftObjectPath>();

					if (!cachedModuleData.TryGetValue(moduleLevelPath.AssetPathName.Text, out ModuleLevelData moduleLevelData))
					{
						moduleLevelData = new();

						Package moduleLevelPackage = (Package)moduleLevelPath.Load().Owner!;
						foreach (FObjectExport export in moduleLevelPackage.ExportMap)
						{
							if (!export.ClassName.Equals("HShuaGuaiQiDiXiaCheng")) continue;

							string? dungeonName = null;
							string? spawnerName = null;
							SpawnDataCollection? spawnerData = null;
							foreach (FPropertyTag property in export.ExportObject.Value.Properties)
							{
								switch (property.Name.Text)
								{
									case "UsedDiXiaChengName":
										dungeonName = property.Tag?.GetValue<string>();
										break;
									case "SCGClass":
										spawnerName = property.Tag?.GetValue<FPackageIndex>()?.Name;
										spawnerData = SpawnDataUtil.LoadSpawnData(property, logger, export.ObjectName.Text);
										break;
								}

								if (dungeonName is not null && spawnerData is not null)
								{
									break;
								}
							}

							if (dungeonName is null || spawnerName is null || spawnerData is null)
							{
								logger.Warning($"Unable to find data for a spawner in dungeon level {pair.Key}, module {moduleLevelPath.AssetPathName.Text}");
								continue;
							}

							if (spawnerData.GameModeSpawnData.Count > 0)
							{
								logger.Warning($"Spawner \"{spawnerName}\" in dungeon level {pair.Key} has game mode specific spawn data, which is not supported.");
							}

							if (!moduleLevelData.SpawnerMap.TryGetValue(dungeonName, out Dictionary<string, SpawnData>? map))
							{
								map = new();
								moduleLevelData.SpawnerMap.Add(dungeonName, map);
							}
							map.TryAdd(spawnerName, spawnerData.DefaultSpawnData);
						}

						cachedModuleData.Add(moduleLevelPath.AssetPathName.Text, moduleLevelData);
					}

					if (moduleLevelData.SpawnerMap.TryGetValue(pair.Key, out Dictionary<string, SpawnData>? spawners))
					{
						foreach (var spawnerPair in spawners)
						{
							pair.Value.Spawners.TryAdd(spawnerPair.Key, spawnerPair.Value);
						}
					}
				}
			}
		}

		private void FindThemeObjects(IReadOnlyDictionary<string, DungeonConfig> configMap, Logger logger)
		{
			const string chestBaseClass = "HJianZhuBaoXiang";

			List<BlueprintClassInfo> chestBpClasses = new(GameClassHierarchy.Instance.GetDerivedBlueprintClasses(chestBaseClass));

			Dictionary<string, UObject?> chestClasses = new();
			foreach (BlueprintClassInfo bpClass in chestBpClasses)
			{
				UBlueprintGeneratedClass? exportObj = (UBlueprintGeneratedClass?)bpClass.Export.ExportObject.Value;
				UObject? defaultObj = exportObj?.ClassDefaultObject.Load();
				chestClasses.Add(bpClass.Name, defaultObj);
			}

			foreach (var pair in configMap)
			{
				Package package = (Package)pair.Value.ThemeAsset.Load().Owner!;

				foreach (FObjectExport export in package.ExportMap)
				{
					if (chestClasses.TryGetValue(export.ClassName, out UObject? defaultObj))
					{
						ChestData? chestData = ChestDataUtil.LoadChestData(export.ObjectName.Text, export.ExportObject.Value, logger);
						if (chestData is null)
						{
							logger.Warning($"[{export.ObjectName.Text}] Unable to load data for chest");
							continue;
						}

						pair.Value.Chests.TryAdd(new(chestData), chestData);
					}
				}
			}
		}

		private DungeonData CreateDungeonData(DungeonConfig dungeonConfig)
		{
			return new(
				dungeonConfig.Title,
				dungeonConfig.Description,
				dungeonConfig.Entrance.Level,
				dungeonConfig.MaxCount,
				dungeonConfig.MaxTimeSeconds,
				dungeonConfig.MaxPlayers,
				dungeonConfig.MaxRetryTimes,
				dungeonConfig.ThemeAsset,
				dungeonConfig.Entrance.ItemCost,
				dungeonConfig.Entrance.MaskEnergyCost,
				dungeonConfig.Spawners.Values.ToArray(),
				dungeonConfig.Chests.Values.ToArray());
		}

		private IReadOnlyDictionary<string, DungeonEntrance> BuildEntranceMap(Logger logger)
		{
			if (mEntranceMap is not null) return mEntranceMap;

			mEntranceMap = new();

			foreach (BlueprintClassInfo classInfo in GameClassHierarchy.Instance.GetDerivedBlueprintClasses("HJianZhuGameFunction"))
			{
				UBlueprintGeneratedClass? bpClass = (UBlueprintGeneratedClass?)classInfo.Export.ExportObject.Value;
				UObject? defaultsObj = bpClass?.ClassDefaultObject.Load();
				if (defaultsObj is null) continue;

				UScriptMap? functionMap = defaultsObj.Properties.FirstOrDefault(p => p.Name.Text.Equals("GameFunctionExecutionMap"))?.Tag?.GetValue<UScriptMap>();
				if (functionMap is null || functionMap.Properties.Count == 0) continue;

				var funcData = functionMap.Properties.First();
				UScriptArray? execList = funcData.Value?.GetValue<FStructFallback>()?.Properties[0].Tag?.GetValue<UScriptArray>();
				if (execList is null || execList.Properties.Count == 0) continue;

				EJianZhuGameFunctionType funcType = EJianZhuGameFunctionType.EJZGFT_NOT_DEFINE;
				string? name = null;

				FStructFallback execFunc = execList.Properties.First().GetValue<FStructFallback>()!;
				foreach (FPropertyTag property in execFunc.Properties)
				{
					switch (property.Name.Text)
					{
						case "FunctionType":
							if (DataUtil.TryParseEnum(property, out EJianZhuGameFunctionType value))
							{
								funcType = value;
							}
							break;
						case "ExecutePara1":
							name = property.Tag?.GetValue<string>();
							break;
					}
				}

				if (funcType != EJianZhuGameFunctionType.EJZGFT_ENTRY_DIXIACHENG)
				{
					continue;
				}

				if (name is null)
				{
					continue;
				}

				FPackageIndex recipeAsset = funcData.Key.GetValue<FPackageIndex>()!;
				UBlueprintGeneratedClass? recipeClass = recipeAsset.Load<UBlueprintGeneratedClass>();
				UObject? recipeObj = recipeClass?.ClassDefaultObject.Load();
				if (recipeObj is null)
				{
					logger.Warning($"Dungeon entry recipe class \"{recipeAsset.Name}\" could not be loaded");
					continue;
				}

				DungeonEntrance entrance = new() { AssetName = defaultsObj.Class!.Name.Text, ItemCost = new() };
				UScriptArray? recipeItemArray = null;
				foreach (FPropertyTag property in recipeObj.Properties)
				{
					switch (property.Name.Text)
					{
						case "PeiFangDengJi":
							entrance.Level = property.Tag!.GetValue<int>();
							break;
						case "DemandDaoJu":
							recipeItemArray = property.Tag?.GetValue<UScriptArray>();
							break;
						case "DemandMianJuNengLiang":
							entrance.MaskEnergyCost = property.Tag!.GetValue<int>();
							break;
					}
				}

				if (recipeItemArray is not null)
				{
					foreach (FPropertyTagType item in recipeItemArray.Properties)
					{
						UScriptArray? itemsArray = null;
						int count = 0;

						FStructFallback itemObj = item.GetValue<FStructFallback>()!;
						foreach (FPropertyTag property in itemObj.Properties)
						{
							switch (property.Name.Text)
							{
								case "DemandDaoJu":
									itemsArray = property.Tag?.GetValue<UScriptArray>();
									break;
								case "DemandCount":
									count = property.Tag!.GetValue<int>();
									break;
							}
						}

						if (itemsArray is null || itemsArray.Properties.Count == 0) continue;
						if (count == 0) continue;

						foreach (FPropertyTagType componentItem in itemsArray.Properties)
						{
							entrance.ItemCost.Add(new() { ItemClass = componentItem.GetValue<FPackageIndex>()!.Name, Count = count });
						}
					}
				}

				mEntranceMap.Add(name, entrance);
			}

			return mEntranceMap;
		}

		private class DungeonConfig
		{
			public string Title { get; set; }
			public string Description { get; set; }
			public int MaxCount { get; set; }
			public float MaxTimeSeconds { get; set; }
			public int MaxPlayers { get; set; }
			public int MaxRetryTimes { get; set; }
			public FSoftObjectPath ModuleDatabase { get; set; }
			public FSoftObjectPath ThemeAsset { get; set; }
			public DungeonEntrance Entrance { get; set; }
			public Dictionary<string, SpawnData> Spawners { get; set; }
			public Dictionary<ChestKey, ChestData> Chests { get; set; }

			public DungeonConfig()
			{
				Title = null!;
				Description = null!;
				Spawners = new();
				Chests = new();
			}
		}

		private struct DungeonEntrance
		{
			public string AssetName;
			public int Level;
			public List<RecipeComponent> ItemCost;
			public int MaskEnergyCost;
		}

		private struct ModuleLevelData
		{
			public Dictionary<string, Dictionary<string, SpawnData>> SpawnerMap;

			public ModuleLevelData()
			{
				SpawnerMap = new();
			}
		}

		private readonly struct ChestKey : IEquatable<ChestKey>
		{
			public readonly string? LootId;
			public readonly string? LootItem;

			public ChestKey(ChestData chestData)
			{
				LootId = chestData.LootId;
				LootItem = chestData.LootItem?.Name;
			}

			public override int GetHashCode()
			{
				return HashCode.Combine(LootId, LootItem);
			}

			public override bool Equals([NotNullWhen(true)] object? obj)
			{
				return obj is ChestKey other && Equals(other);
			}

			public bool Equals(ChestKey other)
			{
				return string.Equals(LootId, other.LootId) && string.Equals(LootId, other.LootItem);
			}
		}
	}

	/// <summary>
	/// Properties of a procedural dungeon
	/// </summary>
	internal class DungeonData
	{
		/// <summary>
		/// The display name
		/// </summary>
		public string Title { get; }

		/// <summary>
		/// The description
		/// </summary>
		public string Description { get; }

		/// <summary>
		/// The intended level to enter
		/// </summary>
		public int Level { get; }

		/// <summary>
		/// Maximum concurrent instances
		/// </summary>
		public int MaxCount { get; }

		/// <summary>
		/// Maximum time after opening an instance until it is closed
		/// </summary>
		public float MaxTimeSeconds { get; }

		/// <summary>
		/// Max players in an instance
		/// </summary>
		public int MaxPlayers { get; }

		/// <summary>
		/// Unknown
		/// </summary>
		public int MaxRetryTimes { get; }

		/// <summary>
		/// Asset containing data about the dungeon
		/// </summary>
		public FSoftObjectPath ThemeAsset { get; }

		/// <summary>
		/// Item cost for entering
		/// </summary>
		public IReadOnlyList<RecipeComponent> EntranceItemCost { get; }
		
		/// <summary>
		/// Mask energy cost for entering
		/// </summary>
		public int EntranceMaskEnergyCost { get; }

		/// <summary>
		/// Possible spawners that can be found in the dungeon
		/// </summary>
		public IReadOnlyList<SpawnData> Spawners { get; }

		/// <summary>
		/// Possible chests that can be found in the dungeon
		/// </summary>
		public IReadOnlyList<ChestData> Chests { get; }

		public DungeonData(
			string title,
			string description,
			int level,
			int maxCount,
			float maxTimeSeconds,
			int maxPlayers,
			int maxRetryTimes,
			FSoftObjectPath themeAsset,
			IReadOnlyList<RecipeComponent> entranceItemCost,
			int entranceMaskEnergyCost,
			IReadOnlyList<SpawnData> spawners,
			IReadOnlyList<ChestData> chests)
		{
			Title = title;
			Description = description;
			Level = level;
			MaxCount = maxCount;
			MaxTimeSeconds = maxTimeSeconds;
			MaxPlayers = maxPlayers;
			MaxRetryTimes = maxRetryTimes;
			ThemeAsset = themeAsset;
			EntranceItemCost = entranceItemCost;
			EntranceMaskEnergyCost = entranceMaskEnergyCost;
			Spawners = spawners;
			Chests = chests;
		}
	}

	/// <summary>
	/// A component of a recipe
	/// </summary>
	internal struct RecipeComponent
	{
		/// <summary>
		/// The required item
		/// </summary>
		public string ItemClass;

		/// <summary>
		/// How many of the item is required
		/// </summary>
		public int Count;
	}
}
