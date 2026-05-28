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
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Objects.Core.i18N;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.UObject;
using System.Runtime.CompilerServices;

namespace SoulmaskDataMiner.Data
{
	/// <summary>
	/// Utility functions for working with game objects
	/// </summary>
	internal static class DataUtil
	{
		/// <summary>
		/// Parse an enum value from a game enum
		/// </summary>
		/// <typeparam name="T">The type of the enum</typeparam>
		/// <param name="property">An enum property containing the value to parse</param>
		/// <param name="result">If the parse was successful, this will contain the result</param>
		/// <returns>Whether the parse was successful</returns>
		public static bool TryParseEnum<T>(FPropertyTag property, out T result) where T : struct
		{
			if (property.Tag is null)
			{
				result = default;
				return false;
			}
			return TryParseEnum(property.Tag, out result);
		}

		/// <summary>
		/// Parse an enum value from a game enum
		/// </summary>
		/// <typeparam name="T">The type of the enum</typeparam>
		/// <param name="property">An enum property containing the value to parse</param>
		/// <param name="result">If the parse was successful, this will contain the result</param>
		/// <returns>Whether the parse was successful</returns>
		public static bool TryParseEnum<T>(FPropertyTagType property, out T result) where T : struct
		{
			return TryParseEnum(property.GetValue<FName>(), out result);
		}

		/// <summary>
		/// Parse an enum value from a game enum
		/// </summary>
		/// <typeparam name="T">The type of the enum</typeparam>
		/// <param name="value">The value to parse</param>
		/// <param name="result">If the parse was successful, this will contain the result</param>
		/// <returns>Whether the parse was successful</returns>
		public static bool TryParseEnum<T>(FName value, out T result) where T : struct
		{
			string name = value.Text.Substring(value.Text.LastIndexOf(':') + 1);
			return Enum.TryParse(name, out result);
		}

		/// Attempts to read a property value as text
		/// </summary>
		/// <param name="property">The property to read</param>
		/// <returns>The text, or null if failure</returns>
		public static string? ReadTextProperty(FPropertyTag property)
		{
			return property.Tag?.GetValue<FText>()?.Text;
		}

		/// <summary>
		/// Attempts to read a property value as a texture
		/// </summary>
		/// <param name="property">The property to read</param>
		/// <returns>The texture, or null if failure</returns>
		public static UTexture2D? ReadTextureProperty(FPropertyTag property)
		{
			return property.Tag?.GetValue<FPackageIndex>()?.ResolvedObject?.Object?.Value as UTexture2D;
		}

		/// <summary>
		/// Attempts to read a property value as a texture
		/// </summary>
		/// <param name="property">The property to read</param>
		/// <returns>The texture, or null if failure</returns>
		public static UTexture2D? ReadTextureProperty(FPropertyTagType? property)
		{
			return property?.GetValue<FPackageIndex>()?.ResolvedObject?.Object?.Value as UTexture2D;
		}

		/// <summary>
		/// Attempts to read a property value as a range
		/// </summary>
		/// <typeparam name="T">The range value type</typeparam>
		/// <param name="property">The property to read</param>
		/// <returns>The range, or null if failure</returns>
		public static TRange<T>? ReadRangeProperty<T>(FPropertyTag? property) where T : struct
		{
			if (property is null) return null;
			return ReadRangeProperty<T>(property.Tag);
		}

		/// <summary>
		/// Attempts to read a property value as a range
		/// </summary>
		/// <typeparam name="T">The range value type</typeparam>
		/// <param name="property">The property to read</param>
		/// <returns>The range, or null if failure</returns>
		public static unsafe TRange<T>? ReadRangeProperty<T>(FPropertyTagType? property) where T : struct
		{
			FStructFallback? data = property?.GetValue<FStructFallback>();
			if (data is null) return null;

			ERangeBoundTypes? lowerBoundType = null;
			T? lowerBoundValue = null;

			ERangeBoundTypes? upperBoundType = null;
			T? upperBoundValue = null;

			foreach (FPropertyTag prop in data.Properties)
			{
				FStructFallback? boundData = prop.Tag?.GetValue<FStructFallback>();
				if (boundData is null) return null;

				ERangeBoundTypes? boundType = null;
				T? boundValue = null;
				foreach (FPropertyTag boundProp in boundData.Properties)
				{
					switch (boundProp.Name.Text)
					{
						case "Type":
							if (TryParseEnum(boundProp, out ERangeBoundTypes bt))
							{
								boundType = bt;
							}
							break;
						case "Value":
							boundValue = boundProp.Tag?.GetValue<T>();
							break;
					}
				}

				if (!boundType.HasValue || !boundValue.HasValue) return null;

				switch (prop.Name.Text)
				{
					case "LowerBound":
						lowerBoundType = boundType;
						lowerBoundValue = boundValue;
						break;
					case "UpperBound":
						upperBoundType = boundType;
						upperBoundValue = boundValue;
						break;
				}
			}

			if (!lowerBoundType.HasValue || !lowerBoundValue.HasValue) return null;
			if (!upperBoundType.HasValue || !upperBoundValue.HasValue) return null;

			// TRange is a readonly struct with no cosntructor, so we are using unsafe mode to write values.
			TRange<T> value = new();
			int valueSize = Unsafe.SizeOf<TRange<T>>();

			byte* bufferPtr = stackalloc byte[valueSize];
			byte* pos = bufferPtr;

			byte lowerType = (byte)lowerBoundType.Value;
			Unsafe.Copy(pos, ref lowerType);
			++pos;

			T lowerValue = lowerBoundValue.Value;
			Unsafe.Copy(pos, ref lowerValue);
			pos += Unsafe.SizeOf<T>();

			byte upperType = (byte)upperBoundType.Value;
			Unsafe.Copy(pos, ref upperType);
			++pos;

			T upperValue = upperBoundValue.Value;
			Unsafe.Copy(pos, ref upperValue);
			pos += Unsafe.SizeOf<T>();

			value = Unsafe.ReadUnaligned<TRange<T>>(bufferPtr);

			return value;
		}

		/// <summary>
		/// Locates the default properties object for a blueprint
		/// </summary>
		/// <param name="provider">The provider to load the blueprint from</param>
		/// <param name="assetPath">The path to the blueprint asset</param>
		/// <returns>The object, or null if failure</returns>
		public static UObject? FindBlueprintDefaultsObject(IFileProvider provider, string assetPath)
		{
			if (!provider.TryGetGameFile(assetPath, out GameFile? file))
			{
				return null;
			}

			Package package = (Package)provider.LoadPackage(file);
			return FindBlueprintDefaultsObject(package);
		}

		/// <summary>
		/// Locates the default properties object for a blueprint
		/// </summary>
		/// <param name="package">The package containing the blueprint</param>
		/// <returns>The object, or null if failure</returns>
		public static UObject? FindBlueprintDefaultsObject(Package package)
		{
			foreach (FObjectExport export in package.ExportMap)
			{
				if (!export.ClassName.Equals("BlueprintGeneratedClass")) continue;

				UBlueprintGeneratedClass? exportObj = export.ExportObject.Value as UBlueprintGeneratedClass;
				if (exportObj is null) continue;

				return exportObj.ClassDefaultObject.ResolvedObject?.Load();
			}

			return null;
		}

		/// <summary>
		/// Locates the default properties object for a blueprint
		/// </summary>
		/// <param name="export">The blueprint class export</param>
		/// <returns>The object, or null if failure</returns>
		public static UObject? FindBlueprintDefaultsObject(FObjectExport export)
		{
			UBlueprintGeneratedClass? exportObj = export.ExportObject.Value as UBlueprintGeneratedClass;
			return exportObj?.ClassDefaultObject.ResolvedObject?.Load();
		}

		/// <summary>
		/// Locates the default properties object for a blueprint
		/// </summary>
		/// <param name="packageIndex">The blueprint class export package index</param>
		/// <returns>The object, or null if failure</returns>
		public static UObject? FindBlueprintDefaultsObject(FPackageIndex packageIndex)
		{
			UBlueprintGeneratedClass? exportObj = packageIndex.Load() as UBlueprintGeneratedClass;
			return exportObj?.ClassDefaultObject.ResolvedObject?.Load();
		}

		/// <summary>
		/// Load the first texture found within an asset's exports
		/// </summary>
		/// <param name="provider">The provider to load the asset from</param>
		/// <param name="assetPath">The path to the asset</param>
		/// <param name="logger">For logging warnings and errors</param>
		/// <returns>The loaded texture, or null if no texture could be oaded</returns>
		public static UTexture2D? LoadFirstTexture(IFileProvider provider, string assetPath, Logger logger)
		{
			if (!provider.TryGetGameFile(assetPath, out GameFile? file))
			{
				logger.Error($"Unable to locate asset {assetPath}.");
				return null;
			}

			Package package = (Package)provider.LoadPackage(file);

			foreach (FObjectExport export in package.ExportMap)
			{
				if (!export.ClassName.Equals("Texture2D")) continue;

				UTexture2D? texture = export.ExportObject.Value as UTexture2D;
				if (texture is null) continue;

				return texture;
			}

			return null;
		}
	}
}
