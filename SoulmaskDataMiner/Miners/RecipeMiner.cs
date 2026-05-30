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

using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using SoulmaskDataMiner.Data;
using SoulmaskDataMiner.GameData;
using SoulmaskDataMiner.IO;
using System.Text;

namespace SoulmaskDataMiner.Miners
{
	[MinerName("Recipe")]
	internal class RecipeMiner : SubclassMinerBase
	{
		protected override string NameProperty => "PeiFangName";

		protected override string? DescriptionProperty => "TeShuPeiFangYiWen";

		protected override string? IconProperty => "PeiFangIcon";

		protected override IReadOnlySet<string>? AdditionalPropertyNames => sAdditionalPropertyNames;

		private static readonly HashSet<string> sAdditionalPropertyNames;

		private const string BaseClassName_Formula = "HPeiFangBase";

		static RecipeMiner()
		{
			sAdditionalPropertyNames =
			[
				"PeiFangUniqueID",
				"PeiFangDengJi",
				"PeiFangMakeTime",
				"MakeCompleteAddExp",
				"MakeProficiencyType",
				"MakeAddProficiencyExp",
				"DemandDaoJu",
				"DemandMianJuNengLiang",
				"ProduceDaoJu",
				"MatchGongZuoTaiData",
				"HiddenRecipeInGameMode"
			];
		}

		// Row type for the flat recipe table: pairs each recipe with its proficiency group.
		private readonly record struct RecipeRow(EProficiency Prof, RecipeInfo Recipe);

		// Schema
		// create table `recipe`
		// (
		//   `prof` int not null,
		//   `id` varchar(127) not null,
		//   `bench` varchar(511) not null,
		//   `name` varchar(127) not null,
		//   `desc` varchar(511),
		//   `icon` varchar(255),
		//   `level` int not null,
		//   `time` float not null,
		//   `exp` int not null,
		//   `profexp` float not null,
		//   `inputs` varchar(2047) not null,
		//   `energy` int,
		//   `output` varchar(255),
		//   `modemask` tinyint unsigned
		// )
		private static readonly MinerTable<RecipeRow> sRecipeTable = new(
			csvFileName: "Recipe.csv",
			sqlTableName: "recipe",
			columns:
			[
				TableColumn.Int<RecipeRow>("prof", r => (int)r.Prof),
				TableColumn.Str<RecipeRow>("id", r => r.Recipe.UniqueID),
				TableColumn.Str<RecipeRow>("bench", r => FormatWorkbenchesJson(r.Recipe.Workbenches)),
				TableColumn.Str<RecipeRow>("name", r => r.Recipe.Name),
				TableColumn.Str<RecipeRow>("desc", r => r.Recipe.Description),
				TableColumn.Str<RecipeRow>("icon", r => r.Recipe.Icon.Name),
				TableColumn.Int<RecipeRow>("level", r => r.Recipe.Level),
				TableColumn.Float<RecipeRow>("time", r => r.Recipe.CraftTime),
				TableColumn.Int<RecipeRow>("exp", r => r.Recipe.ExpGain),
				TableColumn.Float<RecipeRow>("profexp", r => r.Recipe.ProficiencyExpGain),
				TableColumn.Str<RecipeRow>("inputs", r => FormatInputsJson(r.Recipe.InputItems)),
				TableColumn.NullInt<RecipeRow>("energy", r => r.Recipe.MaskEnergy),
				TableColumn.Str<RecipeRow>("output", r => r.Recipe.OutputItem),
				TableColumn.NullInt<RecipeRow>("modemask", r => r.Recipe.GameModeMask),
			],
			iconSelector: r => r.Recipe.Icon)
		{
			IconSubdir = "Icon",
		};

		// Schema
		// create table `bench`
		// (
		//   `class` varchar(255) not null,
		//   `name` varchar(127) not null,
		//   `icon` varchar(255)
		// )
		private static readonly MinerTable<KeyValuePair<string, WorkbenchInfo>> sWorkbenchTable = new(
			csvFileName: "Workbenches.csv",
			sqlTableName: "bench",
			columns:
			[
				TableColumn.Str<KeyValuePair<string, WorkbenchInfo>>("class", p => p.Key),
				TableColumn.Str<KeyValuePair<string, WorkbenchInfo>>("name", p => p.Value.Name),
				TableColumn.Str<KeyValuePair<string, WorkbenchInfo>>("icon", p => p.Value.Icon?.Name),
			],
			iconSelector: p => p.Value.Icon)
		{
			IconSubdir = "BenchIcon",
		};

		public override bool Run(IProviderManager providerManager, Config config, Logger logger, ISqlWriter sqlWriter)
		{
			IEnumerable<ObjectInfo> formulaObjects = FindObjects(BaseClassName_Formula.AsEnumerable());
			Dictionary<EProficiency, List<RecipeInfo>> recipeMap = new();
			foreach (ObjectInfo formulaObject in formulaObjects)
			{
				RecipeInfo? recipe = RecipeInfo.Create(formulaObject, logger);
				if (recipe is null)
				{
					continue;
				}

				List<RecipeInfo>? recipes;
				if (!recipeMap.TryGetValue(recipe.ProficiencyType, out recipes))
				{
					recipes = new();
					recipeMap.Add(recipe.ProficiencyType, recipes);
				}
				recipes.Add(recipe);
			}

			HashSet<EquatablePackageIndex> workbenchPackageIndices = new();

			foreach (List<RecipeInfo> recipes in recipeMap.Values)
			{
				recipes.Sort();
				foreach (RecipeInfo recipe in recipes)
				{
					foreach (FPackageIndex workbenchIndex in recipe.Workbenches)
					{
						workbenchPackageIndices.Add(new(workbenchIndex));
					}
				}
			}

			Dictionary<string, WorkbenchInfo> workbenchMap = new();
			foreach (EquatablePackageIndex workbenchIndex in workbenchPackageIndices)
			{
				UObject? workbenchObject = workbenchIndex.Value.Load<UBlueprintGeneratedClass>()?.ClassDefaultObject.Load();
				if (workbenchObject is null)
				{
					logger.Warning($"Unable to load workbench {workbenchIndex.Value.Name}");
					continue;
				}
				WorkbenchInfo? workbenchInfo = WorkbenchInfo.Load(workbenchObject);
				if (workbenchInfo is null)
				{
					logger.Debug($"Unable to read workbench {workbenchIndex.Value.Name}");
					continue;
				}
				workbenchMap.Add(workbenchIndex.Value.Name, workbenchInfo);
			}

			IEnumerable<RecipeRow> recipeRows = recipeMap
				.OrderBy(kvp => kvp.Key)
				.SelectMany(kvp => kvp.Value.Select(r => new RecipeRow(kvp.Key, r)));
			IEnumerable<KeyValuePair<string, WorkbenchInfo>> workbenchRows = workbenchMap.OrderBy(kvp => kvp.Key);

			WriteTable(recipeRows, sRecipeTable, config, logger, sqlWriter);
			sqlWriter.WriteEmptyLine();
			WriteTable(workbenchRows, sWorkbenchTable, config, logger, sqlWriter);

			return true;
		}

		private static string FormatWorkbenchesJson(IReadOnlyList<FPackageIndex> workbenches)
		{
			if (workbenches.Count == 0) return "[]";
			return $"[\"{string.Join("\",\"", workbenches.Select(b => b.Name))}\"]";
		}

		private static string FormatInputsJson(IReadOnlyList<RecipeIngredient> inputs)
		{
			if (inputs.Count == 0) return "[]";
			StringBuilder builder = new("[");
			foreach (RecipeIngredient ingredient in inputs)
			{
				builder.Append("{");
				builder.Append($"\"c\":{ingredient.Quantity},");
				builder.Append("\"m\":[");
				builder.Append($"\"{string.Join("\",\"", ingredient.Names)}\"");
				builder.Append("]},");
			}
			--builder.Length; // remove trailing comma
			builder.Append("]");
			return builder.ToString();
		}

		private class RecipeInfo : IComparable<RecipeInfo>
		{
			public string UniqueID { get; }
			public string Name { get; }
			public string? Description { get; }
			public UTexture2D Icon { get; }
			public int Level { get; }
			public float CraftTime { get; }
			public int ExpGain { get; }
			public EProficiency ProficiencyType { get; }
			public float ProficiencyExpGain { get; }
			public IReadOnlyList<RecipeIngredient> InputItems { get; }
			public int? MaskEnergy { get; }
			public string? OutputItem { get; }
			public IReadOnlyList<FPackageIndex> Workbenches { get; }
			public IReadOnlyList<ECustomGameMode> HiddenInGameModes { get; }
			public byte? GameModeMask { get; }

			private RecipeInfo(
				string uniqueId,
				string name,
				string? description,
				UTexture2D icon,
				int level,
				float craftTime,
				int expGain,
				EProficiency proficiencyType,
				float proficiencyExpGain,
				IReadOnlyList<RecipeIngredient> inputItems,
				int? maskEnergy,
				string? outputItem,
				IReadOnlyList<FPackageIndex> workbenches,
				IReadOnlyList<ECustomGameMode> hiddenInGameModes)
			{
				UniqueID = uniqueId;
				Name = name;
				Description = description;
				Icon = icon;
				Level = level;
				CraftTime = craftTime;
				ExpGain = expGain;
				ProficiencyType = proficiencyType;
				ProficiencyExpGain = proficiencyExpGain;
				InputItems = inputItems;
				MaskEnergy = maskEnergy;
				OutputItem = outputItem;
				Workbenches = workbenches;
				HiddenInGameModes = hiddenInGameModes;
				
				GameModeMask = null;
				if (hiddenInGameModes.Count > 0)
				{
					byte mask = GameEnumExtensions.AllGameModesMask;
					foreach (ECustomGameMode mode in hiddenInGameModes)
					{
						mask &= (byte)~mode.CreateMask();
					}
					GameModeMask = mask;
				}
			}

			public static RecipeInfo? Create(ObjectInfo info, Logger logger)
			{
				string? uniqueId = null;
				int level = 0;
				float craftTime = 0.0f;
				int expGain = 0;
				EProficiency proficiencyType = EProficiency.Max;
				float proficiencyExpGain = 0.0f;
				List<RecipeIngredient> inputItems = new();
				int? maskEnergy = null;
				string? outputItem = null;
				List<FPackageIndex> workbenches = new();
				List<ECustomGameMode> hiddenInGameModes = new();

				foreach (var pair in info.AdditionalProperties!)
				{
					switch (pair.Key)
					{
						case "PeiFangUniqueID":
							uniqueId = pair.Value.Tag!.GetValue<FName>().Text;
							break;
						case "PeiFangDengJi":
							level = pair.Value.Tag!.GetValue<int>();
							break;
						case "PeiFangMakeTime":
							craftTime = pair.Value.Tag!.GetValue<float>();
							break;
						case "MakeCompleteAddExp":
							expGain = pair.Value.Tag!.GetValue<int>();
							break;
						case "MakeProficiencyType":
							DataUtil.TryParseEnum(pair.Value, out proficiencyType);
							break;
						case "MakeAddProficiencyExp":
							proficiencyExpGain = pair.Value.Tag!.GetValue<float>();
							break;
						case "DemandDaoJu":
							{
								UScriptArray? ingredientArray = pair.Value.Tag?.GetValue<UScriptArray>();
								if (ingredientArray is not null)
								{
									foreach (FPropertyTagType ingredientItem in ingredientArray.Properties)
									{
										FStructFallback? ingredientStruct = ((FStructFallback?)ingredientItem.GetValue<FScriptStruct>()?.StructType);
										RecipeIngredient? ingredient = null;
										if (ingredientStruct is not null)
										{
											ingredient = RecipeIngredient.Load(ingredientStruct);
										}
										if (ingredient is null)
										{
											logger.Warning($"Unable to read ingredient for recipe \"{info.Name}\"");
											continue;
										}
										inputItems.Add(ingredient);
									}
								}
							}
							break;
						case "DemandMianJuNengLiang":
							maskEnergy = pair.Value.Tag?.GetValue<int>();
							break;
						case "ProduceDaoJu":
							outputItem = pair.Value.Tag?.GetValue<FPackageIndex>()?.Name;
							break;
						case "MatchGongZuoTaiData":
							{
								UScriptArray? benchArray = pair.Value.Tag?.GetValue<UScriptArray>();
								if (benchArray is not null)
								{
									foreach (FPropertyTagType benchItem in benchArray.Properties)
									{
										UScriptArray? benchMatchList = ((FStructFallback?)benchItem.GetValue<FScriptStruct>()?.StructType)?.Properties.FirstOrDefault(p => p.Name.Text.Equals("MustMatchGongZuoTaiList"))?.Tag?.GetValue<UScriptArray>();
										if (benchMatchList is null) continue;

										foreach (FPropertyTagType benchMatchItem in benchMatchList.Properties)
										{
											FPackageIndex? workbench = benchMatchItem.GetValue<FPackageIndex>();
											if (workbench is null)
											{
												logger.Warning($"Unable to read workbench for recipe \"{info.Name}\"");
												continue;
											}
											workbenches.Add(workbench);
										}
									}
								}
							}
							break;
						case "HiddenRecipeInGameMode":
							{
								UScriptArray? hiddenModeArray = pair.Value.Tag?.GetValue<UScriptArray>();
								if (hiddenModeArray is not null)
								{
									foreach (FPropertyTagType hiddenModeItem in hiddenModeArray.Properties)
									{
										if (DataUtil.TryParseEnum(hiddenModeItem, out ECustomGameMode mode))
										{
											hiddenInGameModes.Add(mode);
										}
									}
								}
							}
							break;
					}
				}

				if (info.Name is null || info.Icon is null || uniqueId is null)
				{
					logger.Log(LogLevel.Verbose, $"Missing required property for recipe \"{info.Name}\"");
					return null;
				}

				if (inputItems.Count == 0 && outputItem is null && !maskEnergy.HasValue)
				{
					logger.Debug($"Skipping recipe \"{info.Name}\" because it has no inputs or output.");
					return null;
				}

				return new(
					uniqueId,
					info.Name,
					info.Description,
					info.Icon,
					level,
					craftTime,
					expGain,
					proficiencyType,
					proficiencyExpGain,
					inputItems,
					maskEnergy,
					outputItem,
					workbenches,
					hiddenInGameModes);
			}

			public int CompareTo(RecipeInfo? other)
			{
				if (other is null) return 1;

				int nameComp = Name.CompareTo(other.Name);
				if (nameComp != 0) return nameComp;

				return UniqueID.CompareTo(other.UniqueID);
			}

			public override string ToString()
			{
				return $"[{UniqueID}] {Name}";
			}
		}

		private class RecipeIngredient
		{
			public IReadOnlyList<string> Names { get; }
			public int Quantity { get; }

			public RecipeIngredient(IReadOnlyList<string> names, int quantity)
			{
				Names = names;
				Quantity = quantity;
			}

			public static RecipeIngredient? Load(IPropertyHolder data)
			{
				List<string>? names = new();
				int quantity = 1;
				foreach (FPropertyTag property in data.Properties)
				{
					switch (property.Name.Text)
					{
						case "DemandDaoJu":
							{
								UScriptArray? itemArray = property.Tag?.GetValue<UScriptArray>();
								if (itemArray is not null)
								{
									foreach (FPropertyTagType item in itemArray.Properties)
									{
										string? itemName = item.GetValue<FPackageIndex>()?.Name;
										if (itemName is not null)
										{
											names.Add(itemName);
										}
									}
								}
							}
							break;
						case "DemandCount":
							quantity = property.Tag!.GetValue<int>();
							break;
					}
				}

				return names.Count == 0 ? null : new(names, quantity);
			}

			public override string ToString()
			{
				return string.Join(" | ", Names);
			}
		}

		private class WorkbenchInfo
		{
			public static WorkbenchInfo Unknown { get; } = new WorkbenchInfo("Unknown", null);

			public string Name { get; }
			public UTexture2D? Icon { get; }

			public WorkbenchInfo(string name, UTexture2D? icon)
			{
				Name = name;
				Icon = icon;
			}

			public static WorkbenchInfo? Load(IPropertyHolder data)
			{
				string? name = null;
				UTexture2D? icon = null;
				foreach (FPropertyTag property in data.Properties)
				{
					switch (property.Name.Text)
					{
						case "JianZhuDisplayName":
							name = DataUtil.ReadTextProperty(property);
							break;
						case "JianZhuIcon":
							icon = DataUtil.ReadTextureProperty(property);
							break;
					}
				}

				return name is null ? null : new(name, icon);
			}

			public override int GetHashCode()
			{
				return Name.GetHashCode();
			}

			public override bool Equals(object? obj)
			{
				return obj is WorkbenchInfo other && Name.Equals(other.Name);
			}

			public override string ToString()
			{
				return Name;
			}
		}
	}
}
