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
	/// Mines data about character attributes
	/// </summary>
	[MinerName("Attribute"), RequireClassData(true)]
	internal class AttributeMiner : MinerBase
	{
		public override bool Run(IProviderManager providerManager, Config config, Logger logger, ISqlWriter sqlWriter)
		{
			IEnumerable<AttributeData>? attributes;
			if (!FindAttributeClasses(providerManager, logger, out attributes))
			{
				return false;
			}
			if (!FindAttributeData(attributes, providerManager, logger))
			{
				return false;
			}

			WriteCsv(attributes, config, logger);
			WriteSql(attributes, sqlWriter, logger);
			WriteTextures(attributes, config, logger);

			return true;
		}

		private bool FindAttributeClasses(IProviderManager providerManager, Logger logger, [NotNullWhen(true)] out IEnumerable<AttributeData>? attributes)
		{
			string[] classNames = new string[]
			{
				"HSuperCommonSet",
				"HSuperStateSet",
				"HBuWeiShangHaiAttriSet"
			};

			List<AttributeData> attrList = new();

			foreach (string className in classNames)
			{
				MetaClass mclass;
				if (!providerManager.ClassMetadata!.TryGetValue(className, out mclass))
				{
					logger.Error($"Failed to locate class {className} in class metadata.");
					attributes = null;
					return false;
				}

				foreach (MetaClassProperty property in mclass.Properties)
				{
					if (!property.Type.Equals("FGameplayAttributeData")) continue;

					attrList.Add(new(property.Name));
				}
			}

			attributes = attrList;
			return true;
		}

		private bool FindAttributeData(IEnumerable<AttributeData> attributes, IProviderManager providerManager, Logger logger)
		{
			if (!providerManager.Provider.TryGetGameFile("WS/Content/Blueprints/UI/ShiTu/WBP_ShiTu.uasset", out GameFile? file))
			{
				logger.Error("Unable to locate asset WBP_ShiTu.");
				return false;
			}

			Package package = (Package)providerManager.Provider.LoadPackage(file);

			foreach (AttributeData attr in attributes)
			{
				if (providerManager.GameTextTable.TryGetValue(attr.ClassName, out string? name))
				{
					attr.DisplayName = name;
				}
			}

			Dictionary<EAttrType, AttributeData> attrMap = new();
			foreach (AttributeData attr in attributes)
			{
				if (Enum.TryParse(attr.ClassName, true, out EAttrType result))
				{
					attrMap.Add(result, attr);
				}
			}

			foreach (FObjectExport export in package.ExportMap)
			{
				if (!export.ClassName.Equals("WBP_ShiTuXiangXiShuXingZiUI_C")) continue;

				UObject obj = export.ExportObject.Value;

				EAttrType? curAttr = null, maxAttr = null;
				string? displayName = null;
				UTexture2D? icon = null;

				foreach (FPropertyTag property in obj.Properties)
				{
					switch (property.Name.Text)
					{
						case "ShuXingTuPian":
							icon = DataUtil.ReadTextureProperty(property);
							break;
						case "ShuXingMing":
							displayName = DataUtil.ReadTextProperty(property);
							break;
						case "ShuXingLeiXing":
							{
								if (DataUtil.TryParseEnum(property, out EAttrType result))
								{
									curAttr = result;
								}
							}
							break;
						case "ZuiDaShuXingLeiXing":
							{
								if (DataUtil.TryParseEnum(property, out EAttrType result))
								{
									maxAttr = result;
								}
							}
							break;
					}
				}

				if (!curAttr.HasValue && !maxAttr.HasValue)
				{
					logger.Warning($"UI element {obj.Name} does not specify an attribute type.");
					continue;
				}

				void updateAttrData(EAttrType attr)
				{
					if (attrMap.TryGetValue(attr, out AttributeData? data))
					{
						// If name was found in name table, don't override it with UI text.
						if (data.DisplayName is null)
						{
							data.DisplayName = displayName;
						}
						data.Icon = icon;
					}
					else
					{
						logger.Warning($"UI element {obj.Name} refers to attribute {attr} which was not found.");
					}
				}

				if (curAttr.HasValue)
				{
					updateAttrData(curAttr.Value);
				}
				if (maxAttr.HasValue)
				{
					updateAttrData(maxAttr.Value);
				}
			}

			return true;
		}

		private void WriteCsv(IEnumerable<AttributeData> attributes, Config config, Logger logger)
		{
			string outPath = Path.Combine(config.OutputDirectory, Name, $"{Name}.csv");
			using FileStream stream = IOUtil.CreateFile(outPath, logger);
			using StreamWriter writer = new(stream, Encoding.UTF8);

			writer.WriteLine("class,desc");

			foreach (AttributeData attribute in attributes)
			{
				writer.WriteLine($"{CsvStr(attribute.ClassName)},");
			}
		}

		private void WriteSql(IEnumerable<AttributeData> attributes, ISqlWriter sqlWriter, Logger logger)
		{
			// Schema
			// create table `attr` (
			//   `idx` int not null,
			//   `class` varchar(63) not null,
			//   `name` varchar(63),
			//   `icon` varchar(63),
			//   primary key (`idx`)
			// )

			sqlWriter.WriteStartTable("attr");
			int i = 0;
			foreach (AttributeData attribute in attributes)
			{
				sqlWriter.WriteRow($"{i++}, {DbStr(attribute.ClassName)}, {DbStr(attribute.DisplayName)}, {DbStr(attribute.Icon?.Name)}");
			}
			sqlWriter.WriteEndTable();
		}

		private void WriteTextures(IEnumerable<AttributeData> attributes, Config config, Logger logger)
		{
			string outDir = Path.Combine(config.OutputDirectory, Name, "icons");
			foreach (AttributeData attr in attributes)
			{
				if (attr.Icon is null) continue;
				TextureExporter.ExportTexture(config,attr.Icon, false, logger, outDir);
			}
		}

		private class AttributeData
		{
			public string ClassName { get; }
			public string? DisplayName { get; set; }
			public UTexture2D? Icon { get; set; }

			public AttributeData(string className)
			{
				ClassName = className;
			}
		}
	}
}
