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
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.UObject;
using SoulmaskDataMiner.Data;
using SoulmaskDataMiner.GameData;
using SoulmaskDataMiner.IO;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace SoulmaskDataMiner.Miners
{
	/// <summary>
	/// Mines data about character proficiencies
	/// </summary>
	[MinerName("Proficiency")]
	internal class ProficiencyMiner : MinerBase
	{
		public override bool Run(IProviderManager providerManager, Config config, Logger logger, ISqlWriter sqlWriter)
		{
			IEnumerable<ProficiencyData>? proficiencies;
			if (!LoadProficiencyData(providerManager, logger, out proficiencies))
			{
				return false;
			}

			WriteCsv(proficiencies, config, logger);
			WriteSql(proficiencies, sqlWriter, logger);
			WriteTextures(proficiencies, config, logger);

			return true;
		}

		internal static IReadOnlyDictionary<EProficiency, ProficiencyData>? LoadProficiencyMap(IProviderManager providerManager, Logger logger)
		{
			if (!providerManager.Provider.TryGetGameFile("WS/Content/Blueprints/UI/ShuLianDu/WBP_ShuLianDu.uasset", out GameFile? file))
			{
				logger.Error("Unable to locate asset WBP_ShuLianDu.");
				return null;
			}

			Package package = (Package)providerManager.Provider.LoadPackage(file);

			Dictionary<EProficiency, ProficiencyData> proficiencyMap = new();
			foreach (FObjectExport export in package.ExportMap)
			{
				if (!export.ClassName.Equals("WBP_ShuLianDuSingle_C"))
				{
					continue;
				}

				EProficiency? proficiency = null;
				string? name = null;
				UTexture2D? icon = null;

				UObject exportObject = export.ExportObject.Value;
				foreach (FPropertyTag property in exportObject.Properties)
				{
					switch (property.Name.Text)
					{
						case "ShuLianDuType":
							if (DataUtil.TryParseEnum(property, out EProficiency p))
							{
								proficiency = p;
							}
							break;
						case "SLDText":
							name = DataUtil.ReadTextProperty(property);
							break;
						case "SLDImage":
							icon = DataUtil.ReadTextureProperty(property);
							break;
					}
				}

				if (!proficiency.HasValue || name is null)
				{
					logger.Warning("Could not find necessary data from an instance of WBP_ShuLianDuSingle_C to build proficiency information. Skipping this instance.");
					continue;
				}

				if (proficiencyMap.ContainsKey(proficiency.Value))
				{
					logger.Warning($"Found an additional instance of WBP_ShuLianDuSingle_C for the {proficiency.Value} proficiency. Skipping this instance.");
					continue;
				}

				ProficiencyData data = new()
				{
					ID = proficiency.Value,
					Name = name,
					Icon = icon
				};

				proficiencyMap.Add(proficiency.Value, data);
			}

			return proficiencyMap;
		}

		private static bool LoadProficiencyData(IProviderManager providerManager, Logger logger, [NotNullWhen(true)] out IEnumerable<ProficiencyData>? proficiencies)
		{
			IReadOnlyDictionary<EProficiency, ProficiencyData>? proficiencyMap = LoadProficiencyMap(providerManager, logger);
			if (proficiencyMap is null)
			{
				proficiencies = null;
				return false;
			}

			EProficiency[] allProfIds = Enum.GetValues<EProficiency>().Take((int)EProficiency.Max).ToArray();
			ProficiencyData[] allProfs = new ProficiencyData[allProfIds.Length];
			for (int i = 0; i < allProfs.Length; ++i)
			{
				if (proficiencyMap.TryGetValue(allProfIds[i], out ProficiencyData data))
				{
					allProfs[i] = data;
				}
				else
				{
					allProfs[i] = new() { ID = allProfIds[i] };
				}
			}

			proficiencies = allProfs;
			return true;
		}

		private void WriteCsv(IEnumerable<ProficiencyData> proficiencies, Config config, Logger logger)
		{
			string outPath = Path.Combine(config.OutputDirectory, Name, $"{Name}.csv");
			using FileStream stream = IOUtil.CreateFile(outPath, logger);
			using StreamWriter writer = new(stream, Encoding.UTF8);

			writer.WriteLine("idx,id,name,icon");

			foreach (ProficiencyData proficiency in proficiencies)
			{
				writer.WriteLine($"{(int)proficiency.ID},{proficiency.ID},{CsvStr(proficiency.Name)},{CsvStr(proficiency.Icon?.Name)}");
			}
		}

		private void WriteSql(IEnumerable<ProficiencyData> proficiencies, ISqlWriter sqlWriter, Logger logger)
		{
			// Schema
			// create table `sld` (
			//   `id` int not null,
			//   `type` varchar(127) not null,
			//   `name` varchar(127),
			//   `icon` varchar(127),
			//   primary key (`id`)
			// )

			sqlWriter.WriteStartTable("sld");
			foreach (ProficiencyData proficiency in proficiencies)
			{
				sqlWriter.WriteRow($"{(int)proficiency.ID},{DbStr(proficiency.ID.ToString())},{DbStr(proficiency.Name)},{DbStr(proficiency.Icon?.Name)}");
			}
			sqlWriter.WriteEndTable();
		}

		private void WriteTextures(IEnumerable<ProficiencyData> proficiencies, Config config, Logger logger)
		{
			string outDir = Path.Combine(config.OutputDirectory, Name, "icons");
			foreach (ProficiencyData proficiency in proficiencies)
			{
				if (proficiency.Icon is null) continue;
				TextureExporter.ExportTexture(config,proficiency.Icon, false, logger, outDir);
			}
		}
	}

	internal struct ProficiencyData
	{
		public EProficiency ID;
		public string? Name;
		public UTexture2D? Icon;

		public override string ToString()
		{
			return $"[{ID}] {Name}";
		}
	}
}
