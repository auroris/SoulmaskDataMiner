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
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.UObject;
using SoulmaskDataMiner.Data;
using SoulmaskDataMiner.GameData;
using SoulmaskDataMiner.IO;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace SoulmaskDataMiner.Miners
{
	/// <summary>
	/// Mines data about game coefficient settings
	/// </summary>
	[MinerName("XiShu")]
	internal class XiShuMiner : MinerBase
	{
		public override bool Run(IProviderManager providerManager, Config config, Logger logger, ISqlWriter sqlWriter)
		{
			if (!TryFindXishu(providerManager, logger, out IReadOnlyDictionary<int, List<XishuData>>? xishuMap))
			{
				return false;
			}

			WriteCsv(xishuMap, config, logger);
			WriteSql(xishuMap, sqlWriter, logger);

			return true;
		}

		private bool TryFindXishu(IProviderManager providerManager, Logger logger, [NotNullWhen(true)] out IReadOnlyDictionary<int, List<XishuData>>? xishuMap)
		{
			AssetsData assetsData;
			if (!TryLoadAssets(providerManager, logger, out assetsData))
			{
				xishuMap = null;
				return false;
			}

			Dictionary<int, List<XishuData>> xishu = new();

			foreach (var pair in assetsData.ConfigMap)
			{
				int group = ((IntProperty)pair.Key).Value;
				List<XishuData>? list;
				if (!xishu.TryGetValue(group, out list))
				{
					list = new();
					xishu.Add(group, list);
				}

				ArrayProperty valueProp = (ArrayProperty)((FStructFallback)((StructProperty)pair.Value!).Value!.StructType).Properties[0].Tag!;
				List<FPropertyTagType> values = valueProp.Value!.Properties;

				for (int i = 0; i < values.Count; ++i)
				{
					StructProperty value = (StructProperty)values[i];

					XishuData xishuData = XishuData.Parse(((FStructFallback)value.Value!.StructType).Properties);
					xishuData.Index = i;

					string? xishuName;
					if (providerManager.GameTextTable.TryGetValue(xishuData.Name, out xishuName))
					{
						xishuData.Description = xishuName;
					}

					FName xishuTip;
					if (assetsData.TipsNameMap.TryGetValue(xishuData.Name, out xishuTip))
					{
						xishuData.Tip = DataUtil.ReadTextProperty(assetsData.TipsTable.RowMap[xishuTip].Properties[0]);
					}

					list.Add(xishuData);
				}
			}

			xishuMap = xishu;
			return true;
		}

		private bool TryLoadAssets(IProviderManager providerManager, Logger logger, out AssetsData assetsData)
		{
			assetsData = default;

			Dictionary<FPropertyTagType, FPropertyTagType?>? configMap = null;
			UDataTable? tipsTable = null;
			Dictionary<string, FName> tipsNameMap = new(StringComparer.OrdinalIgnoreCase);

			{
				if (!providerManager.Provider.TryGetGameFile("WS/Content/Blueprints/ZiYuanGuanLi/BP_GameXiShu_GuanLiQi.uasset", out GameFile? file))
				{
					logger.Error("Unable to locate asset BP_GameXiShu_GuanLiQi.");
					return false;
				}

				Package package = (Package)providerManager.Provider.LoadPackage(file);

				foreach (FObjectExport export in package.ExportMap)
				{
					if (export.ClassName.Equals("BP_GameXiShu_GuanLiQi_C"))
					{
						UObject classDefaultObject = export.ExportObject.Value;
						foreach (FPropertyTag prop in classDefaultObject.Properties)
						{
							if (prop.Name.Text.Equals("GameXiShuConfigMap"))
							{
								if (prop.Tag is MapProperty map)
								{
									configMap = map.Value?.Properties;
								}
								break;
							}
						}
						break;
					}
				}
			}
			if (configMap is null)
			{
				logger.Error("Unable to read GameXiShuConfigMap from asset BP_GameXiShu_GuanLiQi");
				return false;
			}

			{
				if (!providerManager.Provider.TryGetGameFile("WS/Content/Blueprints/ZiYuanGuanLi/DT_XiShuTipsText.uasset", out GameFile? file))
				{
					logger.Error("Unable to locate asset DT_XiShuTipsText.");
					return false;
				}

				Package package = (Package)providerManager.Provider.LoadPackage(file);
				tipsTable = package.ExportMap[0].ExportObject.Value as UDataTable;

				if (tipsTable is null)
				{
					logger.Error("Unable to read data from asset DT_XiShuTipsText");
					return false;
				}

				foreach (FName name in tipsTable.RowMap.Keys)
				{
					tipsNameMap.Add(name.Text, name);
				}
			}

			assetsData = new()
			{
				ConfigMap = configMap,
				TipsTable = tipsTable,
				TipsNameMap = tipsNameMap
			};
			return true;
		}

		private void WriteCsv(IReadOnlyDictionary<int, List<XishuData>> xishuMap, Config config, Logger logger)
		{
			foreach (var pair in xishuMap)
			{
				string outPath = Path.Combine(config.OutputDirectory, Name, $"{Name}_{pair.Key}.csv");
				using FileStream stream = IOUtil.CreateFile(outPath, logger);
				using StreamWriter writer = new(stream, Encoding.UTF8);

				writer.WriteLine("name,category,index,toggle,visible,desc,tip,min,max,default,casual,easy,normal,hard,master");

				foreach (XishuData xishu in pair.Value)
				{
					writer.WriteLine($"{CsvStr(xishu.Name)},{(int)xishu.Category},{xishu.Index},{xishu.IsToggle},{xishu.IsVisible},{CsvStr(xishu.Description)},{CsvStr(xishu.Tip)},{xishu.Min},{xishu.Max},{xishu.Default},{xishu.Casual},{xishu.Easy},{xishu.Normal},{xishu.Hard},{xishu.Master}");
				}
			}

			{
				string outPath = Path.Combine(config.OutputDirectory, Name, $"{Name}_Template.csv");
				using FileStream stream = IOUtil.CreateFile(outPath, logger);
				using StreamWriter writer = new(stream, Encoding.UTF8);

				writer.WriteLine("name,description,span");

				List<XishuData> xishus = xishuMap.First().Value;
				foreach (XishuData xishu in xishus)
				{
					writer.WriteLine($"{xishu.Name},,1");
				}
			}
		}

		private void WriteSql(IReadOnlyDictionary<int, List<XishuData>> xishuMap, ISqlWriter sqlWriter, Logger logger)
		{
			// Schema
			// create table `xishu` (
			//   `name` varchar(127) not null,
			//   `group` int not null,
			//   `index` int not null,
			//   `category` int not null,
			//   `toggle` bool not null,
			//   `visible` bool not null,
			//   `desc` varchar(255) not null,
			//   `tip` varchar(255),
			//   `min` float not null,
			//   `max` float not null,
			//   `default` float not null,
			//   `casual` float not null,
			//   `easy` float not null,
			//   `normal` float not null,
			//   `hard` float not null,
			//   `master` float not null,
			//   primary key (`name`, `group`)
			// )

			// We will move through the lists in parallel and write the same item from each list before moving to the next item.
			// This gives us a nicer ordering of statements.

			// Typing enumerators as interface to force boxing
			var enumerators = new Dictionary<int, IEnumerator>(xishuMap.Count);
			foreach (var pair in xishuMap)
			{
				enumerators[pair.Key] = pair.Value.GetEnumerator();
			}

			sqlWriter.WriteStartTable("xishu");
			while (enumerators.All(e => e.Value.MoveNext()))
			{
				foreach (var pair in enumerators)
				{
					XishuData xishu = (XishuData)pair.Value.Current;
					sqlWriter.WriteRow($"{DbStr(xishu.Name)}, {pair.Key}, {xishu.Index}, {(int)xishu.Category}, {xishu.IsToggle}, {xishu.IsVisible}, {DbStr(xishu.Description)}, {DbStr(xishu.Tip)}, {xishu.Min}, {xishu.Max}, {xishu.Default}, {xishu.Casual}, {xishu.Easy}, {xishu.Normal}, {xishu.Hard}, {xishu.Master}");
				}
			}
			sqlWriter.WriteEndTable();
		}

		private struct AssetsData
		{
			public Dictionary<FPropertyTagType, FPropertyTagType?> ConfigMap;
			public UDataTable TipsTable;
			public Dictionary<string, FName> TipsNameMap;
		}

		private struct XishuData
		{
			public int Index;

			public EGameXiShuType Category;
			public bool IsToggle;
			public bool IsVisible;

			public string Name;
			public string Description;
			public string? Tip;
			
			public float Min;
			public float Max;
			public float Default;

			public float Casual;
			public float Easy;
			public float Normal;
			public float Hard;
			public float Master;
			public float Custom;

			public static XishuData Parse(IEnumerable<FPropertyTag> properties)
			{
				XishuData xishu = new();

				int defaultsIndex = 0;
				foreach (FPropertyTag property in properties)
				{
					switch (property.Name.Text)
					{
						case "XiShuFenLei":
							{
								string text = property.Tag!.GetValue<FName>().Text;
								text = text.Substring(text.LastIndexOf(':') + 1);
								xishu.Category = Enum.Parse<EGameXiShuType>(text);
							}
							break;
						case "IsKaiGuan":
							xishu.IsToggle = property.Tag!.GetValue<bool>();
							break;
						case "IsShow":
							xishu.IsVisible = property.Tag!.GetValue<bool>();
							break;
						case "XiShuKey":
							xishu.Name = property.Tag!.GetValue<string>()!;
							break;
						case "Description":
							xishu.Description = property.Tag!.GetValue<string>()!;
							break;
						case "XiShuMinValue":
							xishu.Min = property.Tag!.GetValue<float>();
							break;
						case "XiShuMaxValue":
							xishu.Max = property.Tag!.GetValue<float>();
							break;
						case "XiShuDefaultValue":
							xishu.Default = property.Tag!.GetValue<float>();
							break;
						case "BuTongNanDu_XiShuDefaultValue":
							{
								float value = property.Tag!.GetValue<float>();
								switch (defaultsIndex)
								{
									case 0:
										xishu.Casual = value;
										break;
									case 1:
										xishu.Easy = value;
										break;
									case 2:
										xishu.Normal = value;
										break;
									case 3:
										xishu.Hard = value;
										break;
									case 4:
										xishu.Master = value;
										break;
									case 5:
										xishu.Custom = value;
										break;
								}
								++defaultsIndex;
							}
							break;
					}
				}

				return xishu;
			}

			public override string ToString()
			{
				return $"[{Name}] {Description}";
			}
		}
	}
}
