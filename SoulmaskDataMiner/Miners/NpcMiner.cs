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
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using SoulmaskDataMiner.Data;
using SoulmaskDataMiner.GameData;
using SoulmaskDataMiner.IO;
using SoulmaskDataMiner.MapUtil;

namespace SoulmaskDataMiner.Miners
{
	/// <summary>
	/// Gathers information about NPC classes
	/// </summary>
	[MinerName("Npc"), RequiredBaseClasses("HCharacterRen", "HCharacterDongWu")]
	internal class NpcMiner : SubclassMinerBase
	{
		protected override string NameProperty => "MoRenMingZi";

		private const string BaseClassName_NonHuman = "HCharacterDongWu";
		private const string BaseClassName_Human = "HCharacterRen";

		// Row type for the unified NPC table: tags each NPC with its kind.
		// 0 = animal, 1 = mechanical, 2 = human (matches existing SQL convention).
		private readonly record struct NpcRow(int Type, ObjectInfo Info);

		// Schema
		// create table `npc`
		// (
		//   `type` int not null,
		//   `name` varchar(255) not null,
		//   `class` varchar(255) not null
		// )
		private static readonly MinerTable<NpcRow> sNpcTable = new(
			csvFileName: "Npc.csv",
			sqlTableName: "npc",
			columns:
			[
				TableColumn.Int<NpcRow>("type", r => r.Type),
				TableColumn.Str<NpcRow>("name", r => r.Info.Name, treatNullAsEmpty: true),
				TableColumn.Str<NpcRow>("class", r => r.Info.ClassName),
			]);

		// Schema
		// create table `sm`
		// (
		//   `number` int not null,
		//   `name` varchar(255) not null,
		//   `quality` int not null,
		//   `status` varchar(31) not null,
		//   `prof` varchar(511),
		//   `ng` varchar(1023),
		//   `male` bool not null,
		//   `female` bool not null,
		//   `min` int not null,
		//   `max` int not null,
		//   primary key (`number`)
		// )
		private static readonly MinerTable<SpecifiedManData> sSpecifiedManTable = new(
			csvFileName: "SpecifiedMan.csv",
			sqlTableName: "sm",
			columns:
			[
				TableColumn.Int<SpecifiedManData>("number", sm => sm.Number),
				TableColumn.Str<SpecifiedManData>("name", sm => sm.Name),
				TableColumn.Int<SpecifiedManData>("quality", sm => sm.Quality),
				TableColumn.Str<SpecifiedManData>("status", sm => sm.ClanStatus.ToEn()),
				TableColumn.Str<SpecifiedManData>("prof", sm => string.Join("<br/>", sm.Proficiencies.Select(p => p.ToString()))),
				TableColumn.Str<SpecifiedManData>("ng", sm => string.Join("<br/>", sm.NaturalGifts.Select(g => g.ToString()))),
				TableColumn.Bool<SpecifiedManData>("male", sm => sm.HasMale),
				TableColumn.Bool<SpecifiedManData>("female", sm => sm.HasFemale),
				TableColumn.Int<SpecifiedManData>("min", sm => sm.MinLevel),
				TableColumn.Int<SpecifiedManData>("max", sm => sm.MaxLevel),
			]);

		public override bool Run(IProviderManager providerManager, Config config, Logger logger, ISqlWriter sqlWriter)
		{
			IEnumerable<ObjectInfo> nonHumans = FindObjects(BaseClassName_NonHuman.AsEnumerable());
			IEnumerable<ObjectInfo> humans = FindObjects(BaseClassName_Human.AsEnumerable());

			List<ObjectInfo> animals = new();
			List<ObjectInfo> mechanical = new();
			foreach (ObjectInfo nonHuman in nonHumans)
			{
				if (GameClassHierarchy.Instance.IsDerivedFrom(nonHuman.ClassName, "BP_JiXie_Base_C"))
				{
					mechanical.Add(nonHuman);
				}
				else
				{
					animals.Add(nonHuman);
				}
			}

			IEnumerable<SpecifiedManData>? specifiedManData = FindSpecifiedManData(providerManager, logger);
			if (specifiedManData is null) return false;

			// Preserve historical SQL row order: animals (type=0), mechanicals (type=1), humans (type=2).
			IEnumerable<NpcRow> npcRows = animals.Select(o => new NpcRow(0, o))
				.Concat(mechanical.Select(o => new NpcRow(1, o)))
				.Concat(humans.Select(o => new NpcRow(2, o)));

			WriteTable(npcRows, sNpcTable, config, logger, sqlWriter);
			sqlWriter.WriteEmptyLine();
			WriteTable(specifiedManData, sSpecifiedManTable, config, logger, sqlWriter);
			return true;
		}

		private IEnumerable<SpecifiedManData>? FindSpecifiedManData(IProviderManager providerManager, Logger logger)
		{
			UScriptArray specifiedManArray;
			if (!providerManager.SingletonManager.ResourceManager.TryGetPropertyValue<UScriptArray>("SpecifiedTribeManArray", out specifiedManArray))
			{
				logger.Error("Failed to find SpecifiedTribeManArray in resource manager");
				return null;
			}

			IDictionary<int, NaturalGiftData>? gifts = LoadNaturalGifts(providerManager, logger);
			if (gifts is null)
			{
				logger.Error("Failed to load natural gift data");
				return null;
			}

			IReadOnlyDictionary<EProficiency, ProficiencyData>? proficiencyMap = ProficiencyMiner.LoadProficiencyMap(providerManager, logger);
			if (proficiencyMap is null)
			{
				logger.Error("Failed to load proficiency data");
				return null;
			}

			List<SpecifiedManData> outList = new();

			foreach (FPropertyTagType specifiedManObjProp in specifiedManArray.Properties)
			{
				SpecifiedManData specifiedMan = new();
				SpawnData? spawnData = null;
				UScriptArray? profArray = null;
				UScriptArray? giftIdArray = null;

				FStructFallback specifiedManObj = specifiedManObjProp.GetValue<FStructFallback>()!;
				foreach (FPropertyTag property in specifiedManObj.Properties)
				{
					switch (property.Name.Text)
					{
						case "CreateNo":
							specifiedMan.Number = property.Tag!.GetValue<int>();
							break;
						case "ZhiDingPinZhi":
							specifiedMan.Quality = property.Tag!.GetValue<int>();
							break;
						case "ZhiDingDiWei":
							if (DataUtil.TryParseEnum<EClanDiWei>(property, out EClanDiWei result))
							{
								specifiedMan.ClanStatus = result;
							}
							break;
						case "SCGClass":
							spawnData = SpawnDataUtil.LoadSpawnData(property, logger, null)?.DefaultSpawnData;
							break;
						case "ShuLianDu":
							profArray = property.Tag?.GetValue<UScriptArray>();
							break;
						case "TianFu":
							giftIdArray = property.Tag?.GetValue<UScriptArray>();
							break;
					}
				}

				if (spawnData is null)
				{
					logger.Warning("Unable to load data for specified man.");
					continue;
				}

				specifiedMan.Name = spawnData.NpcNames.First();
				specifiedMan.MinLevel = spawnData.MinLevel;
				specifiedMan.MaxLevel = spawnData.MaxLevel;

				foreach (NpcData npcData in spawnData.NpcData.Select(wv => wv.Value))
				{
					switch (npcData.Sex)
					{
						case EXingBieType.CHARACTER_XINGBIE_NAN:
							specifiedMan.HasMale = true;
							break;
						case EXingBieType.CHARACTER_XINGBIE_NV:
							specifiedMan.HasFemale = true;
							break;
					}
				}

				if (profArray is not null)
				{
					foreach (FPropertyTagType item in profArray.Properties)
					{
						FStructFallback? profStruct = item.GetValue<FStructFallback>();
						if (profStruct is null) continue;

						EProficiency? proficiency = null;
						int curLevel = 0, maxLevel = 0;

						foreach (FPropertyTag property in profStruct.Properties)
						{
							switch (property.Name.Text)
							{
								case "ShuLianDuType":
									if (DataUtil.TryParseEnum(property, out EProficiency result))
									{
										proficiency = result;
									}
									break;
								case "CurLv":
									curLevel = property.Tag!.GetValue<int>();
									break;
								case "MaxLv":
									maxLevel = property.Tag!.GetValue<int>();
									break;
							}
						}

						if (!proficiency.HasValue)
						{
							continue;
						}

						ProficiencyEntry profEntry = new()
						{
							Data = proficiencyMap[proficiency.Value],
							CurrentLevel = curLevel,
							MaxLevel = maxLevel
						};

						specifiedMan.Proficiencies.Add(profEntry);
					}
				}

				if (giftIdArray is not null)
				{
					foreach (FPropertyTagType item in giftIdArray.Properties)
					{
						int giftId = item.GetValue<int>();
						specifiedMan.NaturalGifts.Add(gifts[giftId]);
					}
				}

				outList.Add(specifiedMan);
			}

			return outList;
		}

		private IDictionary<int, NaturalGiftData>? LoadNaturalGifts(IProviderManager providerManager, Logger logger)
		{
			if (!providerManager.Provider.TryGetGameFile("WS/Content/Blueprints/DataTable/NaturalGift/DT_GiftZongBiao.uasset", out GameFile? file))
			{
				logger.Error("Unable to locate natural gift data table.");
				return null;
			}

			Package package = (Package)providerManager.Provider.LoadPackage(file);
			UDataTable table = (UDataTable)package.ExportMap[0].ExportObject.Value;

			Dictionary<int, NaturalGiftData> gifts = new();

			foreach (var row in table.RowMap)
			{
				int id;
				if (!int.TryParse(row.Key.Text, out id))
				{
					logger.Warning($"Natural gift table row key is not a valid integer: {row.Key.Text}");
					continue;
				}

				int level = 0;
				string? title = null, description = null;

				foreach (FPropertyTag property in row.Value.Properties)
				{
					switch (property.Name.Text)
					{
						case "Star":
							level = property.Tag!.GetValue<int>();
							break;
						case "Title":
							title = DataUtil.ReadTextProperty(property);
							break;
						case "Desc":
							description = DataUtil.ReadTextProperty(property);
							break;
					}
				}

				string levelStr = level switch
				{
					1 => " I",
					2 => " II",
					3 => " III",
					4 => " IV",
					5 => " V",
					_ => string.Empty
				};

				gifts.Add(id, new() { ID = id, Title = $"{title}{levelStr}", Description = description });
			}

			return gifts;
		}

		private struct SpecifiedManData
		{
			public int Number;
			public string Name;
			public int Quality;
			public EClanDiWei ClanStatus;
			public List<ProficiencyEntry> Proficiencies;
			public List<NaturalGiftData> NaturalGifts;
			public bool HasMale;
			public bool HasFemale;
			public int MinLevel;
			public int MaxLevel;

			public SpecifiedManData()
			{
				Name = null!;
				Proficiencies = new();
				NaturalGifts = new();
			}

			public override string ToString()
			{
				return $"[{Number}] {Name} ({MinLevel}-{MaxLevel})";
			}
		}

		private struct ProficiencyEntry
		{
			public ProficiencyData Data;
			public int CurrentLevel;
			public int MaxLevel;

			public override string ToString()
			{
				// Used in program output
				return $"{Data.Name}: {CurrentLevel}/{MaxLevel}";
			}
		}

		private struct NaturalGiftData
		{
			public int ID;
			public string? Title;
			public string? Description;

			public override string ToString()
			{
				// Used in program output
				return $"[{ID}] {Title}: {Description}";
			}
		}
	}
}
