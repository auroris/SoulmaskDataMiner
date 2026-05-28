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

using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Component;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.UObject;
using SoulmaskDataMiner.Data;
using SoulmaskDataMiner.GameData;

namespace SoulmaskDataMiner.MapUtil.Processor
{
	/// <summary>
	/// Map point of interest processor for mineral vein locations
	/// </summary>
	internal class MineralVeinProcessor : ProcessorBase
	{
		public MineralVeinProcessor(MapData mapData)
			: base(mapData)
		{
		}

		public void Process(MapPoiDatabase poiDatabase, IReadOnlyList<FObjectExport> mineralVeinObjects, IProviderManager providerManager, Logger logger)
		{
			logger.Information($"Processing {mineralVeinObjects.Count} mineral veins...");

			Dictionary<EKuangMaiType, UTexture2D?> mineralIconMap = new();

			foreach (FObjectExport mineralVeinObject in mineralVeinObjects)
			{
				string? lootId = null;
				float interval = 0.0f;
				EKuangMaiType mineralType = EKuangMaiType.KMT_None;
				float lowerBound = -1.0f;
				float upperBound = -1.0f;
				USceneComponent? rootComponent = null;

				float? getStructValue(FStructFallback? strct, string propertyName)
				{
					FPropertyTag? property = strct?.Properties.FirstOrDefault(p => p.Name.Text.Equals(propertyName));
					FStructFallback? innerStruct = property?.Tag?.GetValue<FStructFallback>();
					FPropertyTag? valueProperty = innerStruct?.Properties.FirstOrDefault(p => p.Name.Text.Equals("Value"));
					if (valueProperty?.Tag is not null)
					{
						return valueProperty.Tag.GetValue<float>();
					}
					return null;
				}

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
							case "KuangMaiType":
								if (mineralType == EKuangMaiType.KMT_None)
								{
									if (DataUtil.TryParseEnum(property, out EKuangMaiType type))
									{
										mineralType = type;
									}
								}
								break;
							case "HanLiangData":
								{
									FStructFallback? dataStruct = property.Tag?.GetValue<FStructFallback>();
									FPropertyTag? rangeProperty = dataStruct?.Properties.FirstOrDefault(p => p.Name.Text.Equals("HanLiangRange"));
									FStructFallback? rangeStruct = rangeProperty?.Tag?.GetValue<FStructFallback>();
									if (lowerBound == -1.0f)
									{
										float? value = getStructValue(rangeStruct, "LowerBound");
										if (value.HasValue)
										{
											lowerBound = value.Value;
										}
									}
									if (upperBound == -1.0f)
									{
										float? value = getStructValue(rangeStruct, "UpperBound");
										if (value.HasValue)
										{
											upperBound = value.Value;
										}
									}
								}
								break;
						}
					}
				}

				UObject worldObj = mineralVeinObject.ExportObject.Value;
				searchObj(worldObj);

				GameClassHierarchy.SearchInheritance((UClass)worldObj.Class!.Load()!, (current) =>
				{
					UObject? curObj = current.ClassDefaultObject.Load();
					if (curObj is null) return false;

					searchObj(curObj);

					return lootId is not null && interval != 0.0f && rootComponent is not null;
				});

				if (lootId is null || interval == 0.0f || mineralType == EKuangMaiType.KMT_None || lowerBound == -1.0f || upperBound == -1.0f || rootComponent is null)
				{
					logger.Warning("Mineral vein properties not found");
					continue;
				}

				FPropertyTag? locationProperty = rootComponent?.Properties.FirstOrDefault(p => p.Name.Text.Equals("RelativeLocation"));
				if (locationProperty is null)
				{
					logger.Warning("Failed to locate mineral vein");
					continue;
				}

				FVector location = locationProperty.Tag!.GetValue<FVector>();

				if (!poiDatabase.StaticData.Loot.LootMap.TryGetValue(lootId, out LootTable? lootTable))
				{
					logger.Warning($"Failed to locate mineral vein content type '{lootId}'");
					continue;
				}

				string name = $"{mineralType.ToEn()} Vein";

				UTexture2D? icon;
				if (!mineralIconMap.TryGetValue(mineralType, out icon))
				{
					switch (mineralType)
					{
						case EKuangMaiType.KMT_TongKuang:
							icon = LoadItemIcon("WS/Content/Blueprints/DaoJu/DaojuCaiLiao/Kuangshi/Daoju_Item_CopperOre.uasset", providerManager, logger);
							break;
						case EKuangMaiType.KMT_XiKuang:
							icon = LoadItemIcon("WS/Content/Blueprints/DaoJu/DaojuCaiLiao/Kuangshi/Daoju_Item_TinOre.uasset", providerManager, logger);
							break;
						case EKuangMaiType.KMT_LiuKuang:
							icon = LoadItemIcon("WS/Content/Blueprints/DaoJu/DaojuCaiLiao/Kuangshi/Daoju_Item_SulfurOre.uasset", providerManager, logger);
							break;
						case EKuangMaiType.KMT_LinKuang:
							icon = LoadItemIcon("WS/Content/Blueprints/DaoJu/DaojuCaiLiao/Kuangshi/Daoju_Item_PhosphateOre.uasset", providerManager, logger);
							break;
						case EKuangMaiType.KMT_YanKuang:
							icon = LoadItemIcon("WS/Content/Blueprints/DaoJu/DaojuCaiLiao/Kuangshi/Daoju_Item_Stone.uasset", providerManager, logger);
							break;
						case EKuangMaiType.KMT_MeiKuang:
							icon = LoadItemIcon("WS/Content/Blueprints/DaoJu/DaojuCaiLiao/Kuangshi/Daoju_Item_CoalOre.uasset", providerManager, logger);
							break;
						case EKuangMaiType.KMT_TieKuang:
							icon = LoadItemIcon("WS/Content/Blueprints/DaoJu/DaojuCaiLiao/Kuangshi/Daoju_Item_IronOre.uasset", providerManager, logger);
							break;
						case EKuangMaiType.KMT_XiaoShi:
							icon = LoadItemIcon("WS/Content/Blueprints/DaoJu/DaojuCaiLiao/Kuangshi/Daoju_Item_Nitre.uasset", providerManager, logger);
							break;
						case EKuangMaiType.KMT_ShuiJing:
							icon = LoadItemIcon("WS/Content/Blueprints/DaoJu/DaojuCaiLiao/Kuangshi/Daoju_Item_Crystal.uasset", providerManager, logger);
							break;
					}

					mineralIconMap.Add(mineralType, icon);
				}
				if (icon is null)
				{
					logger.Warning($"Unable to find icon for mineral vein content type {lootId}");
					continue;
				}

				MapPoi poi = new()
				{
					GroupIndex = SpawnLayerGroup.MineralVein,
					Type = name,
					Title = name,
					Name = $"Grade: {lowerBound}-{upperBound}",
					Description = $"Interval: {interval} seconds",
					LootId = lootId,
					Location = location,
					MapLocation = WorldToMap(location),
					Icon = icon
				};

				poiDatabase.MineralVeins.Add(poi);
			}
		}

		private UTexture2D? LoadItemIcon(string assetPath, IProviderManager providerManager, Logger logger)
		{
			if (!providerManager.Provider.TryGetGameFile(assetPath, out GameFile? file))
			{
				logger.Warning($"Unable to find {assetPath}");
				return null;
			}
			Package package = (Package)providerManager.Provider.LoadPackage(file);

			UObject? itemObject = DataUtil.FindBlueprintDefaultsObject(package);
			if (itemObject is null)
			{
				logger.Warning($"Unable to load {assetPath}");
				return null;
			}

			FPropertyTag? iconProperty = itemObject.Properties.FirstOrDefault(p => p.Name.Text.Equals("Icon"));
			if (iconProperty is null)
			{
				logger.Warning($"Unable to find Icon property in {assetPath}");
				return null;
			}
			return DataUtil.ReadTextureProperty(iconProperty);
		}
	}
}
