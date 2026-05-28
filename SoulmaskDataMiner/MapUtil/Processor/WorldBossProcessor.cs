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
using CUE4Parse.UE4.Assets.Exports.Engine;
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
	/// Map point of interest processor for world boss locations
	/// </summary>
	internal class WorldBossProcessor : ProcessorBase
	{
		public WorldBossProcessor(MapData mapData)
			: base(mapData)
		{
		}

		public void Process(MapPoiDatabase poiDatabase, IReadOnlyList<FObjectExport> gameFunctionObjects, Logger logger)
		{
			logger.Information("Processing world bosses...");

			foreach (FObjectExport export in gameFunctionObjects)
			{
				UObject obj = export.ExportObject.Value;

				UBlueprintGeneratedClass? objClass = export.ClassIndex.Load<UBlueprintGeneratedClass>();
				UObject? defaultsObj = objClass?.ClassDefaultObject.Load();
				if (defaultsObj is null) continue;

				UScriptMap? functionMap = defaultsObj.Properties.FirstOrDefault(p => p.Name.Text.Equals("GameFunctionExecutionMap"))?.Tag?.GetValue<UScriptMap>();
				if (functionMap is null || functionMap.Properties.Count == 0) continue;

				string? bossName = null;
				List<BossData> bosses = new();
				foreach (var pair in functionMap.Properties)
				{
					EJianZhuGameFunctionType funcType = EJianZhuGameFunctionType.EJZGFT_NOT_DEFINE;
					FPackageIndex? npcIndex = null;

					UScriptArray? execList = pair.Value?.GetValue<FStructFallback>()?.Properties[0].Tag?.GetValue<UScriptArray>();
					if (execList is null || execList.Properties.Count == 0) continue;

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
							case "ExecuteActorClass":
								npcIndex = property.Tag?.GetValue<FPackageIndex>();
								break;
						}
					}

					if (funcType != EJianZhuGameFunctionType.EJZGFT_SUMMON_NPC)
					{
						continue;
					}

					if (npcIndex is null)
					{
						logger.Warning($"Boss summon function in class {export.ObjectName} is missing an NPC class.");
						continue;
					}

					UBlueprintGeneratedClass npcClass = npcIndex.Load<UBlueprintGeneratedClass>()!;

					string? npcName = null;
					List<FPackageIndex> growthComponentIndices = new();
					GameClassHierarchy.SearchInheritance(npcClass, (current =>
					{
						UObject? npcObj = current?.ClassDefaultObject.Load();
						if (npcObj is null)
						{
							return false;
						}

						foreach (FPropertyTag property in npcObj.Properties)
						{
							switch (property.Name.Text)
							{
								case "MoRenMingZi":
									if (npcName is null)
									{
										npcName = DataUtil.ReadTextProperty(property);
									}
									break;
								case "ChengZhangComponent":
									{
										FPackageIndex? growthComponentIndex = property.Tag?.GetValue<FPackageIndex>();
										if (growthComponentIndex is not null)
										{
											growthComponentIndices.Add(growthComponentIndex);
										}
									}
									break;
							}
						}

						return false;
					}));

					if (npcName is null)
					{
						logger.Warning($"Boss defined by class {npcClass.Name} is missing a name.");
						continue;
					}

					if (growthComponentIndices.Count == 0)
					{
						logger.Warning($"Boss defined by class {npcClass.Name} is missing a growth component.");
						continue;
					}

					if (bossName is null)
					{
						bossName = npcName.Substring(npcName.IndexOf(" ") + 1);
					}

					int level = 0;
					UDataTable? statTable = null;
					foreach (FPackageIndex growthComponentIndex in growthComponentIndices)
					{
						UObject growthComponent = growthComponentIndex.Load()!;

						foreach (FPropertyTag property in growthComponent.Properties)
						{
							switch (property.Name.Text)
							{
								case "AttrMetaDataDT":
									if (statTable is null)
									{
										statTable = property.Tag?.GetValue<FPackageIndex>()?.Load<UDataTable>();
									}
									break;
								case "NeedLevel":
									if (level == 0)
									{
										level = property.Tag!.GetValue<int>();
									}
									break;
							}
						}
					}

					if (level == 0 || statTable is null)
					{
						logger.Warning($"Boss defined by class {npcClass.Name} is missing growth data.");
						continue;
					}

					int maxHealthBase = (int)statTable.RowMap.FirstOrDefault(r => r.Key.Text.Equals("HSuperCommonSet.MaxHealth")).Value.Properties.FirstOrDefault(p => p.Name.Text.Equals("BaseValue"))!.Tag!.GetValue<float>();
					int maxHealth = maxHealthBase + (int)((level - 1) * (maxHealthBase * 0.2f));

					UBlueprintGeneratedClass recipeClass = pair.Key.GetValue<FPackageIndex>()!.Load<UBlueprintGeneratedClass>()!;
					UObject recipeObj = recipeClass.ClassDefaultObject.Load()!;

					UTexture2D? recipeIcon = null;
					int requiredLevel = 0;
					UScriptArray? recipeItemArray = null;
					int maskEnergyCost = 0;
					foreach (FPropertyTag property in recipeObj.Properties)
					{
						switch (property.Name.Text)
						{
							case "PeiFangIcon":
								recipeIcon = DataUtil.ReadTextureProperty(property);
								break;
							case "PeiFangDengJi":
								requiredLevel = property.Tag!.GetValue<int>();
								break;
							case "DemandDaoJu":
								recipeItemArray = property.Tag?.GetValue<UScriptArray>();
								break;
							case "DemandMianJuNengLiang":
								maskEnergyCost = property.Tag!.GetValue<int>();
								break;
						}
					}

					List<RecipeComponent> recipeItems = new();

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
								recipeItems.Add(new() { ItemClass = componentItem.GetValue<FPackageIndex>()!.Name, Count = count });
							}
						}
					}

					string summonRecipe = "{}";
					if (recipeItems.Count > 0 || maskEnergyCost > 0 || requiredLevel > 0)
					{
						StringBuilder builder = new("{");

						builder.Append("\"items\":[");
						foreach (RecipeComponent item in recipeItems)
						{
							builder.Append($"{{\"i\":\"{item.ItemClass}\",\"c\":{item.Count}}},");
						}
						builder.Length -= 1; // Remove trailing comma
						builder.Append("]");

						builder.Append($",\"level\":{requiredLevel}");
						builder.Append($",\"mask\":{maskEnergyCost}");

						builder.Append("}");

						summonRecipe = builder.ToString();
					}

					string loot = "{}";
					{
						CollectionData? collectionData = null;
						GameClassHierarchy.SearchInheritance(npcClass, (current) =>
						{
							if (poiDatabase.StaticData.Loot.CollectionMap.TryGetValue(current.Name, out CollectionData value))
							{
								collectionData = value;
								return true;
							}
							return false;
						});

						if (collectionData.HasValue)
						{
							StringBuilder collectMapBuilder = new("{");

							if (collectionData.Value.Hit is not null) collectMapBuilder.Append($"\"base\":\"{collectionData.Value.Hit}\",");
							if (collectionData.Value.FinalHit is not null) collectMapBuilder.Append($"\"bonus\":\"{collectionData.Value.FinalHit}\",");

							collectMapBuilder.Append($"\"amount\":{collectionData.Value.Amount}");
							collectMapBuilder.Append("}");

							loot = collectMapBuilder.ToString();
						}
					}

					bosses.Add(new() { Name = npcName, Level = level, MaxHealth = maxHealth, SummonRecipe = summonRecipe, Loot = loot, Icon = recipeIcon });

					if (recipeIcon is not null) poiDatabase.AdditionalIconsToExport.Add(recipeIcon);
				}

				if (bosses.Count == 0) continue;

				UObject? rootComponent = obj.Properties.FirstOrDefault(p => p.Name.Text.Equals("RootComponent"))?.Tag?.GetValue<FPackageIndex>()?.Load();
				FPropertyTag? locationProperty = rootComponent?.Properties.FirstOrDefault(p => p.Name.Text.Equals("RelativeLocation"));
				if (locationProperty is null)
				{
					logger.Warning($"Failed to find location for world boss: {bossName}");
					continue;
				}
				FVector location = locationProperty.Tag!.GetValue<FVector>();

				string bossData;
				{
					StringBuilder builder = new("[");
					foreach (BossData boss in bosses)
					{
						builder.Append("{");
						builder.Append($"\"name\":\"{boss.Name}\"");
						builder.Append($",\"level\":{boss.Level}");
						builder.Append($",\"health\":{boss.MaxHealth}");
						builder.Append($",\"icon\":{(boss.Icon?.Name is null ? "null" : $"\"{boss.Icon.Name}\"")}");
						builder.Append($",\"summon\":{boss.SummonRecipe}");
						builder.Append($",\"loot\":{boss.Loot}");
						builder.Append("},");
					}
					builder.Length -= 1; // Remove trailing comma
					builder.Append("]");

					bossData = builder.ToString();
				}

				MapPoi poi = new()
				{
					GroupIndex = SpawnLayerGroup.PointOfInterest,
					Type = "Boss Altar",
					Title = bossName,
					Name = "World boss summoning altar",
					BossInfo = bossData,
					Location = location,
					MapLocation = WorldToMap(location),
					Icon = poiDatabase.StaticData.BossIcon
				};

				poiDatabase.WorldBosses.Add(poi);
			}
		}

		private struct BossData
		{
			public string Name;
			public int Level;
			public int MaxHealth;
			public string SummonRecipe;
			public string Loot;
			public UTexture2D? Icon;
		}
	}
}
