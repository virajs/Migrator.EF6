﻿using System;
using System.Data.Entity.Migrations;
using System.Data.Entity.Migrations.Design;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Dnx.Runtime.Common.CommandLine;
using Microsoft.Extensions.PlatformAbstractions;

namespace Migrator.EF6
{
	public class Program
	{
		public static int Main(string[] args)
		{
			var app = new CommandLineApplication
			{
				Name = "dnx ef",
				FullName = "Entity Framework 6 Commands"
			};
			app.HelpOption("-?|-h|--help");
			app.OnExecute(() => app.ShowHelp());
			app.Command(
				"database",
				database =>
				{
					database.Description = "Commands to manage your database";
					database.HelpOption("-?|-h|--help");
					database.OnExecute(() => database.ShowHelp());
					database.Command(
						"update",
						update =>
						{
							update.Description = "Updates the database to a specified migration";
							var migrationName = update.Argument(
								"[migration]",
								"The target migration. If '0', all migrations will be reverted. If omitted, all pending migrations will be applied");
							update.OnExecute(
								() =>
								{
									CreateExecutor().UpdateDatabase(migrationName.Value);
								});
						});
				});
			app.Command(
				"migrations",
				migration =>
				{
					migration.Description = "Commands to manage your migrations";
					migration.HelpOption("-?|-h|--help");
					migration.OnExecute(() => migration.ShowHelp());
					migration.Command(
						"enable",
						enable =>
						{
							enable.Description = "Enable migrations";
							enable.OnExecute(
								() =>
								{
									CreateExecutor().EnableMigrations();
								});
						});
					migration.Command(
						"add",
						add =>
						{
							add.Description = "Add a new migration";
							add.HelpOption("-?|-h|--help");
							add.OnExecute(() => add.ShowHelp());
							var name = add.Argument(
								"[name]",
								"The name of the migration");
							add.OnExecute(
								() =>
								{
									if (string.IsNullOrEmpty(name.Value))
									{
										return 1;
									}

									CreateExecutor().AddMigration(name.Value);
									return 0;
								});
						});
					migration.Command(
					   "list",
					   list =>
					   {
						   list.Description = "List the migrations";
						   list.HelpOption("-?|-h|--help");
						   list.OnExecute(() => list.ShowHelp());
						   list.OnExecute(
							   () =>
							   {
								   CreateExecutor().ListMigrations();
							   });
					   });
				});

			return app.Execute(args);
		}

		private static Executor CreateExecutor() => new Executor();

		public class Executor
		{
			private IApplicationEnvironment _appEnv;
			private ILibraryManager _libraryManager;
			private string _projectDir;
			private string _rootNamespace;
			private Assembly _startupAssembly;
			private Library _targetLibrary;
			private string _targetName;
			private Type[] _types;

			public Executor()
			{
				_appEnv = PlatformServices.Default.Application;
				_libraryManager = PlatformServices.Default.LibraryManager;
				_targetName = _appEnv.ApplicationName;
				_targetLibrary = _libraryManager.GetLibrary(_targetName);
				_projectDir = Path.GetDirectoryName(_targetLibrary.Path);
				_rootNamespace = _targetLibrary.Name;
				_startupAssembly = Assembly.Load(new AssemblyName(_targetName));
				_types = _startupAssembly.GetTypes();
				Directory.CreateDirectory(MigrationsDir);
			}

			private string MigrationsDir => Path.Combine(_projectDir, "Migrations");

			private string Combine(params string[] paths) => Path.Combine(paths);

			public void EnableMigrations()
			{
				var ns = $"{_rootNamespace}.Migrations";
				var path = Combine(MigrationsDir, "Configuration.cs");
				File.WriteAllText(path, @"using System.Data.Entity.Migrations;
using " + _rootNamespace + @".Models;

namespace " + ns + @"
{
	public class Configuration : DbMigrationsConfiguration<ApplicationDbContext>
	{
	}
}
");
			}

			public void AddMigration(string name)
			{
				var config = FindDbMigrationsConfiguration();

				// Scaffold migration.
				var scaffolder = new MigrationScaffolder(config);
				var migration = scaffolder.Scaffold(name);

				// Write the user code file.
				File.WriteAllText(Combine(MigrationsDir, migration.MigrationId + ".cs"), migration.UserCode);

				// Write needed resource values directly inside the designer code file.
				// Apparently, aspnet5 and resource files don't play well (or more specifically,
				// the way ef6 migration generator is interacting with the resources system)
				var targetValue = migration.Resources["Target"];
				var designerCode = migration.DesignerCode
					.Replace("Resources.GetString(\"Target\")", $"\"{targetValue}\"")
					.Replace("private readonly ResourceManager Resources = new ResourceManager(typeof(InitialCreate));", "");

				// Write the designer code file.
				File.WriteAllText(Path.Combine(MigrationsDir, migration.MigrationId + ".Designer.cs"), designerCode);
			}

			public void UpdateDatabase(string targetMigration)
			{
				var config = FindDbMigrationsConfiguration();
				var migrator = new DbMigrator(config);
				migrator.Update(targetMigration);
			}

			private DbMigrationsConfiguration FindDbMigrationsConfiguration()
			{
				var configType = _types
					.Where(t => typeof(DbMigrationsConfiguration).IsAssignableFrom(t))
					.FirstOrDefault();
				var config = Activator.CreateInstance(configType) as DbMigrationsConfiguration;
				return config;
			}

			public void ListMigrations()
			{
				var config = FindDbMigrationsConfiguration();
				var migrator = new DbMigrator(config);
				foreach (var migration in migrator.GetDatabaseMigrations())
				{
					Console.WriteLine(migration);
				}
			}
		}
	}

	internal static partial class Extensions
	{
		public static void OnExecute(this CommandLineApplication @this, Action action)
		{
			@this.OnExecute(() =>
			{
				action();
				return 0;
			});
		}
	}
}