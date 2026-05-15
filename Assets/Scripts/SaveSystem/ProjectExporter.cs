using System;
using System.IO;
using System.IO.Compression;

namespace DLS.SaveSystem
{
	public static class ProjectExporter
	{
		public const string ExportExtension = ".dlsproj";
		public const string ExportFilterName = "DLS Project";

		/// <summary>
		/// Exports a project folder to a single .dlsproj ZIP archive.
		/// Returns the path of the created file, or null on failure.
		/// </summary>
		public static string ExportProject(string projectName, string destinationDirectory)
		{
			string projectPath = SavePaths.GetProjectPath(projectName);

			if (!Directory.Exists(projectPath))
				throw new DirectoryNotFoundException("Project folder not found: " + projectPath);

			SavePaths.EnsureDirectoryExists(destinationDirectory);

			string sanitizedName = SanitizeFileName(projectName);
			string exportFilePath = Path.Combine(destinationDirectory, sanitizedName + ExportExtension);
			exportFilePath = SaveUtils.EnsureUniqueFileName(exportFilePath);

			// Delete temp file if it exists from a failed previous export
			string tempPath = exportFilePath + ".tmp";
			if (File.Exists(tempPath)) File.Delete(tempPath);

			// Create ZIP archive
			ZipFile.CreateFromDirectory(projectPath, tempPath, CompressionLevel.Optimal, includeBaseDirectory: true);

			// Atomic rename
			File.Move(tempPath, exportFilePath);

			return exportFilePath;
		}

		/// <summary>
		/// Exports a project and opens the containing folder in the system file browser.
		/// </summary>
		public static string ExportProjectToDesktop(string projectName)
		{
			string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
			string exportDir = Path.Combine(desktop, "DLS Exports");
			return ExportProject(projectName, exportDir);
		}

		static string SanitizeFileName(string name)
		{
			char[] invalid = Path.GetInvalidFileNameChars();
			foreach (char c in invalid)
			{
				name = name.Replace(c, '_');
			}
			return name;
		}
	}
}
