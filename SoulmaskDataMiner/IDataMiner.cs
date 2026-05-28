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


using SoulmaskDataMiner.IO;

namespace SoulmaskDataMiner
{
	/// <summary>
	/// Interface for data miners. Implementing this interface will cause your class to be instantiated and run by the mine runner
	/// </summary>
	internal interface IDataMiner
	{
		/// <summary>
		/// The name of the data miner. This is reported to the output window when the miner runs and is used when filtering which miners to run.
		/// </summary>
		string Name { get; }

		/// <summary>
		/// Called by the mine runner to run this data miner
		/// </summary>
		/// <param name="providerManager">Provides access to game data resources</param>
		/// <param name="config">Application config containing (among other things) the location of a directory which the miner can write to</param>
		/// <param name="logger">For logging any output messages while running</param>
		/// <param name="sqlWriter">Add sql statements needed to perform a database update to this writer</param>
		/// <returns>Whether the miner was successful (true) or encountered errors (false)</returns>
		bool Run(IProviderManager providerManager, Config config, Logger logger, ISqlWriter sqlWriter);
	}

	/// <summary>
	/// Declares the public-facing name of a data miner (shown in logs and accepted by the
	/// --miners filter). Required on every concrete <see cref="IDataMiner"/> implementation.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class)]
	internal class MinerNameAttribute : Attribute
	{
		public string Name { get; }

		public MinerNameAttribute(string name)
		{
			Name = name;
		}
	}

	/// <summary>
	/// Indicates whether a data miner should be run when no filter has been applied.
	/// </summary>
	/// <remarks>
	/// If this attribute is not present, the data miner will be enabled by default.
	/// </remarks>
	[AttributeUsage(AttributeTargets.Class)]
	internal class DefaultEnabledAttribute : Attribute
	{
		public bool IsEnabled { get; set; }

		public DefaultEnabledAttribute(bool isEnabled)
		{
			IsEnabled = isEnabled;
		}
	}

	/// <summary>
	/// Indicates whether a data miner requires the use of the blueprint hierarchy.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class)]
	internal class RequireHierarchyAttribute : Attribute
	{
		public bool IsRequired { get; set; }

		public RequireHierarchyAttribute(bool isRequired)
		{
			IsRequired = isRequired;
		}
	}

	/// <summary>
	/// Indicates whether a data miner requires that class metadata is available.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class)]
	internal class RequireClassDataAttribute : Attribute
	{
		public bool IsRequired { get; set; }

		public RequireClassDataAttribute(bool isRequired)
		{
			IsRequired = isRequired;
		}
	}

	/// <summary>
	/// Indicates whether a data miner requires that a loot database is available.
	/// </summary>
	[AttributeUsage(AttributeTargets.Class)]
	internal class RequireLootDatabaseAttribute : Attribute
	{
		public bool IsRequired { get; set; }

		public RequireLootDatabaseAttribute(bool isRequired)
		{
			IsRequired = isRequired;
		}
	}
}
