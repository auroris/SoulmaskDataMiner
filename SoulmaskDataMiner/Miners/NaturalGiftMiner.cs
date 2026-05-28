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
using CUE4Parse.UE4.Objects.UObject;
using SoulmaskDataMiner.Data;
using SoulmaskDataMiner.IO;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace SoulmaskDataMiner.Miners
{
	/// <summary>
	/// Mines data about natural gifts (talents)
	/// </summary>
	[MinerName("Gift")]
	internal class NaturalGiftMiner : MinerBase
	{
		public override bool Run(IProviderManager providerManager, Config config, Logger logger, ISqlWriter sqlWriter)
		{
			if (!TryFindGifts(providerManager, config, logger, out IReadOnlyDictionary<ENaturalGiftSource, List<CombinedGiftData>>? combinedGifts))
			{
				return false;
			}

			WriteCsv(combinedGifts, config, logger);

			string sqlPath = Path.Combine(config.OutputDirectory, Name, "NaturalGifts.sql");
			WriteSql(combinedGifts, sqlWriter, logger);

			WriteTextures(combinedGifts, config, logger);

			return true;
		}

		private bool TryFindGifts(IProviderManager providerManager, Config config, Logger logger, [NotNullWhen(true)] out IReadOnlyDictionary<ENaturalGiftSource, List<CombinedGiftData>>? combinedGifts)
		{
			if (!providerManager.Provider.TryGetGameFile("WS/Content/Blueprints/DataTable/NaturalGift/DT_GiftZongBiao.uasset", out GameFile? file))
			{
				logger.Error("Unable to locate natural gift data table.");
				combinedGifts = null;
				return false;
			}

			Package package = (Package)providerManager.Provider.LoadPackage(file);
			UDataTable table = (UDataTable)package.ExportMap[0].ExportObject.Value;

			Dictionary<ENaturalGiftSource, List<GiftData>> gifts = new();

			foreach (var row in table.RowMap)
			{
				int id;
				if (!int.TryParse(row.Key.Text, out id))
				{
					logger.Warning($"Natural gift table row key is not a valid integer: {row.Key.Text}");
					continue;
				}

				bool isGood = IsGood(id);

				int level = 0;
				ENaturalGiftSource source = ENaturalGiftSource.Normal;
				string? title = null, description = null;
				UTexture2D? icon = null;

				foreach (FPropertyTag property in row.Value.Properties)
				{
					switch (property.Name.Text)
					{
						case "Star":
							level = property.Tag!.GetValue<int>();
							break;
						case "NGEffectSource":
							source = ParseSource(property.Tag!.GetValue<FName>()!.Text, id, logger);
							break;
						case "Title":
							title = DataUtil.ReadTextProperty(property);
							break;
						case "Desc":
							description = DataUtil.ReadTextProperty(property);
							break;
						case "Pic":
							icon = DataUtil.ReadTextureProperty(property);
							break;
					}
				}

				List<GiftData>? list;
				if (!gifts.TryGetValue(source, out list))
				{
					list = new();
					gifts.Add(source, list);
				}
				list.Add(new()
				{
					ID = id,
					Level = level,
					IsGood = isGood,
					Title = title,
					Description = description,
					Icon = icon
				});
			}

			DescriptionComparer descriptionComparer = new();
			Dictionary<ENaturalGiftSource, List<CombinedGiftData>> combined = new();
			foreach (var pair in gifts)
			{
				pair.Value.Sort();

				List<CombinedGiftData>? combinedList;
				if (!combined.TryGetValue(pair.Key, out combinedList))
				{
					combinedList = new();
					combined.Add(pair.Key, combinedList);
				}

				List<GiftData> giftsToCombine = new();
				for (int i = 1; i < pair.Value.Count; ++i)
				{
					GiftData a = pair.Value[i - 1];
					GiftData b = pair.Value[i];

					giftsToCombine.Add(a);

					if (!string.Equals(a.Title, b.Title, StringComparison.OrdinalIgnoreCase) ||
						!descriptionComparer.Equals(a.Description, b.Description) ||
						!string.Equals(a.Icon?.Name, b.Icon?.Name, StringComparison.OrdinalIgnoreCase))
					{
						combinedList.Add(CombinedGiftData.CombineGifts(giftsToCombine, logger));
						giftsToCombine.Clear();
					}

					if (i == pair.Value.Count - 1)
					{
						giftsToCombine.Add(b);
						combinedList.Add(CombinedGiftData.CombineGifts(giftsToCombine, logger));
					}
				}
			}

			combinedGifts = combined;
			return true;
		}

		private void WriteCsv(IReadOnlyDictionary<ENaturalGiftSource, List<CombinedGiftData>> combinedGifts, Config config, Logger logger)
		{
			foreach (var pair in combinedGifts)
			{
				string outPathGood = Path.Combine(config.OutputDirectory, Name, $"Good_{TranslateGiftSource(pair.Key)}.csv");
				string outPathBad = Path.Combine(config.OutputDirectory, Name, $"Bad_{TranslateGiftSource(pair.Key)}.csv");
				using FileStream streamGood = IOUtil.CreateFile(outPathGood, logger);
				using FileStream streamBad = IOUtil.CreateFile(outPathBad, logger);
				using StreamWriter writerGood = new(streamGood, Encoding.UTF8);
				using StreamWriter writerBad = new(streamBad, Encoding.UTF8);

				writerGood.WriteLine("Level 1,Level 2, Level 3,Title,Description,Icon");
				writerBad.WriteLine("Level 1,Level 2, Level 3,Title,Description,Icon");

				foreach (CombinedGiftData gift in pair.Value)
				{
					StreamWriter writer = gift.IsGood ? writerGood : writerBad;
					writer.WriteLine($"{gift.Level1},{gift.Level2},{gift.Level3},{CsvStr(gift.Title)},{CsvStr(gift.Description)},{gift.Icon?.Name}");
				}
			}
		}

		private void WriteSql(IReadOnlyDictionary<ENaturalGiftSource, List<CombinedGiftData>> combinedGifts, ISqlWriter sqlWriter, Logger logger)
		{
			// Schema
			// create table `ng` (
			//   `positive` bool,
			//   `source` int,
			//   `id1` int,
			//   `id2` int,
			//   `id3` int,
			//   `title` varchar(255) not null,
			//   `description` varchar(1023),
			//   `icon` varchar(255)
			// )

			sqlWriter.WriteStartTable("ng");
			foreach (var pair in combinedGifts)
			{
				foreach (CombinedGiftData gift in pair.Value)
				{
					sqlWriter.WriteRow($"{gift.IsGood}, {(int)pair.Key}, {DbVal(gift.Level1)}, {DbVal(gift.Level2)}, {DbVal(gift.Level3)}, {DbStr(gift.Title)}, {DbStr(gift.Description)}, {DbStr(gift.Icon?.Name)}");
				}
			}
			sqlWriter.WriteEndTable();
		}

		private void WriteTextures(IReadOnlyDictionary<ENaturalGiftSource, List<CombinedGiftData>> combinedGifts, Config config, Logger logger)
		{
			HashSet<string> seenTextures = new();
			string outDir = Path.Combine(config.OutputDirectory, Name, "icons");
			foreach (var pair in combinedGifts)
			{
				foreach (CombinedGiftData data in pair.Value)
				{
					if (data.Icon is null) continue;
					if (!seenTextures.Add(data.Icon!.Name)) continue;

					TextureExporter.ExportTexture(config,data.Icon!, false, logger, outDir);
				}
			}
		}

		private static ENaturalGiftSource ParseSource(string text, int id, Logger logger)
		{
			text = text[(text.LastIndexOf(':') + 1)..];
			if (!Enum.TryParse<ENaturalGiftSource>(text, out ENaturalGiftSource result))
			{
				logger.Warning($"Unable to parse gift source \"{text}\" for gift {id}");
			}
			return result;
		}

		private static bool IsGood(int id)
		{
			return id < 200000
				|| id >= 300000 && id < 510000
				|| id >= 600000 && id < 900000 && id != 600051 && id != 600054 && id != 600056 && id != 600058;
		}

		private static string TranslateGiftSource(ENaturalGiftSource source)
		{
			return source switch
			{
				ENaturalGiftSource.Normal => "Normal",
				ENaturalGiftSource.BornChuShen => "Origin",
				ENaturalGiftSource.BornBuLuoCiTiao => "Tribe",
				ENaturalGiftSource.ChengHao => "Title",
				ENaturalGiftSource.JingLi => "Experience",
				ENaturalGiftSource.XiHao => "Preference",
				ENaturalGiftSource.XingGe => "Personality",
				ENaturalGiftSource.GuanXi => "Relationship",
				_ => "Unknown"
			};
		}



		private struct GiftData : IComparable<GiftData>
		{
			public int ID;
			public int Level;

			public bool IsGood;

			public string? Title;
			public string? Description;

			public UTexture2D? Icon;

			public int CompareTo(GiftData other)
			{
				return ID.CompareTo(other.ID);
			}

			public override string ToString()
			{
				return $"[{ID}] ({(IsGood ? "Good" : "Bad")}) {Title}";
			}
		}

		private struct CombinedGiftData : IComparable<CombinedGiftData>
		{
			private const string sRegex = @"(\d*\.?\d+)";

			public int? Level1;
			public int? Level2;
			public int? Level3;

			public bool IsGood;

			public string? Title;
			public string? Description;

			public UTexture2D? Icon;

			public static CombinedGiftData CombineGifts(IReadOnlyList<GiftData> gifts, Logger logger)
			{
				if (gifts.Count == 0)
				{
					throw new DataMinerException("Error combining gifts. Collection is empty.");
				}

				string? description;
				if (gifts.Count == 1 || gifts[0].Description is null)
				{
					description = gifts[0].Description;
				}
				else
				{
					MatchCollection[] matches = new MatchCollection[gifts.Count];

					for (int i = 0; i < gifts.Count; ++i)
					{
						matches[i] = Regex.Matches(gifts[i].Description!, sRegex);
					}

					int count = matches[0].Count;
					if (matches.Skip(1).Any(m => m.Count != count))
					{
						throw new DataMinerException("Error combining gifts. Descriptions do not match.");
					}

					StringBuilder builder = new();
					int lastIndex = 0;
					for (int i = 0; i < count; ++i)
					{
						builder.Append(gifts[0].Description!.Substring(lastIndex, matches[0][i].Index - lastIndex));
						lastIndex = matches[0][i].Index + matches[0][i].Length;

						string firstValue = matches[0][i].Value;
						if (matches.Skip(1).All(mc => mc[i].Value == firstValue))
						{
							builder.Append(firstValue);
						}
						else
						{
							builder.Append("[");
							foreach (MatchCollection mc in matches)
							{
								builder.Append($"{mc[i].Value},");
							}
							builder.Length = builder.Length - 1;
							builder.Append("]");
						}
					}
					builder.Append(gifts[0].Description!.Substring(lastIndex));

					description = builder.ToString();
				}

				int? level1 = null, level2 = null, level3 = null;
				foreach (GiftData gift in gifts)
				{
					switch (gift.Level)
					{
						case 1:
							if (level1.HasValue) logger.Warning($"Found duplicated gifts: {level1.Value} and {gift.ID}");
							else level1 = gift.ID;
							break;
						case 2:
							if (level2.HasValue) logger.Warning($"Found duplicated gifts: {level2.Value} and {gift.ID}");
							else level2 = gift.ID;
							break;
						case 3:
							if (level3.HasValue) logger.Warning($"Found duplicated gifts: {level3.Value} and {gift.ID}");
							else level3 = gift.ID;
							break;
					}
				}

				CombinedGiftData instance = new()
				{
					Level1 = level1,
					Level2 = level2,
					Level3 = level3,
					IsGood = gifts[0].IsGood,
					Title = gifts[0].Title,
					Description = description,
					Icon = gifts[0].Icon
				};

				return instance;
			}

			public int CompareTo(CombinedGiftData other)
			{
				int a = Level1.HasValue ? Level1.Value : Level2.HasValue ? Level2.Value : Level3.HasValue ? Level3.Value : 0;
				int b = other.Level1.HasValue ? other.Level1.Value : other.Level2.HasValue ? other.Level2.Value : other.Level3.HasValue ? other.Level3.Value : 0;
				return a.CompareTo(b);
			}

			public override string ToString()
			{
				return $"[{Level1},{Level2},{Level3}] ({(IsGood ? "Good" : "Bad")}) {Title}";
			}
		}

		private enum ENaturalGiftSource
		{
			Normal,
			BornChuShen,
			BornBuLuoCiTiao,
			ChengHao,
			JingLi,
			XiHao,
			XingGe,
			GuanXi
		};

		private class DescriptionComparer : IEqualityComparer<string>
		{
			private const string sRegex = @"\d*\.?\d+";

			public int GetHashCode([DisallowNull] string obj)
			{
				string val = Regex.Replace(obj, sRegex, string.Empty);
				return val.ToLowerInvariant().GetHashCode();
			}

			public bool Equals(string? x, string? y)
			{
				if (x is null) return y is null;
				if (y is null) return false;

				string xVal = Regex.Replace(x, sRegex, string.Empty);
				string yVal = Regex.Replace(y, sRegex, string.Empty);
				return string.Equals(xVal, yVal, StringComparison.OrdinalIgnoreCase);
			}
		}
	}
}
