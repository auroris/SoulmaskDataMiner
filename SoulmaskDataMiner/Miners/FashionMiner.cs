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

namespace SoulmaskDataMiner.Miners
{
	[MinerName("Fashion")]
	internal class FashionMiner : MinerBase
	{
		// Schema
		// create table `fashion` (
		//   `name` varchar(63) not null,
		//   `desc` varchar(127),
		//   `id_m` int not null,
		//   `id_f` int not null,
		//   `icon` varchar(127) not null,
		//   primary key (`name`)
		// )
		private static readonly MinerTable<CombinedFashionData> sTable = new(
			csvFileName: "Fashion.csv",
			sqlTableName: "fashion",
			columns:
			[
				TableColumn.Str<CombinedFashionData>("name", f => f.Name),
				TableColumn.Str<CombinedFashionData>("desc", f => f.Desc),
				TableColumn.Int<CombinedFashionData>("id_m", f => f.MaleId),
				TableColumn.Int<CombinedFashionData>("id_f", f => f.FemaleId),
				TableColumn.Str<CombinedFashionData>("icon", f => f.Icon.Name),
			],
			iconSelector: f => f.Icon);

		public override bool Run(IProviderManager providerManager, Config config, Logger logger, ISqlWriter sqlWriter)
		{
			IEnumerable<CombinedFashionData> fashionData = GetFashionData(providerManager, logger);
			if (!fashionData.Any())
			{
				logger.Error("No fashion data found.");
				return false;
			}

			WriteTable(fashionData, sTable, config, logger, sqlWriter);
			return true;
		}

		private IEnumerable<CombinedFashionData> GetFashionData(IProviderManager providerManager, Logger logger)
		{
			IEnumerable<FashionData>? fashionList = GetFashionList(providerManager, logger);
			if (fashionList is null) yield break;

			Dictionary<string, FashionData> iconFashionMap = new();
			foreach (FashionData fashion in fashionList)
			{
				string iconName = fashion.Icon.Name;
				if (iconFashionMap.TryGetValue(iconName, out FashionData? otherFashion))
				{
					int maleId = 0, femaleId = 0;

					if (fashion.Gender == EXingBieType.CHARACTER_XINGBIE_NAN)
					{
						maleId = fashion.Id;
					}
					else if (fashion.Gender == EXingBieType.CHARACTER_XINGBIE_NV)
					{
						femaleId = fashion.Id;
					}

					if (maleId == 0 && otherFashion.Gender == EXingBieType.CHARACTER_XINGBIE_NAN)
					{
						maleId = otherFashion.Id;
					}
					else if (femaleId == 0 && otherFashion.Gender == EXingBieType.CHARACTER_XINGBIE_NV)
					{
						femaleId = otherFashion.Id;
					}

					if (maleId == 0 && femaleId == 0)
					{
						logger.Warning($"Could not parse data for Fashion pair [{fashion.Id},{otherFashion.Id}]");
						continue;
					}

					string name = fashion.Name.Substring(0, fashion.Name.Length - 4);

					yield return new CombinedFashionData(maleId, femaleId, name, fashion.Desc, fashion.Icon);

					iconFashionMap.Remove(iconName);
				}
				else
				{
					iconFashionMap.Add(iconName, fashion);
				}
			}

			// Any remaining fashions are single-gender or genderless
			foreach (FashionData fashion in iconFashionMap.Values)
			{
				int maleId = 0, femaleId = 0;

				if (fashion.Gender == EXingBieType.CHARACTER_XINGBIE_NAN)
				{
					maleId = fashion.Id;
				}
				else if (fashion.Gender == EXingBieType.CHARACTER_XINGBIE_NV)
				{
					femaleId = fashion.Id;
				}
				else
				{
					// No gender
					maleId = fashion.Id;
					femaleId = fashion.Id;
				}

				yield return new CombinedFashionData(maleId, femaleId, fashion.Name, fashion.Desc, fashion.Icon);
			}
		}

		private IEnumerable<FashionData>? GetFashionList(IProviderManager providerManager, Logger logger)
		{
			if (!providerManager.Provider.TryGetGameFile("WS/Content/Data/DataTables/DT_Fashion.uasset", out GameFile? file))
			{
				logger.Error("Unable to locate asset DT_Fashion.");
				return null;
			}

			Package package = (Package)providerManager.Provider.LoadPackage(file);
			UDataTable? table = package.ExportMap[0].ExportObject.Value as UDataTable;
			if (table is null)
			{
				logger.Error("Error loading DT_Fashion");
				return null;
			}

			List<FashionData> fashionList = new();
			foreach (var pair in table.RowMap)
			{
				if (!int.TryParse(pair.Key.Text, out int id))
				{
					logger.Warning($"Failed to parse ID '{pair.Key.Text}'. Skipping this Fashion.");
					continue;
				}

				string? name = null;
				string? desc = null;
				UTexture2D? icon = null;
				EXingBieType? gender = null;
				foreach (FPropertyTag property in pair.Value.Properties)
				{
					switch (property.Name.Text)
					{
						case "FashionName":
							name = DataUtil.ReadTextProperty(property);
							break;
						case "FashionDesc":
							desc = DataUtil.ReadTextProperty(property);
							break;
						case "XingBie":
							if (DataUtil.TryParseEnum<EXingBieType>(property, out EXingBieType xingBie))
							{
								gender = xingBie;
							}
							break;
						case "FashionIcon":
							icon = DataUtil.ReadTextureProperty(property);
							break;
					}
				}

				if (name is null || icon is null || !gender.HasValue)
				{
					logger.Warning($"Failed to read required properties. Skipping Fashion '{id}'.");
					continue;
				}

				FashionData data = new()
				{
					Id = id,
					Name = name,
					Desc = desc,
					Gender = gender.Value,
					Icon = icon
				};

				fashionList.Add(data);
			}

			if (fashionList.Count == 0)
			{
				logger.Error("Failed to load any Fashion instances");
				return null;
			}

			fashionList.Sort();

			return fashionList;
		}

		private class FashionData : IComparable<FashionData>
		{
			public int Id { get; set; }
			public string Name { get; set; }
			public string? Desc { get; set; }
			public EXingBieType Gender { get; set; }
			public UTexture2D Icon { get; set; }

			public FashionData()
			{
				Name = null!;
				Icon = null!;
			}

			public int CompareTo(FashionData? other)
			{
				return Id.CompareTo(other?.Id);
			}

			public override string ToString()
			{
				return $"[{Id}] {Name}";
			}
		}

		private class CombinedFashionData
		{
			public int MaleId { get; set; }
			public int FemaleId { get; set; }
			public string Name { get; set; }
			public string? Desc { get; set; }
			public UTexture2D Icon { get; set; }

			public CombinedFashionData(int maleId, int femaleId, string name, string? desc, UTexture2D icon)
			{
				MaleId = maleId;
				FemaleId = femaleId;
				Name = name;
				Desc = desc;
				Icon = icon;
			}
		}
	}
}
