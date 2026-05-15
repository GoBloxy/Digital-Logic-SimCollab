using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using DLS.Description;

namespace DLS.SaveSystem
{
	public static class ProjectImporter
	{
		public enum ImportResult
		{
			Success,
			FileNotFound,
			InvalidArchive,
			NoProjectDescriptionFound,
			ProjectAlreadyExists,
			Error
		}

		/// <summary>
		/// Imports a .dlsproj ZIP file into the Projects directory.
		/// If a project with the same name already exists, appends a number.
		/// Returns the result and the imported project name.
		/// </summary>
		public static (ImportResult result, string projectName, string errorMessage) ImportProject(string dlsprojFilePath, bool autoRenameOnConflict = true)
		{
			if (!File.Exists(dlsprojFilePath))
				return (ImportResult.FileNotFound, null, "File not found: " + dlsprojFilePath);

			string tempExtractPath = Path.Combine(SavePaths.AllData, "_import_temp_" + Guid.NewGuid().ToString("N"));

			try
			{
				// Extract to temp directory
				ZipFile.ExtractToDirectory(dlsprojFilePath, tempExtractPath);

				// Find the project directory (contains ProjectDescription.json)
				string projectDir = FindProjectDirectory(tempExtractPath);
				if (projectDir == null)
				{
					CleanupTemp(tempExtractPath);
					return (ImportResult.NoProjectDescriptionFound, null, "No valid project found in the archive.");
				}

				// Read the project description to get the name
				string descPath = Path.Combine(projectDir, SavePaths.ProjectFileName);
				string descJson = File.ReadAllText(descPath);
				ProjectDescription desc = Serializer.DeserializeProjectDescription(descJson);
				string projectName = desc.ProjectName;

				if (string.IsNullOrWhiteSpace(projectName))
				{
					// Fall back to directory name
					projectName = Path.GetFileName(projectDir);
				}

				// Ensure unique name
				string targetPath = SavePaths.GetProjectPath(projectName);
				if (Directory.Exists(targetPath))
				{
					if (!autoRenameOnConflict)
					{
						CleanupTemp(tempExtractPath);
						return (ImportResult.ProjectAlreadyExists, projectName, $"A project named '{projectName}' already exists.");
					}

					string baseName = projectName;
					int counter = 1;
					while (Directory.Exists(targetPath))
					{
						projectName = $"{baseName} ({counter})";
						targetPath = SavePaths.GetProjectPath(projectName);
						counter++;
					}
				}

				// Move to final location
				SavePaths.EnsureDirectoryExists(SavePaths.ProjectsPath);
				Directory.Move(projectDir, targetPath);

				// Update the project description name to match the folder
				desc.ProjectName = projectName;
				string updatedDescJson = Serializer.SerializeProjectDescription(desc);
				File.WriteAllText(Path.Combine(targetPath, SavePaths.ProjectFileName), updatedDescJson);

				CleanupTemp(tempExtractPath);
				return (ImportResult.Success, projectName, null);
			}
			catch (InvalidDataException)
			{
				CleanupTemp(tempExtractPath);
				return (ImportResult.InvalidArchive, null, "The file is not a valid .dlsproj archive.");
			}
			catch (Exception e)
			{
				CleanupTemp(tempExtractPath);
				return (ImportResult.Error, null, "Import failed: " + e.Message);
			}
		}

		static string FindProjectDirectory(string extractRoot)
		{
			// Check if ProjectDescription.json is directly in extractRoot
			if (File.Exists(Path.Combine(extractRoot, SavePaths.ProjectFileName)))
				return extractRoot;

			// Check one level deep (ZIP was created with includeBaseDirectory: true)
			foreach (string dir in Directory.EnumerateDirectories(extractRoot))
			{
				if (File.Exists(Path.Combine(dir, SavePaths.ProjectFileName)))
					return dir;
			}

			return null;
		}

		static void CleanupTemp(string tempPath)
		{
			try
			{
				if (Directory.Exists(tempPath))
					Directory.Delete(tempPath, true);
			}
			catch
			{
				// Best effort cleanup
			}
		}
	}
}
