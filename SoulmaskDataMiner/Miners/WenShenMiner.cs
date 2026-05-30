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
using CUE4Parse.UE4.Assets.Exports.Engine;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using SoulmaskDataMiner.Data;
using SoulmaskDataMiner.GameData;
using SoulmaskDataMiner.IO;
using System.Diagnostics.CodeAnalysis;

namespace SoulmaskDataMiner.Miners
{
	/// <summary>
	/// Mines data about WenShen (body paints)
	/// </summary>
	[MinerName("WenShen")]
	internal class WenShenMiner : MinerBase
	{
		// Schema
		// create table `wenshen` (
		//   `name` varchar(127) not null,
		//   `special` bool not null,
		//   `head_m` int, `head_f` int, `head_ico` varchar(127),
		//   `chest_m` int, `chest_f` int, `chest_ico` varchar(127),
		//   `arm_m` int, `arm_f` int, `arm_ico` varchar(127),
		//   `leg_m` int, `leg_f` int, `leg_ico` varchar(127),
		//   primary key (`name`, `special`)
		// )
		private static readonly MinerTable<CombinedWenShenData> sTable = new(
			csvFileName: "WenShen.csv",
			sqlTableName: "wenshen",
			columns:
			[
				TableColumn.Str<CombinedWenShenData>("name", ws => ws.Key.Name),
				TableColumn.Bool<CombinedWenShenData>("special", ws => ws.Key.IsSpecial),
				TableColumn.Int<CombinedWenShenData>("head_m", ws => ws.Head.MaleId),
				TableColumn.Int<CombinedWenShenData>("head_f", ws => ws.Head.FemaleId),
				TableColumn.Str<CombinedWenShenData>("head_ico", ws => ws.Head.Icon.Name),
				TableColumn.Int<CombinedWenShenData>("chest_m", ws => ws.Chest.MaleId),
				TableColumn.Int<CombinedWenShenData>("chest_f", ws => ws.Chest.FemaleId),
				TableColumn.Str<CombinedWenShenData>("chest_ico", ws => ws.Chest.Icon.Name),
				TableColumn.Int<CombinedWenShenData>("arm_m", ws => ws.Arm.MaleId),
				TableColumn.Int<CombinedWenShenData>("arm_f", ws => ws.Arm.FemaleId),
				TableColumn.Str<CombinedWenShenData>("arm_ico", ws => ws.Arm.Icon.Name),
				TableColumn.Int<CombinedWenShenData>("leg_m", ws => ws.Leg.MaleId),
				TableColumn.Int<CombinedWenShenData>("leg_f", ws => ws.Leg.FemaleId),
				TableColumn.Str<CombinedWenShenData>("leg_ico", ws => ws.Leg.Icon.Name),
			]);
			// Each row has four icons (head, chest, arm, leg) so we drive icon export
			// with separate WriteIcons calls below rather than via MinerTable.

		public override bool Run(IProviderManager providerManager, Config config, Logger logger, ISqlWriter sqlWriter)
		{
			List<CombinedWenShenData> data = GetWenShenData(providerManager, logger).ToList();
			if (data.Count == 0)
			{
				return false;
			}

			WriteTable(data, sTable, config, logger, sqlWriter);
			WriteIcons(data, ws => ws.Head.Icon, config, logger);
			WriteIcons(data, ws => ws.Chest.Icon, config, logger);
			WriteIcons(data, ws => ws.Arm.Icon, config, logger);
			WriteIcons(data, ws => ws.Leg.Icon, config, logger);
			return true;
		}

		private IEnumerable<CombinedWenShenData> GetWenShenData(IProviderManager providerManager, Logger logger)
		{
			IEnumerable<WenShenData>? wenShenList = GetWenShenList(providerManager, logger);
			if (wenShenList is null)
			{
				yield break;
			}

			Dictionary<int, WenShenData> wenShenIdMap = wenShenList.ToDictionary(ws => ws.Id, ws => ws);
			
			Dictionary<WenShenKey, List<WenShenData>> wenShenNameMap = new();
			foreach (WenShenData wenShen in wenShenList)
			{
				List<WenShenData>? list;
				if (!wenShenNameMap.TryGetValue(wenShen.Key!.Value, out list))
				{
					list = new();
					wenShenNameMap.Add(wenShen.Key!.Value, list);
				}
				list.Add(wenShen);
			}

			foreach(var pair in wenShenNameMap)
			{
				yield return CombinedWenShenData.Build(pair.Value, wenShenIdMap);
			}
		}

		private IEnumerable<WenShenData>? GetWenShenList(IProviderManager providerManager, Logger logger)
		{
			if (!providerManager.Provider.TryGetGameFile("WS/Content/Blueprints/ZiYuanGuanLi/DT_WenShenTable.uasset", out GameFile? file))
			{
				logger.Error("Unable to locate asset DT_YiWenText.");
				return null;
			}

			Package package = (Package)providerManager.Provider.LoadPackage(file);
			UDataTable? table = package.ExportMap[0].ExportObject.Value as UDataTable;
			if (table is null)
			{
				logger.Error("Error loading DT_WenShenTable");
				return null;
			}

			List<WenShenData> wenShenList = new();
			foreach (var pair in table.RowMap)
			{
				if (!int.TryParse(pair.Key.Text, out int id))
				{
					logger.Warning($"Failed to parse ID '{pair.Key.Text}'. Skipping this WenShen.");
					continue;
				}

				string? name = null;
				bool? isSpecial = null;
				WenShenData data = new()
				{
					Id = id
				};

				foreach (FPropertyTag property in pair.Value.Properties)
				{
					switch (property.Name.Text)
					{
						case "CaiHuiIcon":
							{
								UTexture2D? icon = DataUtil.ReadTextureProperty(property);
								if (icon is not null)
								{
									string[] parts = icon.Name.Split('_');
									name = parts[0];
									if (int.TryParse(parts.Last(), out int index1))
									{
										name = $"{parts[0]} {index1}";
									}
									else if (TryParseIntFromEnd(parts[0], out string? truncated, out int index2))
									{
										name = $"{truncated} {index2}";
									}
									data.Icon = icon;
								}
								else
								{
									goto ExitLoop;
								}
							}
							break;
						case "XingBie":
							if (DataUtil.TryParseEnum<EXingBieType>(property, out EXingBieType gender))
							{
								data.Gender = gender;
							}
							break;
						case "BuWei":
							if (DataUtil.TryParseEnum<EHWenShenBuWei>(property, out EHWenShenBuWei location))
							{
								data.Location = location;
							}
							break;
						case "YiXingCaiHuiIndex":
							if (property.Tag is null)
							{
								continue;
							}
							data.OtherGenderId = property.Tag.GetValue<int>();
							break;
						case "bTeShu":
							if (property.Tag is null)
							{
								continue;
							}
							isSpecial = property.Tag.GetValue<bool>();
							break;
					}
				}

			ExitLoop:
				if (name is not null && isSpecial.HasValue)
				{
					data.Key = new(name, isSpecial.Value);
				}

				if (data.IsValid())
				{
					wenShenList.Add(data);
				}
				else
				{
					logger.Warning($"Failed to read required properties. Skipping WenShen '{id}'.");
				}
			}

			if (wenShenList.Count == 0)
			{
				logger.Error("Failed to load any WenShen instances");
				return null;
			}

			wenShenList.Sort();

			return wenShenList;
		}

		private static bool TryParseIntFromEnd(string value, [NotNullWhen(true)] out string? truncated, out int result)
		{
			if (value.Length == 0)
			{
				truncated = null;
				result = default;
				return false;
			}

			int count = 0;
			for (int i = value.Length - 1; i >= 0; --i)
			{
				if (!char.IsDigit(value[i]))
				{
					break;
				}
				++count;
			}

			if (count == 0)
			{
				truncated = null;
				result = default;
				return false;
			}

			int split = value.Length - count;

			truncated = value.Substring(0, split);
			result = int.Parse(value.Substring(split));
			return true;
		}

		private readonly struct WenShenKey : IEquatable<WenShenKey>, IComparable<WenShenKey>
		{
			public readonly string Name;
			public readonly bool IsSpecial;

			public WenShenKey(string name, bool isSpecial)
			{
				Name = name;
				IsSpecial = isSpecial;
			}

			public override int GetHashCode()
			{
				return HashCode.Combine(Name.GetHashCode(), IsSpecial.GetHashCode());
			}

			public override bool Equals([NotNullWhen(true)] object? obj)
			{
				return obj is WenShenKey other && Equals(other);
			}

			public bool Equals(WenShenKey other)
			{
				return Name.Equals(other.Name) && IsSpecial.Equals(other.IsSpecial);
			}

			public int CompareTo(WenShenKey other)
			{
				int result = Name.CompareTo(other.Name);
				if (result == 0)
				{
					result = IsSpecial.CompareTo(other.IsSpecial);
				}
				return result;
			}

			public override string ToString()
			{
				return Name;
			}
		}

		private struct CombinedWenShenData
		{
			public WenShenKey Key;
			public WenShenSlot Head;
			public WenShenSlot Chest;
			public WenShenSlot Arm;
			public WenShenSlot Leg;

			public static CombinedWenShenData Build(IReadOnlyList<WenShenData> list, IReadOnlyDictionary<int, WenShenData> idMap)
			{
				if (list.Count == 0)
				{
					throw new ArgumentException("CombinedWenShenData.Build called with empty list", nameof(list));
				}

				CombinedWenShenData result = new()
				{
					Key = list[0].Key!.Value
				};

				HashSet<int> processedIds = new();
				foreach (WenShenData data in list)
				{
					if (!processedIds.Add(data.Id))
					{
						continue;
					}

					int female, male;
					if (data.Gender!.Value == EXingBieType.CHARACTER_XINGBIE_NAN)
					{
						male = data.Id;
						female = data.OtherGenderId;
					}
					else
					{
						female = data.Id;
						male = data.OtherGenderId;
					}

					WenShenSlot slot = new()
					{
						FemaleId = female,
						MaleId = male,
						Icon = data.Icon
					};

					switch (data.Location!.Value)
					{
						case EHWenShenBuWei.Tou:
							result.Head = slot;
							break;
						case EHWenShenBuWei.Xiong:
							result.Chest = slot;
							break;
						case EHWenShenBuWei.Shou:
							result.Arm = slot;
							break;
						case EHWenShenBuWei.Jiao:
							result.Leg = slot;
							break;
					}
				}

				return result;
			}

			public override readonly string ToString()
			{
				return Key.ToString();
			}
		}

		private struct WenShenSlot
		{
			public int MaleId;
			public int FemaleId;
			public UTexture2D Icon;

			public override readonly string ToString()
			{
				return $"{MaleId}, {FemaleId}, {Icon.Name}";
			}
		}

		private struct WenShenData : IComparable<WenShenData>
		{
			public int Id;
			public WenShenKey? Key;
			public UTexture2D Icon;
			public EXingBieType? Gender;
			public EHWenShenBuWei? Location;
			public int OtherGenderId;

			public readonly bool IsValid()
			{
				return Key.HasValue && Icon is not null && Gender.HasValue && Location.HasValue;
			}

			public readonly int CompareTo(WenShenData other)
			{
				return Id.CompareTo(other.Id);
			}

			public override readonly string ToString()
			{
				return $"[{Id}] {Key}";
			}
		}
	}
}
