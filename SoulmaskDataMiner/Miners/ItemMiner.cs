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

using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using SoulmaskDataMiner.Data;
using SoulmaskDataMiner.GameData;
using SoulmaskDataMiner.IO;

namespace SoulmaskDataMiner.Miners
{
	/// <summary>
	/// Gathers information about item classes
	/// </summary>
	internal class ItemMiner : SubclassMinerBase
	{
		public override string Name => "Item";

		protected override string NameProperty => "Name";

		protected override string? DescriptionProperty => "Description";

		protected override string? IconProperty => "Icon";

		private const string BaseClass_Item = "HDaoJuBase";

		protected override IReadOnlySet<string>? AdditionalPropertyNames => new HashSet<string>()
		{
			"CaiLiaoType",
			"CaiLiaoErJiType",
			"MaxAmount",
			"IsSpecialTestDaoJu",
			"Weight",
			"HiddenItemInGameMode" // TODO: Do something with this variable
		};

		public override bool Run(IProviderManager providerManager, Config config, Logger logger, ISqlWriter sqlWriter)
		{
			var categories = GetItemCategories(providerManager, logger);
			if (categories is null)
			{
				logger.Error("Unable to load item categories from resource manager");
				return false;
			}

			UTexture2D? testIcon = DataUtil.LoadFirstTexture(providerManager.Provider, "WS/Content/Characters/Mannequin/Character/Textures/T_UE4Logo_Mask.uasset", logger);
			if (testIcon is null)
			{
				logger.Error("Unable to load test icon texture");
				return false;
			}

			IEnumerable<ObjectInfo> itemInfos = FindObjects(BaseClass_Item.AsEnumerable());
			IEnumerable<ItemData> items = ReadItemData(itemInfos, categories, testIcon);

			WriteCsv(items, config, logger);
			WriteSql(items, sqlWriter, logger);
			WriteTextures(items, categories, config, logger);

			return true;
		}

		private IReadOnlyDictionary<EDaoJuCaiLiaoType, ItemCategoryData>? GetItemCategories(IProviderManager providerManager, Logger logger)
		{
			UScriptMap? typeInfoList = providerManager.SingletonManager.ResourceManager.Properties.FirstOrDefault(p => p.Name.Text.Equals("DaoJuCaiLiaoTypeInfo"))?.Tag?.GetValue<UScriptMap>();
			if (typeInfoList is null) return null;

			Dictionary<EDaoJuCaiLiaoType, ItemCategoryData> result = new();
			foreach (var pair in typeInfoList.Properties)
			{
				if (!DataUtil.TryParseEnum(pair.Key, out EDaoJuCaiLiaoType key))
				{
					key = EDaoJuCaiLiaoType.EDJCL_QiTa; // Other
				}

				ItemCategoryData value = new();
				FStructFallback valueObj = pair.Value!.GetValue<FStructFallback>()!;
				foreach (FPropertyTag property in valueObj.Properties)
				{
					switch (property.Name.Text)
					{
						case "CaiLiaoTypeText":
							value.Name = DataUtil.ReadTextProperty(property)!;
							break;
						case "CaiLiaoTypeIcon":
							value.Icon = DataUtil.ReadTextureProperty(property)!;
							break;
					}
				}

				if (value.Name is null || value.Icon is null)
				{
					logger.Warning("Missing item category data");
					continue;
				}

				result.Add(key, value);
			}

			return result;
		}

		private IEnumerable<ItemData> ReadItemData(IEnumerable<ObjectInfo> itemInfos, IReadOnlyDictionary<EDaoJuCaiLiaoType, ItemCategoryData> categories, UTexture2D testIcon)
		{
			foreach (var itemInfo in itemInfos)
			{
				EDaoJuCaiLiaoType categoryId = EDaoJuCaiLiaoType.EDJCL_QiTa;
				if (itemInfo.AdditionalProperties!.TryGetValue("CaiLiaoType", out FPropertyTag? categoryProp))
				{
					if (DataUtil.TryParseEnum(categoryProp, out EDaoJuCaiLiaoType result))
					{
						categoryId = result;
					}
				}

				if (categoryId == EDaoJuCaiLiaoType.EDJCL_QiTa)
				{
					if (itemInfo.AdditionalProperties!.TryGetValue("CaiLiaoErJiType", out FPropertyTag? secondaryCategoriesProp))
					{
						UScriptArray? secondaryCategoriesList = secondaryCategoriesProp.Tag?.GetValue<UScriptArray>();
						if (secondaryCategoriesList is not null)
						{
							FPropertyTagType? firstItem = secondaryCategoriesList.Properties.FirstOrDefault();
							if (firstItem is not null && DataUtil.TryParseEnum(firstItem, out EDaoJuCaiLiaoType result))
							{
								categoryId = result;
							}
						}
					}
				}

				int stackSize = 1;
				if (itemInfo.AdditionalProperties!.TryGetValue("MaxAmount", out FPropertyTag? stackSizeProp))
				{
					stackSize = stackSizeProp.Tag!.GetValue<int>();
				}

				bool isTestItem = false;
				if (itemInfo.AdditionalProperties!.TryGetValue("IsSpecialTestDaoJu", out FPropertyTag? isTestItemProp))
				{
					isTestItem = isTestItemProp.Tag!.GetValue<bool>();
				}

				float weight = 0.0f;
				if (itemInfo.AdditionalProperties!.TryGetValue("Weight", out FPropertyTag? weightProp))
				{
					weight = weightProp.Tag!.GetValue<float>();
				}

				int categoryIdInt = (int)categoryId;
				string categoryName = categories[categoryId].Name;
				UTexture2D categoryIcon = categories[categoryId].Icon;
				if (isTestItem)
				{
					categoryIdInt = 999;
					categoryName = "Test Items";
					categoryIcon = testIcon;
				}

				yield return new()
				{
					Info = itemInfo,
					CategoryID = categoryIdInt,
					CategoryName = categoryName,
					CategoryIcon = categoryIcon,
					StackSize = stackSize,
					Weight = weight
				};
			}
		}

		private void WriteCsv(IEnumerable<ItemData> items, Config config, Logger logger)
		{
			string outPath = Path.Combine(config.OutputDirectory, Name, $"{Name}.csv");
			using (FileStream outFile = IOUtil.CreateFile(outPath, logger))
			using (StreamWriter writer = new(outFile))
			{
				writer.WriteLine("name,class,desc,icon,stack,weight,cat,cat_name,cat_icon");
				foreach (ItemData item in items)
				{
					writer.WriteLine($"{CsvStr(item.Info.Name)},{CsvStr(item.Info.FullPath)},{CsvStr(item.Info.Description)},{item.Info.Icon?.Name},{item.StackSize},{item.Weight},{item.CategoryID},{CsvStr(item.CategoryName)},{item.CategoryIcon.Name}");
				}
			}
		}

		private void WriteSql(IEnumerable<ItemData> items, ISqlWriter sqlWriter, Logger logger)
		{
			// Schema
			// create table `item` (
			//   `name` varchar(255) not null,
			//   `class` varchar(255) not null,
			//   `path` varchar(511) not null,
			//   `desc` varchar(511),
			//   `icon` varchar(255),
			//   `stack` int not null,
			//   `weight` float not null,
			//   `cat` int not null,
			//   `cat_name` varchar(63) not null,
			//   `cat_icon` varchar(63)
			// )

			sqlWriter.WriteStartTable("item");
			foreach (ItemData item in items)
			{
				sqlWriter.WriteRow($"{DbStr(item.Info.Name, true)}, {DbStr(item.Info.ClassName)}, {DbStr(item.Info.FullPath)}, {DbStr(item.Info.Description)}, {DbStr(item.Info.Icon?.Name)}, {item.StackSize}, {item.Weight}, {item.CategoryID}, {DbStr(item.CategoryName)}, {DbStr(item.CategoryIcon.Name)}");
			}
			sqlWriter.WriteEndTable();
		}

		private void WriteTextures(IEnumerable<ItemData> items, IReadOnlyDictionary<EDaoJuCaiLiaoType, ItemCategoryData> categories, Config config, Logger logger)
		{
			string outRoot = Path.Combine(config.OutputDirectory, Name, "icons");

			string outDir = Path.Combine(outRoot, "item");
			foreach (ItemData item in items)
			{
				if (item.Info.Icon is null) continue;
				TextureExporter.ExportTexture(item.Info.Icon!, false, logger, outDir);
			}

			outDir = Path.Combine(outRoot, "item_cat");
			foreach (ItemCategoryData category in categories.Values)
			{
				TextureExporter.ExportTexture(category.Icon, false, logger, outDir);
			}
		}

		private struct ItemCategoryData
		{
			public string Name;
			public UTexture2D Icon;
		}

		private struct ItemData
		{
			public ObjectInfo Info;
			public int StackSize;
			public float Weight;
			public int CategoryID;
			public string CategoryName;
			public UTexture2D CategoryIcon;
		}
	}
}
