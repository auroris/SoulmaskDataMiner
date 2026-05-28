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
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using SoulmaskDataMiner.Data;
using SoulmaskDataMiner.GameData;
using System.Diagnostics.CodeAnalysis;

namespace SoulmaskDataMiner.Miners
{
	/// <summary>
	/// Base class for miners that gather information about specific class hierarchies
	/// </summary>
	[RequireHierarchy(true)]
	internal abstract class SubclassMinerBase : MinerBase
	{
		/// <summary>
		/// The name of the property that stores the name to associate with the class.
		/// </summary>
		protected abstract string NameProperty { get; }

		/// <summary>
		/// The name of the property that stores the description to associate with the class.
		/// </summary>
		protected virtual string? DescriptionProperty => null;

		/// <summary>
		/// The name of the property that stores the icon to associate with the class.
		/// </summary>
		protected virtual string? IconProperty => null;

		/// <summary>
		/// Names of additional properties to collect
		/// </summary>
		protected virtual IReadOnlySet<string>? AdditionalPropertyNames => null;

		/// <summary>
		/// Gathers a list of classes which derive from a specific class
		/// </summary>
		protected IEnumerable<ObjectInfo> FindObjects(IEnumerable<string> baseClassNames)
		{
			HashSet<ObjectInfo> infoSet = new();
			foreach (string className in baseClassNames)
			{
				foreach (BlueprintClassInfo classInfo in GameClassHierarchy.Instance.GetDerivedBlueprintClasses(className))
				{
					if (classInfo.Export.ExportObject.Value is UClass classObj)
					{
						string packageName = classInfo.Export.ExportObject.Value.Owner!.Name;
						if (packageName.StartsWith("WS/Content")) packageName = $"/Game{packageName[10..]}";
						ObjectInfo obj = new()
						{
							ClassName = classInfo.Name,
							FullPath = $"{packageName}.{classInfo.Name}",
							SuperName = className
						};
						FindObjectProperties(classObj, ref obj);
						infoSet.Add(obj);
					}
				}
			}

			List<ObjectInfo> infos = new(infoSet);
			infos.Sort();
			return infos;
		}

		private void FindObjectProperties(UClass classObj, ref ObjectInfo obj)
		{
			if (obj.AdditionalProperties is null && AdditionalPropertyNames is not null)
			{
				obj.AdditionalProperties = new();
			}

			if (classObj.ClassDefaultObject.TryLoad(out UObject? defaults))
			{
				foreach (FPropertyTag property in defaults!.Properties)
				{
					if (obj.Name is null && string.Equals(property.Name.Text, NameProperty, StringComparison.OrdinalIgnoreCase))
					{
						obj.Name = DataUtil.ReadTextProperty(property);
					}
					else if (obj.Description is null && string.Equals(property.Name.Text, DescriptionProperty, StringComparison.OrdinalIgnoreCase))
					{
						obj.Description = DataUtil.ReadTextProperty(property);
					}
					else if (obj.Icon is null && string.Equals(property.Name.Text, IconProperty, StringComparison.OrdinalIgnoreCase))
					{
						obj.Icon = DataUtil.ReadTextureProperty(property);
					}
					else if (AdditionalPropertyNames?.Contains(property.Name.Text) ?? false)
					{
						if (!obj.AdditionalProperties!.ContainsKey(property.Name.Text))
						{
							obj.AdditionalProperties!.Add(property.Name.Text, property);
						}
					}
				}

				if (classObj.Super is not null &&
					classObj.Super.TryLoad(out UObject? super) &&
					super is UBlueprintGeneratedClass superBlueprint)
				{
					// Search parents for anything we didn't find
					FindObjectProperties(superBlueprint, ref obj);
				}
			}
		}

		protected struct ObjectInfo : IComparable<ObjectInfo>
		{
			public string ClassName;
			public string FullPath;
			public string? SuperName;
			public string? Name;
			public string? Description;
			public UTexture2D? Icon;
			public Dictionary<string, FPropertyTag>? AdditionalProperties;

			public int CompareTo(ObjectInfo other)
			{
				if (Name is null)
				{
					if (other.Name is not null) return -1;
					return ClassName.CompareTo(other.ClassName);
				}
				int result = Name.CompareTo(other.Name);
				if (result == 0)
				{
					return ClassName.CompareTo(other.ClassName);
				}
				return result;
			}

			public override int GetHashCode()
			{
				return FullPath.GetHashCode();
			}

			public override bool Equals([NotNullWhen(true)] object? obj)
			{
				return obj is ObjectInfo other && FullPath.Equals(other.FullPath);
			}

			public override string ToString()
			{
				return ClassName;
			}
		}
	}
}
