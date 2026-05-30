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

using System;

namespace SoulmaskDataMiner
{
	/// <summary>
	/// Declares the expected Unreal Engine base classes that a data miner expects to find
	/// in the ClassesInfo.json class metadata dump to successfully locate game assets.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
	internal sealed class RequiredBaseClassesAttribute : Attribute
	{
		public string[] BaseClassNames { get; }

		public RequiredBaseClassesAttribute(params string[] baseClassNames)
		{
			BaseClassNames = baseClassNames;
		}
	}
}
