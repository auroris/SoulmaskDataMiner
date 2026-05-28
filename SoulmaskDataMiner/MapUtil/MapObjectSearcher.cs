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
	/// Used by MapMiner to locate objects within map levels
	/// </summary>
	internal static class MapObjectSearcher
	{
		public static bool FindTabletData(IProviderManager providerManager, MapPoiDatabase poiDatabase, Achievements achievements, Logger logger)
		{
			if (!achievements.AllAchievements.TryGetValue("BP_ChengJiu_ShiBan_001_C", out AchievementData? ancientAchievement))
			{
				ancientAchievement = null;
			}
			if (!achievements.AllAchievements.TryGetValue("BP_ChengJiu_ShiBan_002_C", out AchievementData? divineAchievement))
			{
				divineAchievement = null;
			}

			foreach (var pair in providerManager.Provider.Files)
			{
				if (!pair.Key.StartsWith("WS/Content/Blueprints/JianZhu/GameFunction/Shibei/") && !pair.Key.StartsWith("WS/Content/AdditionMap01/BluePrints/Building/GameFunction/Tablet/")) continue;
				if (!pair.Key.EndsWith(".uasset")) continue;

				Package package = (Package)providerManager.Provider.LoadPackage(pair.Value);

				string className = $"{Path.GetFileNameWithoutExtension(pair.Key)}_C";

				UObject? classDefaults = DataUtil.FindBlueprintDefaultsObject(package);
				if (classDefaults is null)
				{
					logger.Warning($"Could not find data for tablet POI {className}");
					continue;
				}

				UBlueprintGeneratedClass? tabletDataClass = null;
				foreach (FPropertyTag property in classDefaults.Properties)
				{
					if (!property.Name.Text.Equals("GameFunctionExecutionMap")) continue;

					UScriptMap? executionMap = property.Tag?.GetValue<UScriptMap>();
					if (executionMap is null)
					{
						logger.Warning($"Unable to read data for tablet POI {className}");
						break;
					}

					if (executionMap.Properties.Count < 1)
					{
						logger.Warning($"Unable to read data for tablet POI {className}");
						break;
					}

					FStructFallback? executionMapValue = executionMap.Properties.First().Value?.GetValue<FStructFallback>();
					UScriptArray? executionList = executionMapValue?.Properties[0].Tag?.GetValue<UScriptArray>();
					FStructFallback? executionStruct = executionList?.Properties[0].GetValue<FStructFallback>();
					if (executionStruct is null)
					{
						logger.Warning($"Unable to read data for tablet POI {className}");
						break;
					}

					foreach (FPropertyTag executionProperty in executionStruct.Properties)
					{
						if (!executionProperty.Name.Text.Equals("ExecuteObjPara")) continue;

						tabletDataClass = executionProperty.Tag?.GetValue<FPackageIndex>()?.ResolvedObject?.Load() as UBlueprintGeneratedClass;

						break;
					}

					break;
				}

				UObject? tabletDataObj = tabletDataClass?.ClassDefaultObject.Load();
				if (tabletDataObj is null)
				{
					logger.Warning($"Unable to read data for tablet POI {className}");
					continue;
				}

				string idStr = className.Substring(className.Length - 5, 3);
				int key;
				if (!int.TryParse(idStr, out key))
				{
					logger.Warning($"Unable to parse tablet id from class name {className}");
					key = -1;
				}

				MapPoi tabletData = new()
				{
					Key = key >= 0 ? (key + 100000) : null,
					GroupIndex = SpawnLayerGroup.PointOfInterest,
					Type = "Tablet (Ancient)",
					Title = "Ancient Tablet",
					Achievement = ancientAchievement
				};
				List<string> unlocks = new();
				foreach (FPropertyTag property in tabletDataObj.Properties)
				{
					switch (property.Name.Text)
					{
						case "ChengJiuKeJiPoint":
							tabletData.Name = $"Points: {property.Tag!.GetValue<int>()}";
							break;
						case "ChengJiuName":
							tabletData.Description = DataUtil.ReadTextProperty(property)!;
							break;
						case "ChengJiuTiaoJian":
							tabletData.Extra = DataUtil.ReadTextProperty(property)!;
							break;
						case "TextureIcon":
							tabletData.Icon = DataUtil.ReadTextureProperty(property)!;
							break;
						case "NumberParam1":
							if (property.Tag!.GetValue<int>() == 1)
							{
								tabletData.Type = "Tablet (Divine)";
								tabletData.Title = "Divine Tablet";
								tabletData.Achievement = divineAchievement;
							}
							break;
						case "AutoGetKeJiList":
							{
								UScriptArray list = property.Tag!.GetValue<UScriptArray>()!;
								foreach (FPropertyTagType item in list.Properties)
								{
									UObject? unlockNode = item.GetValue<FStructFallback>()?.Properties.FirstOrDefault(p => p.Name.Text.Equals("KeJiSubNodeClass"))?.Tag?.GetValue<FPackageIndex>()?.Load<UBlueprintGeneratedClass>()?.ClassDefaultObject.Load();
									if (unlockNode is null) continue;

									UScriptArray? unlockRecipeList = unlockNode.Properties.FirstOrDefault(p => p.Name.Text.Equals("KeJiPeiFangSoftList"))?.Tag?.GetValue<UScriptArray>();
									if (unlockRecipeList is null) continue;

									foreach (FPropertyTagType recipe in unlockRecipeList.Properties)
									{
										UObject? unlockRecipe = recipe.GetValue<FSoftObjectPath>().Load<UBlueprintGeneratedClass>()?.ClassDefaultObject.Load();
										if (unlockRecipe is null) continue;

										FPackageIndex? unlockItem = unlockRecipe.Properties.FirstOrDefault(p => p.Name.Text.Equals("ProduceDaoJu"))?.Tag?.GetValue<FPackageIndex>();
										if (unlockItem is null) continue;

										unlocks.Add(unlockItem.Name);
									}
								}
							}
							break;
					}
				}
				if (tabletData.Icon is null)
				{
					logger.Warning($"Unable to find all data for tablet POI {className}");
					continue;
				}

				if (unlocks.Count > 0)
				{
					tabletData.Unlocks = $"[{string.Join(',', unlocks.Select(u => $"\"{u}\""))}]";
				}

				poiDatabase.Tablets.Add(className, tabletData);
			}

			return poiDatabase.Tablets.Any();
		}

		public static bool FindMapObjects(IProviderManager providerManager, Logger logger, MapLevelData mapLevelData, MapPoiDatabase poiDatabase,
			[NotNullWhen(true)] out IReadOnlyList<FObjectExport>? poiObjects,
			[NotNullWhen(true)] out IReadOnlyList<FObjectExport>? tabletObjects,
			[NotNullWhen(true)] out IReadOnlyList<FObjectExport>? respawnObjects,
			[NotNullWhen(true)] out IReadOnlyList<SpawnerObject>? spawnerObjects,
			[NotNullWhen(true)] out IReadOnlyList<FObjectExport>? barracksObjects,
			[NotNullWhen(true)] out IReadOnlyList<ObjectWithDefaults>? fireflyObjects,
			[NotNullWhen(true)] out IReadOnlyList<ObjectWithDefaults>? chestObjects,
			[NotNullWhen(true)] out IReadOnlyList<FObjectExport>? dungeonObjects,
			[NotNullWhen(true)] out IReadOnlyList<FObjectExport>? arenaObjects,
			[NotNullWhen(true)] out IReadOnlyList<FObjectExport>? gamefunctionObjects,
			[NotNullWhen(true)] out IReadOnlyList<FObjectExport>? minePlatformObjects,
			[NotNullWhen(true)] out IReadOnlyList<FObjectExport>? mineralVeinObjects,
			[NotNullWhen(true)] out IReadOnlyList<FObjectExport>? eventManagerObjects)
		{
			poiObjects = null;
			respawnObjects = null;
			tabletObjects = null;
			spawnerObjects = null;
			barracksObjects = null;
			fireflyObjects = null;
			chestObjects = null;
			dungeonObjects = null;
			arenaObjects = null;
			gamefunctionObjects = null;
			minePlatformObjects = null;
			mineralVeinObjects = null;
			eventManagerObjects = null;

			const string poiClass = "HVolumeChuFaQi";

			const string respawnClass = "HPlayerStart";

			string[] spawnerBaseClasses = new string[]
			{
				"HShuaGuaiQiBase", // Standard spawners
				"HShuaGuaiVolumeChuFaQi", // Triggers spawns when the player enters a volume
				"HTanChaActor" // Foootprint tracking event spawners for baby animals
			};

			List<GameClassInfo> spawnerBpClasses = new();
			foreach (String searchClass in spawnerBaseClasses)
			{
				spawnerBpClasses.AddRange(GameClassHierarchy.Instance.GetDerivedClasses(searchClass));
			}

			Dictionary<string, UObject?> spawnerClasses = spawnerBaseClasses.ToDictionary(c => c, c => (UObject?)null);
			foreach (GameClassInfo bpClass in spawnerBpClasses)
			{
				UBlueprintGeneratedClass? exportObj = (UBlueprintGeneratedClass?)bpClass.Export?.ExportObject.Value;
				FPropertyTag? scgClassProperty = exportObj?.ClassDefaultObject.Load()?.Properties.FirstOrDefault(p => p.Name.Text.Equals("SCGClass"));
				UObject? defaultScgObj = scgClassProperty?.Tag?.GetValue<FPackageIndex>()?.Load<UBlueprintGeneratedClass>()?.ClassDefaultObject.Load();
				spawnerClasses.Add(bpClass.Name, defaultScgObj);
			}

			const string barracksBaseClass = "HBuLuoGuanLiQi";

			HashSet<string> barracksClasses = new(GameClassHierarchy.Instance.GetDerivedBlueprintClasses(barracksBaseClass).Select(c => c.Name));

			const string fireflyBaseClass = "BP_CrowdNPC_Firefly_C";
			UObject fireflyDefaults = DataUtil.FindBlueprintDefaultsObject(providerManager.Provider, "WS/Content/Blueprints/CrowdNPC/BP_CrowdNPC_Firefly.uasset")!;

			const string chestBaseClass = "HJianZhuBaoXiang";

			List<BlueprintClassInfo> chestBpClasses = new(GameClassHierarchy.Instance.GetDerivedBlueprintClasses(chestBaseClass));

			Dictionary<string, UObject?> chestClasses = new();
			foreach (BlueprintClassInfo bpClass in chestBpClasses)
			{
				chestClasses.Add(bpClass.Name, DataUtil.FindBlueprintDefaultsObject(bpClass.Export));
			}

			const string arenaBaseClass = "HJianZhuJingJiChang";
			HashSet<string> arenaClasses = new(GameClassHierarchy.Instance.GetDerivedBlueprintClasses(arenaBaseClass).Select(c => c.Name));

			const string gameFunctionBaseClass = "HJianZhuGameFunction";
			HashSet<string> gameFunctionClasses = new(GameClassHierarchy.Instance.GetDerivedBlueprintClasses(gameFunctionBaseClass).Select(c => c.Name));

			const string minePlatformBaseClass = "HJianZhuKaiCaiPingTai";
			HashSet<string> minePlatformClasses = new(GameClassHierarchy.Instance.GetDerivedBlueprintClasses(minePlatformBaseClass).Select(c => c.Name));

			const string mineralVeinBaseClass = "HJianZhuKuangMai";
			HashSet<string> mineralVeinClasses = new(GameClassHierarchy.Instance.GetDerivedBlueprintClasses(mineralVeinBaseClass).Select(c => c.Name));

			const string eventManagerClass = "BP_SpecialTriggerEventManager_C";

			List<FObjectExport> poiObjectList = new();
			List<FObjectExport> respawnObjectList = new();
			List<FObjectExport> tabletObjectList = new();
			List<SpawnerObject> spawnerObjectList = new();
			List<FObjectExport> eventManagerObjectList = new();
			List<FObjectExport> barracksObjectList = new();
			List<ObjectWithDefaults> fireflyObjectList = new();
			List<ObjectWithDefaults> chestObjectList = new();
			List<FObjectExport> dungeonObjectList = new();
			List<FObjectExport> arenaObjectList = new();
			List<FObjectExport> gameFunctionObjectList = new();
			List<FObjectExport> minePlatformObjectList = new();
			List<FObjectExport> mineralVeinObjectList = new();

			logger.Information("Searching for objects...");

			foreach (Package package in mapLevelData.AllLevels)
			{
				logger.Debug(package.Name);
				foreach (FObjectExport export in package.ExportMap)
				{
					if (export.ClassName.Equals(poiClass))
					{
						poiObjectList.Add(export);
					}
					else if (spawnerClasses.TryGetValue(export.ClassName, out UObject? defaultScgObj))
					{
						spawnerObjectList.Add(new() { BaseClassName = export.ClassName, Object = new() { Export = export, DefaultsObject = defaultScgObj } });
					}
					else if (barracksClasses.Contains(export.ClassName))
					{
						barracksObjectList.Add(export);
					}
					else if (chestClasses.TryGetValue(export.ClassName, out UObject? defaultObj))
					{
						chestObjectList.Add(new() { Export = export, DefaultsObject = defaultObj });
					}
					else if (poiDatabase.Tablets.ContainsKey(export.ClassName))
					{
						tabletObjectList.Add(export);
					}
					else if (poiDatabase.DungeonMap.ContainsKey(export.ClassName))
					{
						dungeonObjectList.Add(export);
					}
					else if (gameFunctionClasses.Contains(export.ClassName))
					{
						gameFunctionObjectList.Add(export);
					}
					else if (mineralVeinClasses.Contains(export.ClassName))
					{
						mineralVeinObjectList.Add(export);
					}
					else if (export.ClassName.Equals(eventManagerClass))
					{
						eventManagerObjectList.Add(export);
					}
					else if (export.ClassName.Equals(respawnClass))
					{
						respawnObjectList.Add(export);
					}
					else if (arenaClasses.Contains(export.ClassName))
					{
						arenaObjectList.Add(export);
					}
					else if (minePlatformClasses.Contains(export.ClassName))
					{
						minePlatformObjectList.Add(export);
					}
					else if (export.ClassName.Equals(fireflyBaseClass))
					{
						fireflyObjectList.Add(new() { Export = export, DefaultsObject = fireflyDefaults });
					}
				}
			}

			poiObjects = poiObjectList;
			respawnObjects = respawnObjectList;
			tabletObjects = tabletObjectList;
			spawnerObjects = spawnerObjectList;
			barracksObjects = barracksObjectList;
			fireflyObjects = fireflyObjectList;
			chestObjects = chestObjectList;
			dungeonObjects = dungeonObjectList;
			arenaObjects = arenaObjectList;
			gamefunctionObjects = gameFunctionObjectList;
			minePlatformObjects = minePlatformObjectList;
			mineralVeinObjects = mineralVeinObjectList;
			eventManagerObjects = eventManagerObjectList;

			return true;
		}
	}

	internal struct SpawnerObject
	{
		public string BaseClassName;
		public ObjectWithDefaults Object;
	}
}
