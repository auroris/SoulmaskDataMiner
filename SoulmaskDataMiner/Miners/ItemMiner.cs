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
	[MinerName("Item"), RequiredBaseClasses("HDaoJuBase")]
	internal class ItemMiner : BlueprintScanMinerBase
	{
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
		private static readonly MinerTable<ItemData> sTable = new(
			csvFileName: "Item.csv",
			sqlTableName: "item",
			columns:
			[
				TableColumn.Str<ItemData>("name", i => i.Info.Name, treatNullAsEmpty: true),
				TableColumn.Str<ItemData>("class", i => i.Info.ClassName),
				TableColumn.Str<ItemData>("path", i => i.Info.FullPath),
				TableColumn.Str<ItemData>("desc", i => i.Info.Description),
				TableColumn.Str<ItemData>("icon", i => i.Info.Icon?.Name),
				TableColumn.Int<ItemData>("stack", i => i.StackSize),
				TableColumn.Float<ItemData>("weight", i => i.Weight),
				TableColumn.Int<ItemData>("cat", i => i.CategoryID),
				TableColumn.Str<ItemData>("cat_name", i => i.CategoryName),
				TableColumn.Str<ItemData>("cat_icon", i => i.CategoryIcon.Name),
			],
			iconSelector: i => i.Info.Icon)
		{
			IconSubdir = Path.Combine("icons", "item"),
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
			List<ItemData> items = ReadItemData(itemInfos, categories, testIcon).ToList();

			WriteTable(items, sTable, config, logger, sqlWriter);
			// Category icons live in a separate subdir and come from the category lookup,
			// not per-row, so they get their own WriteIcons call.
			WriteIcons(categories.Values, c => c.Icon, config, logger, Path.Combine("icons", "item_cat"));
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
