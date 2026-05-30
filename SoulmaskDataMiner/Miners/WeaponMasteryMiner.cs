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
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using SoulmaskDataMiner.Data;
using SoulmaskDataMiner.GameData;
using SoulmaskDataMiner.IO;
using System.Diagnostics.CodeAnalysis;

namespace SoulmaskDataMiner.Miners
{
	/// <summary>
	/// Mines data related to weapon masteries (ZhuanJing)
	/// </summary>
	[MinerName("Mastery")]
	internal class WeaponMasteryMiner : MinerBase
	{
		// Row type for the flat output: one entry per (weapon type, index-in-type, data).
		private readonly record struct Row(EWuQiLeiXing Type, int Idx, MasteryData Data);

		// Schema
		// create table `zj` (
		//   `type` int not null,
		//   `idx` int not null,
		//   `id` int not null,
		//   `name` varchar(127) not null,
		//   `desc` varchar(511),
		//   `start` bool not null,
		//   `c30` float not null, `c60` float not null, `c90` float not null, `c120` float not null,
		//   `icon` varchar(127),
		//   primary key (`type`, `idx`)
		// )
		private static readonly MinerTable<Row> sTable = new(
			csvFileName: "Mastery.csv",
			sqlTableName: "zj",
			columns:
			[
				TableColumn.Int<Row>("type", r => (int)r.Type),
				TableColumn.Int<Row>("idx", r => r.Idx),
				TableColumn.Int<Row>("id", r => r.Data.ID),
				TableColumn.Str<Row>("name", r => r.Data.Name),
				TableColumn.Str<Row>("desc", r => r.Data.Description),
				TableColumn.Bool<Row>("start", r => r.Data.IsStartingAbility),
				TableColumn.Float<Row>("c30", r => r.Data.Chance30),
				TableColumn.Float<Row>("c60", r => r.Data.Chance60),
				TableColumn.Float<Row>("c90", r => r.Data.Chance90),
				TableColumn.Float<Row>("c120", r => r.Data.Chance120),
				TableColumn.Str<Row>("icon", r => r.Data.Icon?.Name),
			],
			iconSelector: r => r.Data.Icon);

		public override bool Run(IProviderManager providerManager, Config config, Logger logger, ISqlWriter sqlWriter)
		{
			IReadOnlyDictionary<EWuQiLeiXing, List<MasteryData>>? masteries;
			if (!LoadMasteryData(providerManager, logger, out masteries))
			{
				return false;
			}

			IEnumerable<Row> rows = masteries.SelectMany(p =>
				p.Value.Select((data, i) => new Row(p.Key, i, data)));
			WriteTable(rows, sTable, config, logger, sqlWriter);
			return true;
		}

		private bool LoadMasteryData(IProviderManager providerManager, Logger logger, [NotNullWhen(true)] out IReadOnlyDictionary<EWuQiLeiXing, List<MasteryData>>? masteries)
		{
			UDataTable? masteryTable = null;
			List<FPropertyTagType>? startMasteryArray = null;
			foreach (FPropertyTag prop in providerManager.SingletonManager.ResourceManager.Properties)
			{
				switch (prop.Name.Text)
				{
					case "ZhuanJingArray":
						masteryTable = prop.Tag?.GetValue<FPackageIndex>()?.Load<UDataTable>();
						break;
					case "ZhuanJingAbilitySets":
						startMasteryArray = prop.Tag!.GetValue<UScriptArray>()!.Properties;
						break;
				}
			}

			if (masteryTable is null || startMasteryArray is null)
			{
				logger.Error("Unable to locate ZhuanJingArray or ZhuanJingAbilitySets in BP_ZiYuanGuanLiQi");
				masteries = null;
				return false;
			}

			HashSet<int> startingAbilities = new();
			foreach (IntProperty property in startMasteryArray)
			{
				startingAbilities.Add(property.Value);
			}

			IReadOnlyDictionary<EWuQiLeiXing, Dictionary<int, Dictionary<int, float>>>? masteryAcquireMap = GetMasteryAcquireMap(providerManager, logger);
			if (masteryAcquireMap is null)
			{
				masteries = null;
				return false;
			}

			Dictionary<EWuQiLeiXing, List<MasteryData>> masteryMap = new();
			foreach (FStructFallback masteryRow in masteryTable.RowMap.Values)
			{
				EWuQiLeiXing weaponType = EWuQiLeiXing.WUQI_LEIXING_NONE;
				MasteryData data = new();

				foreach (FPropertyTag property in masteryRow.Properties)
				{
					switch (property.Name.Text)
					{
						case "JiNengIndex":
							data.ID = property.Tag!.GetValue<int>();
							break;
						case "UseWuQiLeiXing":
							if (DataUtil.TryParseEnum<EWuQiLeiXing>(property, out EWuQiLeiXing wt))
							{
								weaponType = wt;
							}
							break;
						case "ZJJN":
							{
								List<FPropertyTag> zjjnProperties = property.Tag!.GetValue<FStructFallback>()!.Properties;
								foreach (FPropertyTag zjjnProperty in zjjnProperties)
								{
									if (zjjnProperty.Name.Text.Equals("Ability"))
									{
										UBlueprintGeneratedClass? abilityClass = zjjnProperty.Tag?.GetValue<FPackageIndex>()?.ResolvedObject?.Object?.Value as UBlueprintGeneratedClass;
										UObject? ability = abilityClass?.ClassDefaultObject.ResolvedObject?.Object?.Value;
										if (ability is null)
										{
											logger.Warning("Failed to load ability blueprint for mastery.");
											break;
										}
										ParseAbility(ability, ref data);
									}
								}
							}
							break;
					}
				}

				if (weaponType == EWuQiLeiXing.WUQI_LEIXING_NONE || data.Name is null)
				{
					logger.Warning("Missing data for mastery. It will be skipped.");
					continue;
				}

				data.IsStartingAbility = startingAbilities.Contains(data.ID);
				Dictionary<int, float>? acquireChances;
				if (masteryAcquireMap[weaponType].TryGetValue(data.ID, out acquireChances))
				{
					foreach (var pair in acquireChances)
					{
						switch (pair.Key)
						{
							case 30:
								data.Chance30 = pair.Value;
								break;
							case 60:
								data.Chance60 = pair.Value;
								break;
							case 90:
								data.Chance90 = pair.Value;
								break;
							case 120:
								data.Chance120 = pair.Value;
								break;
						}
					}
				}

				List<MasteryData>? list;
				if (!masteryMap.TryGetValue(weaponType, out list))
				{
					list = new();
					masteryMap.Add(weaponType, list);
				}

				list.Add(data);
			}

			masteries = masteryMap;
			return true;
		}

		private IReadOnlyDictionary<EWuQiLeiXing, Dictionary<int, Dictionary<int, float>>>? GetMasteryAcquireMap(IProviderManager providerManager, Logger logger)
		{
			if (!providerManager.Provider.TryGetGameFile("WS/Content/Blueprints/ZiYuanGuanLi/DT_ZhuanJingSLD.uasset", out GameFile? file2))
			{
				logger.Error("Unable to locate asset DT_ZhuanJingSLD.");
				return null;
			}
			Package package2 = (Package)providerManager.Provider.LoadPackage(file2);

			UDataTable? abilitySetTable = package2.ExportMap[0].ExportObject.Value as UDataTable;
			if (abilitySetTable is null)
			{
				logger.Error("Error loading DT_ZhuanJingSLD");
				return null;
			}

			Dictionary<EWuQiLeiXing, Dictionary<int, Dictionary<int, float>>> masteryAcquireMap = new();
			foreach (var row in abilitySetTable.RowMap)
			{
				EWuQiLeiXing masteryType = EWuQiLeiXing.WUQI_LEIXING_NONE;
				Dictionary<FPropertyTagType, FPropertyTagType?>? acquireLevelMap = null;
				foreach (FPropertyTag property in row.Value.Properties)
				{
					switch (property.Name.Text)
					{
						case "UseWuQiLeiXing":
							DataUtil.TryParseEnum<EWuQiLeiXing>(property, out masteryType);
							break;
						case "SLDGaiLv":
							acquireLevelMap = property.Tag!.GetValue<UScriptMap>()!.Properties;
							break;
					}
				}
				if (masteryType == EWuQiLeiXing.WUQI_LEIXING_NONE || acquireLevelMap is null)
				{
					logger.Warning("Failed to load some mastery level acquisition data.");
					continue;
				}

				Dictionary<int, Dictionary<int, float>>? mapForType;
				if (!masteryAcquireMap.TryGetValue(masteryType, out mapForType))
				{
					mapForType = new();
					masteryAcquireMap.Add(masteryType, mapForType);
				}

				foreach (var pair in acquireLevelMap)
				{
					int level = pair.Key.GetValue<int>();
					FStructFallback value = pair.Value!.GetValue<FStructFallback>()!;
					Dictionary<FPropertyTagType, FPropertyTagType?>? chanceMap = value.Properties.FirstOrDefault(p => p.Name.Text.Equals("JiNengChi"))?.Tag?.GetValue<UScriptMap>()?.Properties;
					if (chanceMap is null)
					{
						logger.Warning("Failed to load some mastery level acquisition data.");
						continue;
					}

					float totalweight = 0;
					List<Tuple<int, int>> chanceList = new();
					foreach (var chancePair in chanceMap)
					{
						int masteryId = chancePair.Key.GetValue<int>();
						int weight = chancePair.Value!.GetValue<int>();

						chanceList.Add(new(masteryId, weight));
						totalweight += weight;
					}

					foreach (var tuple in chanceList)
					{
						Dictionary<int, float>? mapForId;
						if (!mapForType.TryGetValue(tuple.Item1, out mapForId))
						{
							mapForId = new();
							mapForType.Add(tuple.Item1, mapForId);
						}

						mapForId.Add(level, (float)tuple.Item2 / totalweight);
					}
				}
			}

			return masteryAcquireMap;
		}

		private static void ParseAbility(UObject ability, ref MasteryData mastery)
		{
			List<FPropertyTag> properties = ability.Properties;
			foreach (FPropertyTag property in properties)
			{
				switch (property.Name.Text)
				{
					case "AbilityIcon":
						mastery.Icon = DataUtil.ReadTextureProperty(property);
						break;
					case "AbilityName":
						mastery.Name = DataUtil.ReadTextProperty(property);
						break;
					case "JinengMiaoshu":
						mastery.Description = DataUtil.ReadTextProperty(property);
						break;
				}
			}
		}

		private struct MasteryData
		{
			public int ID;
			public string? Name;
			public string? Description;
			public UTexture2D? Icon;
			public bool IsStartingAbility;
			public float Chance30;
			public float Chance60;
			public float Chance90;
			public float Chance120;

			public override string ToString()
			{
				return $"[{ID}] {Name}";
			}
		}
	}
}
