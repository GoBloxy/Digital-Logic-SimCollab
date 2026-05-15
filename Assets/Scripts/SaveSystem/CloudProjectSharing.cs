using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace DLS.SaveSystem
{
	public static class CloudProjectSharing
	{
		static string SupabaseUrl => SupabaseAuth.SupabaseUrl;
		static string AnonKey => SupabaseAuth.AnonKey;
		const string BucketName = "projects";

		static string PublicBaseUrl => $"{SupabaseUrl}/storage/v1/object/public/{BucketName}/";
		static string UploadBaseUrl => $"{SupabaseUrl}/storage/v1/object/{BucketName}/";
		static string RestUrl => $"{SupabaseUrl}/rest/v1/project_shares";

		// ---- Upload ----

		/// <summary>
		/// Exports the selected project and uploads it to Supabase Storage.
		/// Returns (success, shareCode or errorMessage).
		/// </summary>
		public static async Task<(bool success, string result)> UploadProject(string projectName)
		{
			string tempDir = Path.Combine(Path.GetTempPath(), "DLS_CloudShare");
			Directory.CreateDirectory(tempDir);

			string exportPath = ProjectExporter.ExportProject(projectName, tempDir);
			string shareCode = Path.GetFileNameWithoutExtension(exportPath) + "-" + Guid.NewGuid().ToString("N")[..8];
			string fileName = shareCode + ProjectExporter.ExportExtension;

			byte[] fileBytes = File.ReadAllBytes(exportPath);
			try { File.Delete(exportPath); } catch { }

			// Upload file
			string uploadUrl = UploadBaseUrl + Uri.EscapeDataString(fileName);
			using (UnityWebRequest uploadReq = new(uploadUrl, "POST"))
			{
				uploadReq.uploadHandler = new UploadHandlerRaw(fileBytes);
				uploadReq.downloadHandler = new DownloadHandlerBuffer();
				uploadReq.SetRequestHeader("Authorization", "Bearer " + (SupabaseAuth.IsLoggedIn ? SupabaseAuth.AccessToken : AnonKey));
				uploadReq.SetRequestHeader("Content-Type", "application/octet-stream");

				var op = uploadReq.SendWebRequest();
				while (!op.isDone) await Task.Yield();

				if (uploadReq.result != UnityWebRequest.Result.Success)
					return (false, uploadReq.error);
			}

			// Record share metadata
			await RecordShare(shareCode, projectName);

			return (true, shareCode);
		}

		// ---- Download ----

		public struct ShareInfo
		{
			public string ownerEmail;
			public string projectName;
			public string createdAt;
			public int downloadCount;
		}

		/// <summary>
		/// Downloads a project by share code and imports it.
		/// Returns (success, projectName or errorMessage, shareInfo).
		/// </summary>
		public static async Task<(bool success, string result, ShareInfo info)> DownloadProject(string shareCode)
		{
			shareCode = shareCode.Trim();
			string fileName = shareCode.EndsWith(ProjectExporter.ExportExtension)
				? shareCode
				: shareCode + ProjectExporter.ExportExtension;

			// Fetch share info from DB first (non-fatal if it fails)
			ShareInfo info = await FetchShareInfo(shareCode.Replace(ProjectExporter.ExportExtension, ""));

			string downloadUrl = PublicBaseUrl + Uri.EscapeDataString(fileName);
			using UnityWebRequest req = UnityWebRequest.Get(downloadUrl);
			var dlOp = req.SendWebRequest();
			while (!dlOp.isDone) await Task.Yield();

			if (req.result != UnityWebRequest.Result.Success)
				return (false, "Download failed: " + req.error, info);

			string tempPath = Path.Combine(Path.GetTempPath(), "DLS_import_" + Guid.NewGuid().ToString("N") + ProjectExporter.ExportExtension);
			File.WriteAllBytes(tempPath, req.downloadHandler.data);

			var (result, projectName, errorMessage) = ProjectImporter.ImportProject(tempPath);
			try { File.Delete(tempPath); } catch { }

			if (result != ProjectImporter.ImportResult.Success)
				return (false, errorMessage, info);

			// Increment download count (fire and forget)
			_ = IncrementDownloadCount(shareCode.Replace(ProjectExporter.ExportExtension, ""));

			return (true, projectName, info);
		}

		// ---- DB helpers ----

		static async Task RecordShare(string shareCode, string projectName)
		{
			string ownerEmail = SupabaseAuth.IsLoggedIn ? SupabaseAuth.UserEmail : "anonymous";
			string ownerId = SupabaseAuth.IsLoggedIn ? $"\"{SupabaseAuth.UserId}\"" : "null";

			string body = $"{{" +
				$"\"share_code\":\"{shareCode}\"," +
				$"\"project_name\":\"{Escape(projectName)}\"," +
				$"\"owner_email\":\"{Escape(ownerEmail)}\"," +
				$"\"owner_id\":{ownerId}" +
				$"}}";

			byte[] bytes = Encoding.UTF8.GetBytes(body);
			using UnityWebRequest req = new(RestUrl, "POST");
			req.uploadHandler = new UploadHandlerRaw(bytes);
			req.downloadHandler = new DownloadHandlerBuffer();
			req.SetRequestHeader("apikey", AnonKey);
			req.SetRequestHeader("Content-Type", "application/json");
			req.SetRequestHeader("Prefer", "return=minimal");
			if (SupabaseAuth.IsLoggedIn)
				req.SetRequestHeader("Authorization", "Bearer " + SupabaseAuth.AccessToken);

			var op = req.SendWebRequest();
			while (!op.isDone) await Task.Yield();
		}

		static async Task<ShareInfo> FetchShareInfo(string shareCode)
		{
			string url = RestUrl + $"?share_code=eq.{Uri.EscapeDataString(shareCode)}&select=owner_email,project_name,created_at,download_count&limit=1";
			using UnityWebRequest req = UnityWebRequest.Get(url);
			req.SetRequestHeader("apikey", AnonKey);

			var op = req.SendWebRequest();
			while (!op.isDone) await Task.Yield();

			if (req.result != UnityWebRequest.Result.Success) return default;

			string json = req.downloadHandler.text;
			// Unwrap array: [{ ... }]
			json = json.Trim();
			if (json.StartsWith("[") && json.Length > 2)
				json = json.Substring(1, json.LastIndexOf(']') - 1).Trim();

			if (string.IsNullOrWhiteSpace(json)) return default;

			ShareInfoRaw raw = JsonUtility.FromJson<ShareInfoRaw>(json);
			return new ShareInfo
			{
				ownerEmail = raw.owner_email,
				projectName = raw.project_name,
				createdAt = raw.created_at,
				downloadCount = raw.download_count
			};
		}

		static async Task IncrementDownloadCount(string shareCode)
		{
			// Read current count
			ShareInfo info = await FetchShareInfo(shareCode);
			int newCount = info.downloadCount + 1;

			string url = RestUrl + $"?share_code=eq.{Uri.EscapeDataString(shareCode)}";
			string body = $"{{\"download_count\":{newCount}}}";
			byte[] bytes = Encoding.UTF8.GetBytes(body);

			using UnityWebRequest req = new(url, "PATCH");
			req.uploadHandler = new UploadHandlerRaw(bytes);
			req.downloadHandler = new DownloadHandlerBuffer();
			req.SetRequestHeader("apikey", AnonKey);
			req.SetRequestHeader("Content-Type", "application/json");

			var op = req.SendWebRequest();
			while (!op.isDone) await Task.Yield();
		}

		static string Escape(string s) => s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";

		[Serializable] class ShareInfoRaw { public string owner_email; public string project_name; public string created_at; public int download_count; }
	}
}
