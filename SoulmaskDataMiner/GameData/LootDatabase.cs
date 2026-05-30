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

using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.UObject;
using Org.BouncyCastle.Bcpg;
using SoulmaskDataMiner.Data;
using SoulmaskDataMiner.IO;
using System.Diagnostics;
using System.Text;

namespace SoulmaskDataMiner.GameData
{
	/// <summary>
	/// Utility for gather loot table data
	/// </summary>
	internal class LootDatabase
	{
		private bool mIsLoaded;

		private readonly Dictionary<string, LootTable> mLootMap;
		private readonly Dictionary<string, CollectionData> mCollectionMap;

		/// <summary>
		/// Map of loot table keys to loot table data
		/// </summary>
		public IReadOnlyDictionary<string, LootTable> LootMap => mLootMap;

		/// <summary>
		/// Map of class names to collection data
		/// </summary>
		public IReadOnlyDictionary<string, CollectionData> CollectionMap => mCollectionMap;

		public LootDatabase()
		{
			mIsLoaded = false;
			mLootMap = new();
			mCollectionMap = new();
		}

		/// <summary>
		/// Loads data for this instance
		/// </summary>
		public bool Load(IFileProvider provider, Logger logger)
		{
			if (mIsLoaded) return true;

			logger.Information("Loading loot database..");

			Stopwatch timer = new Stopwatch();
			timer.Start();

			if (!LoadLootData(provider, logger)) return false;
			if (!LoadCollectionData(provider, logger)) return false;

			ResolveDependencies(logger);

			timer.Stop();

			logger.Information($"Loot database load completed in {timer.ElapsedTicks / (double)Stopwatch.Frequency * 1000.0:0.##}ms");

			mIsLoaded = true;
			return true;
		}

		/// <summary>
		/// Saves loot data to output directory
		/// </summary>
		/// <param name="sqlWriter">For writing sql data</param>
		/// <param name="config">For obtaining an directory for csv output</param>
		/// <param name="logger">For logging messages</param>
		public void SaveData(ISqlWriter sqlWriter, Config config, Logger logger)
		{
			logger.Information("Saving loot data...");
			WriteCsvLoot(config, logger);
			WriteSqlLoot(sqlWriter, logger);
		}

		private bool LoadLootData(IFileProvider provider, Logger logger)
		{
			foreach (var filePair in provider.Files)
			{
				if (!filePair.Key.StartsWith("WS/Content/Blueprints/DataTable/CaiJiBao") && !filePair.Key.StartsWith("WS/Content/AdditionMap01/BluePrints/DataTable/Drop")) continue;
				if (!filePair.Key.EndsWith(".uasset")) continue;

				if (!provider.TryLoadPackage(filePair.Value, out IPackage? iPackage)) continue;

				Package? package = iPackage as Package;
				if (package is null) continue;

				UDataTable? table = package.ExportMap[0].ExportObject.Value as UDataTable;
				if (table is null) continue;

				FPropertyTag? rowStructProperty = table.Properties.FirstOrDefault(p => p.Name.Text.Equals("RowStruct"));
				if (rowStructProperty is null) continue;

				FPackageIndex pi = rowStructProperty.Tag!.GetValue<FPackageIndex>()!;
				if (!pi.Name.Equals("CaiJiDaoJuBaoDataTable")) continue;

				foreach (var pair in table.RowMap)
				{
					string? name = null;
					UScriptArray? contentArray = null;
					UScriptArray? extraContentArray = null;

					foreach (FPropertyTag property in pair.Value.Properties)
					{
						switch (property.Name.Text)
						{
							case "DaoJuBaoName":
								name = property.Tag!.GetValue<FName>().Text;
								break;
							case "DaoJuBaoContent":
								contentArray = property.Tag!.GetValue<UScriptArray>();
								break;
							case "ExtraDropContentData":
								extraContentArray = property.Tag!.GetValue<UScriptArray>();
								break;
						}
					}

					if (name is null || contentArray is null)
					{
						logger.Warning($"Could not read loot data from {Path.GetFileNameWithoutExtension(filePair.Key)} row \"{pair.Key.Text}\"");
						continue;
					}

					LootTable content = new();
					foreach (FPropertyTagType contentItem in contentArray.Properties)
					{
						int probability = 0;
						UScriptArray? conditionArray = null;
						UScriptArray? itemArray = null;

						FStructFallback entryData = contentItem.GetValue<FStructFallback>()!;
						foreach (FPropertyTag property in entryData.Properties)
						{
							switch (property.Name.Text)
							{
								case "SelectedRandomProbability":
									probability = property.Tag!.GetValue<int>();
									break;
								case "ConditionAndCheckData":
									conditionArray = property.Tag!.GetValue<UScriptArray>();
									break;
								case "BaoNeiDaoJuInfos":
									itemArray = property.Tag!.GetValue<UScriptArray>();
									break;
							}
						}

						if (probability == 0 || itemArray is null)
						{
							continue;
						}

						LootEntry entry = new() { Probability = probability };
						int modeMask = 0, notModeMask = 0;

						if (conditionArray is not null)
						{
							foreach (FPropertyTagType conditionObj in conditionArray.Properties)
							{
								UScriptArray? innerArray = conditionObj.GetValue<FStructFallback>()?.Properties.FirstOrDefault(p => p.Name.Text.Equals("ConditionOrCheck"))?.Tag?.GetValue<UScriptArray>();
								if (innerArray is null) continue;

								foreach (FPropertyTagType conditionProp in innerArray.Properties)
								{
									string conditionName = conditionProp.GetValue<FPackageIndex>()!.Name;
									switch (conditionName)
									{
										case "BP_Condition_Area_IsDaCaoYuan_C":
											entry.AreaCondition = 8;
											break;
										case "BP_Condition_Area_IsDLC01_1_C":
											entry.AreaCondition = 103;
											break;
										case "BP_Condition_Area_IsDLC01_2_C":
											entry.AreaCondition = 107;
											break;
										case "BP_Condition_Area_IsDLC01_3_C":
											entry.AreaCondition = 118;
											break;
										case "BP_Condition_Area_IsDLC01_4_C":
											entry.AreaCondition = 125;
											break;
										case "BP_Condition_Area_IsDLC02_1_C":
											entry.AreaCondition = 102;
											break;
										case "BP_Condition_Area_IsDLC02_2_C":
											entry.AreaCondition = 111;
											break;
										case "BP_Condition_Area_IsDLC02_3_C":
											entry.AreaCondition = 122;
											break;
										case "BP_Condition_Area_IsDLC02_4_C":
											entry.AreaCondition = 126;
											break;
										case "BP_Condition_Area_IsHuangYuan_C":
											entry.AreaCondition = 3;
											break;
										case "BP_Condition_Area_IsHuAnSenLin_C":
											entry.AreaCondition = 9;
											break;
										case "BP_Condition_Area_IsHuoShan_C":
											entry.AreaCondition = 4;
											break;
										case "BP_Condition_Area_IsJuMuLin_C":
											entry.AreaCondition = 7;
											break;
										case "BP_Condition_Area_IsQiuLing_C":
											entry.AreaCondition = 6;
											break;
										case "BP_Condition_Area_IsXueShan_C":
											entry.AreaCondition = 5;
											break;
										case "BP_Condition_Area_IsYuLin_C":
											entry.AreaCondition = 1;
											break;
										case "BP_Condition_Area_IsZhaoze_C":
										case "BP_Condition_Area_IsZhaoZe_C":
											entry.AreaCondition = 2;
											break;
										case "BP_Condition_GongJiangDrop_C":
											entry.CraftsmanOnly = true;
											break;
										case "BP_Condition_IsHorn_C":
											entry.ClanCondition = (int)EClanType.CLAN_TYPE_E;
											break;
										case "BP_Condition_IsShenMi_C":
											entry.ClanCondition = (int)EClanType.CLAN_TYPE_C;
											break;
										case "BP_Condition_IsYeMan_C":
											entry.ClanCondition = (int)EClanType.CLAN_TYPE_A;
											break;
										case "BP_Condition_IsWolf_C":
											entry.ClanCondition = (int)EClanType.CLAN_TYPE_F;
											break;
										case "BP_Condition_IsZhiHui_C":
											entry.ClanCondition = (int)EClanType.CLAN_TYPE_B;
											break;
										case "TJ_DL_IsActionMode_C":
											modeMask |= ECustomGameMode.Action.CreateMask();
											break;
										case "TJ_DL_IsCreativeMode_C":
											modeMask |= ECustomGameMode.Creative.CreateMask();
											break;
										case "TJ_DL_IsManagementMode_C":
											modeMask |= ECustomGameMode.Management.CreateMask();
											break;
										case "TJ_DL_IsNotPVP_C":
											notModeMask |= ECustomGameMode.PVP.CreateMask();
											break;
										case "TJ_DL_IsNotSurvival_C":
											notModeMask |= ECustomGameMode.Survival.CreateMask();
											break;
										case "TJ_DL_IsPVP_C":
											entry.PvpOnly = true;
											break;
										case "TJ_DL_PVP_1Day+_C":
											entry.PvpDayCondition = 1;
											break;
										case "TJ_DL_PVP_3Day+_C":
											entry.PvpDayCondition = 3;
											break;
										case "TJ_DL_PVP_7Day+_C":
											entry.PvpDayCondition = 7;
											break;
										case "TJ_Map_Is_DLCLevel01_Main_C":
											entry.AllowedMaps.Add("DemoMap");
											entry.AllowedMaps.Add("DLC_Level01_Main");
											break;
										case "TJ_Map_Is_Level01_Main_C":
											entry.AllowedMaps.Add("DemoMap");
											entry.AllowedMaps.Add("Level01_Main");
											break;
										case "TJ_Map_IsOnly_DLCLevel01_Main_C":
											entry.AllowedMaps.Add("DLC_Level01_Main");
											break;
										case "TJ_Map_IsOnly_Level01_Main_C":
											entry.AllowedMaps.Add("Level01_Main");
											break;
										default:
											logger.Debug($"Found unknown loot condition {conditionName} in {Path.GetFileNameWithoutExtension(filePair.Key)} row \"{pair.Key.Text}\"");
											break;
									}
								}
							}
						}

						if (modeMask != 0) entry.ModeMask = modeMask;
						if (notModeMask != 0) entry.NotModeMask = notModeMask;

						int totalWeight = 0;
						foreach (FPropertyTagType entryItem in itemArray.Properties)
						{
							LootItem item = new();
							FPackageIndex? daoJuIndex = null;

							FStructFallback itemData = entryItem.GetValue<FStructFallback>()!;
							foreach (FPropertyTag property in itemData.Properties)
							{
								switch (property.Name.Text)
								{
									case "DaoJuQuanZhong":
										item.Weight = property.Tag!.GetValue<int>();
										totalWeight += item.Weight;
										break;
									case "DaoJuMagnitude":
										{
											TRange<float>? value = DataUtil.ReadRangeProperty<float>(property);
											if (value.HasValue) item.Amount = value.Value;
										}
										break;
									case "DaoJuPinZhi":
										if (DataUtil.TryParseEnum(property, out EDaoJuPinZhi pinZhi))
										{
											item.Quality = pinZhi;
										}
										break;
									case "DaoJuClass":
										daoJuIndex = property.Tag!.GetValue<FPackageIndex>();
										break;
								}
							}

							if (daoJuIndex is null || daoJuIndex.Name.Equals("None"))
							{
								totalWeight -= item.Weight;
								continue;
							}

							item.Asset = daoJuIndex;

							entry.Items.Add(item);
						}

						for (int i = 0; i < entry.Items.Count; ++i)
						{
							entry.Items[i] = entry.Items[i] with { Weight = (int)((float)entry.Items[i].Weight / totalWeight * 100.0f) };
						}

						content.BaseEntries.Add(entry);
					}

					if (extraContentArray is not null)
					{
						foreach (FPropertyTagType extraContent in extraContentArray.Properties)
						{
							FStructFallback? contentStruct = extraContent.GetValue<FStructFallback>();
							if (contentStruct is null) continue;

							string? entryName = null;
							int? entryProbability = null;
							TRange<float>? entryAmount = null;
							foreach (FPropertyTag contentProperty in contentStruct.Properties)
							{
								switch (contentProperty.Name.Text!)
								{
									case "DropIDName":
										entryName = contentProperty.Tag!.GetValue<FName>().Text;
										break;
									case "DropProbability":
										entryProbability = contentProperty.Tag!.GetValue<int>();
										break;
									case "DropMagnitude":
										entryAmount = DataUtil.ReadRangeProperty<float>(contentProperty);
										break;
								}
							}

							if (entryName is null || !entryProbability.HasValue || !entryAmount.HasValue)
							{
								logger.Warning($"Unable to read ExtraDropContentData from {Path.GetFileNameWithoutExtension(filePair.Key)} row \"{pair.Key.Text}\"");
								continue;
							}

							content.ExtraDrops.Add(new(entryName, entryProbability.Value, entryAmount.Value));
						}
					}

					mLootMap.Add(name, content);
				}
			}

			if (mLootMap.Count == 0)
			{
				logger.Error("Failed to load any loot tables");
				return false;
			}

			return true;
		}

		private bool LoadCollectionData(IFileProvider provider, Logger logger)
		{
			if (!provider.TryGetGameFile("WS/Content/Blueprints/ZiYuanGuanLi/BP_ShengWuCollectData.uasset", out GameFile? file))
			{
				logger.Error("Unable to load BP_ShengWuCollectData");
				return false;
			}

			Package package = (Package)provider.LoadPackage(file);
			UScriptMap? configMap = DataUtil.FindBlueprintDefaultsObject(package)?.Properties.FirstOrDefault(p => p.Name.Text.Equals("ShengWuPropConfigSoftMap"))?.Tag?.GetValue<UScriptMap>();
			if (configMap == null)
			{
				logger.Error("Unable to load ShengWuPropConfigSoftMap from BP_ShengWuCollectData");
				return false;
			}

			foreach (var configPair in configMap.Properties)
			{
				string key = configPair.Key.GetValue<FSoftObjectPath>()!.AssetPathName.Text;
				key = key.Substring(key.LastIndexOf('.') + 1);
				FStructFallback config = configPair.Value!.GetValue<FStructFallback>()!;

				UScriptMap? collectMap = null;
				int amount = 0;
				float totalDamage = 0, damagePerReward = 0;
				foreach (FPropertyTag property in config.Properties)
				{
					switch (property.Name.Text)
					{
						case "ShengWuCollectDaoJuMap":
							collectMap = property.Tag?.GetValue<UScriptMap>();
							break;
						case "ShengWuCollectAmount":
							amount = property.Tag!.GetValue<int>();
							break;
						case "ShengWuCollectableTotalAmount":
							totalDamage = property.Tag!.GetValue<float>();
							break;
						case "ShengWuCollectGainDaojuDamage":
							damagePerReward = property.Tag!.GetValue<float>();
							break;
					}
				}

				if (collectMap is null || amount == 0 || totalDamage == 0 || damagePerReward == 0)
				{
					logger.Warning($"Unable to locate collection data for {key}");
					continue;
				}

				string? hit = null, finalHit = null, baby = null;
				foreach (var collectPair in collectMap.Properties)
				{
					FStructFallback collectObj = collectPair.Value!.GetValue<FStructFallback>()!;
					foreach (FPropertyTag property in collectObj.Properties)
					{
						switch (property.Name.Text)
						{
							case "CaiJiDaoJuBaoName":
								{
									string? item = property.Tag?.GetValue<FName>().Text;
									if (hit is null)
									{
										hit = item;
									}
									else if (!hit.Equals(item))
									{
										logger.Warning($"Found differing loot data values in {key}");
									}
								}
								break;
							case "CaiJiFinalDaoJuBaoName":
								{
									string? item = property.Tag?.GetValue<FName>().Text;
									if (finalHit is null)
									{
										finalHit = item;
									}
									else if (!finalHit.Equals(item))
									{
										logger.Warning($"Found differing loot data values in {key}");
									}
								}
								break;
							case "FaYuingCaiJiDaoJuBaoName":
								{
									string? item = property.Tag?.GetValue<FName>().Text;
									if (baby is null)
									{
										baby = item;
									}
									else if (!baby.Equals(item))
									{
										logger.Warning($"Found differing loot data values in {key}");
									}
								}
								break;
						}
					}
				}

				if (string.Equals(hit, "None")) hit = null;
				if (string.Equals(finalHit, "None")) finalHit = null;
				if (string.Equals(baby, "None")) baby = null;

				mCollectionMap.Add(key, new()
				{
					Hit = hit,
					FinalHit = finalHit,
					Baby = baby,
					Amount = amount
				});
			}

			return true;
		}

		private void ResolveDependencies(Logger logger)
		{
			foreach (var pair in mLootMap)
			{
				foreach (ExtraDropEntry extra in pair.Value.ExtraDrops)
				{
					if (!mLootMap.TryGetValue(extra.Name, out LootTable? table))
					{
						logger.Warning($"Failed to locate referenced extra loot entry {extra.Name} found in entry {pair.Key}");
						continue;
					}
					extra.Table = table;
				}
			}

			foreach (var pair in mLootMap)
			{
				pair.Value.Resolve(logger);
			}
		}

		private void WriteCsvLoot(Config config, Logger logger)
		{
			string outPath = Path.Combine(config.OutputDirectory, "loot.csv");
			using FileStream outFile = IOUtil.CreateFile(outPath, logger);
			using StreamWriter writer = new(outFile, Encoding.UTF8);

			writer.WriteLine("id,entry,item,chance,cond,weight,min,max,quality,asset");

			foreach (var pair in LootMap)
			{
				for (int e = 0; e < pair.Value.AllEntries.Count; ++e)
				{
					LootEntry entry = pair.Value.AllEntries[e];
					string? conditions = entry.GetConditionsJson();
					for (int i = 0; i < entry.Items.Count; ++i)
					{
						LootItem item = entry.Items[i];
						writer.WriteLine($"{SerializationUtil.CsvStr(pair.Key)},{e},{i},{entry.Probability},{SerializationUtil.CsvStr(conditions)},{item.Weight},{item.Amount.LowerBound.Value},{item.Amount.UpperBound.Value},{(int)item.Quality},{SerializationUtil.CsvStr(item.Asset.Name)}");
					}
				}
			}
		}

		private void WriteSqlLoot(ISqlWriter sqlWriter, Logger logger)
		{
			// create table `loot` (
			//   `id` varchar(127) not null,
			//   `entry` int not null,
			//   `item` int not null,
			//   `chance` int not null,
			//   `cond` varchar(255),
			//   `weight` int not null,
			//   `min` int not null,
			//   `max` int not null,
			//   `quality` int not null,
			//   `asset` varchar(127) not null,
			//   primary key (`id`, `entry`, `item`)
			// )

			sqlWriter.WriteStartTable("loot");

			foreach (var pair in LootMap)
			{
				for (int e = 0; e < pair.Value.AllEntries.Count; ++e)
				{
					LootEntry entry = pair.Value.AllEntries[e];
					string? conditions = entry.GetConditionsJson();
					for (int i = 0; i < entry.Items.Count; ++i)
					{
						LootItem item = entry.Items[i];
						sqlWriter.WriteRow($"{SerializationUtil.DbStr(pair.Key)}, {e}, {i}, {entry.Probability}, {SerializationUtil.DbStr(conditions)}, {item.Weight}, {item.Amount.LowerBound.Value}, {item.Amount.UpperBound.Value}, {(int)item.Quality}, {SerializationUtil.DbStr(item.Asset.Name)}");
					}
				}
			}

			sqlWriter.WriteEndTable();
		}
	}

	/// <summary>
	/// Data for generating loot when collecting from a dead animal
	/// </summary>
	internal struct CollectionData
	{
		public string? Hit;
		public string? FinalHit;
		public string? Baby;
		public int Amount;
	}

	/// <summary>
	/// The data for a named loot table
	/// </summary>
	internal class LootTable
	{
		private bool mIsResolved;

		public List<LootEntry> AllEntries { get; }

		public List<LootEntry> BaseEntries { get; }

		public List<ExtraDropEntry> ExtraDrops { get; }

		public LootTable()
		{
			mIsResolved = false;
			AllEntries = new();
			BaseEntries = new();
			ExtraDrops = new();
		}

		public void Resolve(Logger logger)
		{
			if (mIsResolved) return;

			AllEntries.AddRange(BaseEntries);
			foreach (ExtraDropEntry extraEntry in ExtraDrops)
			{
				if (extraEntry.Table is null) continue;

				extraEntry.Table.Resolve(logger);
				foreach (LootEntry entry in extraEntry.Table.AllEntries)
				{
					LootEntry copy = entry with { Probability = (int)(entry.Probability * (extraEntry.Probability / 100.0f)) };
					AllEntries.Add(copy);
				}
			}

			mIsResolved = true;
		}

		public override string? ToString()
		{
			return $"{BaseEntries.Count} entries, {ExtraDrops.Count} extras";
		}
	}

	/// <summary>
	/// An entry in a loot table
	/// </summary>
	internal record LootEntry
	{
		public int Probability { get; set; }
		public List<LootItem> Items { get; init; } = new();

		public int? AreaCondition { get; set; }
		public int? ClanCondition { get; set; }
		public bool CraftsmanOnly { get; set; }
		public List<string> AllowedMaps { get; init; } = new();
		public int? ModeMask { get; set; }
		public int? NotModeMask { get; set; }
		public bool PvpOnly { get; set; }
		public int? PvpDayCondition { get; set; }
		public bool UnknownCondition { get; set; }

		public string? GetConditionsJson()
		{
			if (AreaCondition.HasValue || ClanCondition.HasValue || CraftsmanOnly || AllowedMaps.Count > 0 || ModeMask.HasValue || NotModeMask.HasValue || PvpOnly || PvpDayCondition.HasValue || UnknownCondition)
			{
				StringBuilder builder = new("{");
				if (AreaCondition.HasValue) builder.Append($"\"area\":{AreaCondition.Value},");
				if (ClanCondition.HasValue) builder.Append($"\"clan\":{ClanCondition.Value},");
				if (CraftsmanOnly) builder.Append($"\"craft\":1,");
				if (AllowedMaps.Count > 0) builder.Append($"\"maps\":[\"{string.Join("\",\"", AllowedMaps)}\"],");
				if (ModeMask.HasValue) builder.Append($"\"mode\":{ModeMask.Value},");
				if (NotModeMask.HasValue) builder.Append($"\"notmode\":{NotModeMask.Value},");
				if (PvpOnly) builder.Append($"\"pvp\":1,");
				if (PvpDayCondition.HasValue) builder.Append($"\"pvpday\":{PvpDayCondition.Value},");
				if (UnknownCondition) builder.Append($"\"unknown\":1,");
				--builder.Length; // Remove trailing comma
				builder.Append("}");
				return builder.ToString();
			}
			return null;
		}

		public override string ToString()
		{
			return $"{Probability}, {Items.Count} items";
		}
	}

	/// <summary>
	/// An item in a loot entry
	/// </summary>
	internal record struct LootItem
	{
		public int Weight { get; set; }
		public TRange<float> Amount { get; set; }
		public EDaoJuPinZhi Quality { get; set; }
		public FPackageIndex Asset { get; set; }

		public LootItem()
		{
			Weight = 0;
			Amount = new TRange<float>();
			Quality = EDaoJuPinZhi.EDJPZ_Level1;
			Asset = null!;
		}

		public override string ToString()
		{
			return $"{Weight}, {Amount.LowerBound.Value}-{Amount.UpperBound.Value} {Asset.Name} (Quality {(int)Quality})";
		}
	}

	/// <summary>
	/// A reference to an additional loot table
	/// </summary>
	internal class ExtraDropEntry
	{
		public string Name { get; }
		public int Probability { get; }
		public TRange<float> Amount { get; }
		public LootTable? Table { get; set; }

		public ExtraDropEntry(string name, int probability, TRange<float> amount)
		{
			Name = name;
			Probability = probability;
			Amount = amount;
		}
	}
}
