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
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Textures;
using SkiaSharp;
using SoulmaskDataMiner.Data;

namespace SoulmaskDataMiner.IO
{
	/// <summary>
	/// Helper for exporting texture assets
	/// </summary>
	internal static class TextureExporter
	{
		/// <summary>
		/// Whether to export textures
		/// </summary>
		public static bool Enabled { get; set; } = true;

		/// <summary>
		/// Export the first texture found within an asset's exports
		/// </summary>
		/// <param name="provider">The provider to load the asset from</param>
		/// <param name="assetPath">The path to the asset</param>
		/// <param name="includePath">True to append the asset's path to the output directory. False to output the file directly to the output directory.</param>
		/// <param name="logger">For logging warnings and errors</param>
		/// <param name="outDir">The directory to output the texture to</param>
		/// <returns>True if the export succeeded, false on failure or if no texture was found</returns>
		public static bool ExportFirstTexture(IFileProvider provider, string assetPath, bool includePath, Logger logger, string outDir)
		{
			if (!Enabled) return true;

			UTexture2D? texture = DataUtil.LoadFirstTexture(provider, assetPath, logger);
			if (texture is null) return false;

			return ExportTexture(texture, includePath, logger, outDir);
		}

		/// <summary>
		/// Exports a texture asset to an image file
		/// </summary>
		/// <param name="texture">The texture to export</param>
		/// <param name="includePath">True to append the asset's path to the output directory. False to output the file directly to the output directory.</param>
		/// <param name="logger">For logging warnings and errors</param>
		/// <param name="outDir">The directory to output the texture to</param>
		/// <returns>True if the export succeeded, false on failure</returns>
		public static bool ExportTexture(UTexture2D texture, bool includePath, Logger logger, string outDir)
		{
			if (!Enabled) return true;

			string outPath = Path.Combine(outDir, $"{(includePath ? ConvertAssetPath(texture.GetPathName()) : texture.Name)}.png");
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
			}
			catch (Exception ex)
			{
				logger.Error($"Failed to create output directory. [{ex.GetType().FullName}] {ex.Message}");
				return false;
			}

			SKBitmap? bitmap = texture.Decode()?.ToSkBitmap();
			if (bitmap == null)
			{
				logger.Warning($"{texture.GetPathName()} - Failed to decode texture.");
				return false;
			}

			if (!texture.SRGB)
			{
				unsafe
				{
					SKColor* ptr = (SKColor*)bitmap.GetPixels().ToPointer();
					int count = bitmap.Width * bitmap.Height;
					for (int i = 0; i < count; ++i)
					{
						ptr[i] = LinearToSrgb(ptr[i]);
					}
				}
				bitmap.NotifyPixelsChanged();
			}

			SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
			if (data == null)
			{
				return false;
			}

			using (FileStream file = IOUtil.CreateFile(outPath, logger))
			{
				data.SaveTo(file);
			}

			return true;
		}

		/// <summary>
		/// If asset file name is the same as asset name, remove asset name from the path.
		/// Also converts path separators to OS format.
		/// </summary>
		private static string ConvertAssetPath(string assetPath)
		{
			string? dir = Path.GetDirectoryName(assetPath);
			string? file = Path.GetFileName(assetPath);

			if (file is null) return assetPath;

			string[] parts = file.Split('.');
			if (parts.Length > 1)
			{
				if (parts[0] == parts[1])
				{
					parts[1] = string.Empty;
				}
			}
			file = string.Join(".", parts.Where(p => !string.IsNullOrEmpty(p)));

			if (dir is null)
			{
				return file;
			}

			return Path.Combine(dir, file);
		}

		private const float FloatToInt = 255.0f;
		private const float IntToFloat = 1.0f / FloatToInt;

		private static SKColor LinearToSrgb(SKColor linearColor)
		{
			return new SKColor(LinearToSrgb(linearColor.Red * IntToFloat), LinearToSrgb(linearColor.Green * IntToFloat), LinearToSrgb(linearColor.Blue * IntToFloat), linearColor.Alpha);
		}

		private static byte LinearToSrgb(float linear)
		{
			if (linear <= 0.0f) return 0;
			if (linear <= 0.00313066844250063f) return (byte)(linear * 12.92f * FloatToInt);
			if (linear < 1) return (byte)((1.055f * Math.Pow(linear, 1.0f / 2.4f) - 0.055f) * FloatToInt);
			return 255;
		}

	}
}
