using System;
using System.Collections.Generic;
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

		static string PublicBaseUrl  => $"{SupabaseUrl}/storage/v1/object/public/{BucketName}/";
		static string UploadBaseUrl  => $"{SupabaseUrl}/storage/v1/object/{BucketName}/";
		static string SharesUrl      => $"{SupabaseUrl}/rest/v1/project_shares";
		static string DownloadsUrl   => $"{SupabaseUrl}/rest/v1/download_logs";

		// ---- Upload ----

		public static async Task<(bool success, string shareCode, string error)> UploadProject(string projectName)
		{
			string tempDir = Path.Combine(Path.GetTempPath(), "DLS_CloudShare");
			Directory.CreateDirectory(tempDir);

			string exportPath = ProjectExporter.ExportProject(projectName, tempDir);
			string shareCode = Path.GetFileNameWithoutExtension(exportPath) + "-" + Guid.NewGuid().ToString("N")[..8];
			string fileName  = shareCode + ProjectExporter.ExportExtension;

			byte[] fileBytes = File.ReadAllBytes(exportPath);
			try { File.Delete(exportPath); } catch { }

			string uploadUrl = UploadBaseUrl + Uri.EscapeDataString(fileName);
			using (UnityWebRequest uploadReq = new(uploadUrl, "POST"))
			{
				uploadReq.uploadHandler   = new UploadHandlerRaw(fileBytes);
				uploadReq.downloadHandler = new DownloadHandlerBuffer();
				uploadReq.SetRequestHeader("Authorization", "Bearer " + (SupabaseAuth.IsLoggedIn ? SupabaseAuth.AccessToken : AnonKey));
				uploadReq.SetRequestHeader("Content-Type", "application/octet-stream");

				var op = uploadReq.SendWebRequest();
				while (!op.isDone) await Task.Yield();

				if (uploadReq.result != UnityWebRequest.Result.Success)
					return (false, null, uploadReq.error);
			}

			await RecordShare(shareCode, projectName);
			return (true, shareCode, null);
		}

		// ---- Download ----

		public struct ShareInfo
		{
			public string ownerEmail;
			public string projectName;
			public string createdAt;
			public int    downloadCount;
		}

		public static async Task<(bool success, string projectName, string error, ShareInfo info)> DownloadProject(string shareCode)
		{
			shareCode = shareCode.Trim();
			string fileName = shareCode.EndsWith(ProjectExporter.ExportExtension)
				? shareCode
				: shareCode + ProjectExporter.ExportExtension;

			string cleanCode = shareCode.Replace(ProjectExporter.ExportExtension, "");
			ShareInfo info   = await FetchShareInfo(cleanCode);

			string downloadUrl = PublicBaseUrl + Uri.EscapeDataString(fileName);
			using UnityWebRequest req = UnityWebRequest.Get(downloadUrl);
			var dlOp = req.SendWebRequest();
			while (!dlOp.isDone) await Task.Yield();

			if (req.result != UnityWebRequest.Result.Success)
				return (false, null, "Download failed: " + req.error, info);

			string tempPath = Path.Combine(Path.GetTempPath(), "DLS_import_" + Guid.NewGuid().ToString("N") + ProjectExporter.ExportExtension);
			File.WriteAllBytes(tempPath, req.downloadHandler.data);

			var (result, projectName, errorMessage) = ProjectImporter.ImportProject(tempPath);
			try { File.Delete(tempPath); } catch { }

			if (result != ProjectImporter.ImportResult.Success)
				return (false, null, errorMessage, info);

			_ = LogDownload(cleanCode);
			return (true, projectName, null, info);
		}

		// ---- History ----

		public struct ShareHistoryEntry
		{
			public string shareCode;
			public string projectName;
			public string createdAt;
			public int    downloadCount;
		}

		public struct DownloadHistoryEntry
		{
			public string shareCode;
			public string projectName;
			public string ownerEmail;
			public string downloadedAt;
		}

		public static async Task<List<ShareHistoryEntry>> FetchMyShares()
		{
			if (!SupabaseAuth.IsLoggedIn) return new List<ShareHistoryEntry>();

			string url = SharesUrl + $"?owner_id=eq.{Uri.EscapeDataString(SupabaseAuth.UserId)}" +
			             "&select=share_code,project_name,created_at,download_count&order=created_at.desc&limit=20";

			using UnityWebRequest req = UnityWebRequest.Get(url);
			req.SetRequestHeader("apikey", AnonKey);
			req.SetRequestHeader("Authorization", "Bearer " + SupabaseAuth.AccessToken);
			var op = req.SendWebRequest();
			while (!op.isDone) await Task.Yield();

			if (req.result != UnityWebRequest.Result.Success) return new List<ShareHistoryEntry>();

			return ParseJsonArray<ShareHistoryEntryRaw>(req.downloadHandler.text)
				.ConvertAll(r => new ShareHistoryEntry
				{
					shareCode     = r.share_code,
					projectName   = r.project_name,
					createdAt     = FormatDate(r.created_at),
					downloadCount = r.download_count
				});
		}

		public static async Task<List<DownloadHistoryEntry>> FetchMyDownloads()
		{
			string downloaderFilter = SupabaseAuth.IsLoggedIn
				? $"downloader_id=eq.{Uri.EscapeDataString(SupabaseAuth.UserId)}"
				: $"downloader_email=eq.anonymous";

			string url = DownloadsUrl + $"?{downloaderFilter}" +
			             "&select=share_code,downloaded_at,project_shares(project_name,owner_email)&order=downloaded_at.desc&limit=20";

			using UnityWebRequest req = UnityWebRequest.Get(url);
			req.SetRequestHeader("apikey", AnonKey);
			if (SupabaseAuth.IsLoggedIn)
				req.SetRequestHeader("Authorization", "Bearer " + SupabaseAuth.AccessToken);
			var op = req.SendWebRequest();
			while (!op.isDone) await Task.Yield();

			if (req.result != UnityWebRequest.Result.Success) return new List<DownloadHistoryEntry>();

			return ParseJsonArray<DownloadLogRaw>(req.downloadHandler.text)
				.ConvertAll(r => new DownloadHistoryEntry
				{
					shareCode   = r.share_code,
					projectName = r.project_shares?.project_name ?? "Unknown",
					ownerEmail  = r.project_shares?.owner_email ?? "Unknown",
					downloadedAt = FormatDate(r.downloaded_at)
				});
		}

		// ---- DB helpers ----

		static async Task RecordShare(string shareCode, string projectName)
		{
			string ownerEmail = SupabaseAuth.IsLoggedIn ? SupabaseAuth.UserEmail : "anonymous";
			string ownerId    = SupabaseAuth.IsLoggedIn ? $"\"{SupabaseAuth.UserId}\"" : "null";

			string body = $"{{\"share_code\":\"{shareCode}\"," +
			              $"\"project_name\":\"{Escape(projectName)}\"," +
			              $"\"owner_email\":\"{Escape(ownerEmail)}\"," +
			              $"\"owner_id\":{ownerId}}}";

			await PostJson(SharesUrl, body, "return=minimal");
		}

		static async Task LogDownload(string shareCode)
		{
			string email = SupabaseAuth.IsLoggedIn ? SupabaseAuth.UserEmail : "anonymous";
			string id    = SupabaseAuth.IsLoggedIn ? $"\"{SupabaseAuth.UserId}\"" : "null";

			string body = $"{{\"share_code\":\"{shareCode}\"," +
			              $"\"downloader_email\":\"{Escape(email)}\"," +
			              $"\"downloader_id\":{id}}}";

			await PostJson(DownloadsUrl, body, "return=minimal");

			// Increment download_count on the share
			string countUrl = SharesUrl + $"?share_code=eq.{Uri.EscapeDataString(shareCode)}";
			ShareInfo info  = await FetchShareInfo(shareCode);
			string patch    = $"{{\"download_count\":{info.downloadCount + 1}}}";
			await PatchJson(countUrl, patch);
		}

		static async Task<ShareInfo> FetchShareInfo(string shareCode)
		{
			string url = SharesUrl + $"?share_code=eq.{Uri.EscapeDataString(shareCode)}" +
			             "&select=owner_email,project_name,created_at,download_count&limit=1";

			using UnityWebRequest req = UnityWebRequest.Get(url);
			req.SetRequestHeader("apikey", AnonKey);
			var op = req.SendWebRequest();
			while (!op.isDone) await Task.Yield();

			if (req.result != UnityWebRequest.Result.Success) return default;

			string json = UnwrapArray(req.downloadHandler.text);
			if (string.IsNullOrWhiteSpace(json)) return default;

			ShareInfoRaw raw = JsonUtility.FromJson<ShareInfoRaw>(json);
			return new ShareInfo
			{
				ownerEmail    = raw.owner_email,
				projectName   = raw.project_name,
				createdAt     = raw.created_at,
				downloadCount = raw.download_count
			};
		}

		static async Task PostJson(string url, string body, string prefer = null)
		{
			byte[] bytes = Encoding.UTF8.GetBytes(body);
			using UnityWebRequest req = new(url, "POST");
			req.uploadHandler   = new UploadHandlerRaw(bytes);
			req.downloadHandler = new DownloadHandlerBuffer();
			req.SetRequestHeader("apikey", AnonKey);
			req.SetRequestHeader("Content-Type", "application/json");
			if (prefer != null) req.SetRequestHeader("Prefer", prefer);
			if (SupabaseAuth.IsLoggedIn)
				req.SetRequestHeader("Authorization", "Bearer " + SupabaseAuth.AccessToken);
			var op = req.SendWebRequest();
			while (!op.isDone) await Task.Yield();
		}

		static async Task PatchJson(string url, string body)
		{
			byte[] bytes = Encoding.UTF8.GetBytes(body);
			using UnityWebRequest req = new(url, "PATCH");
			req.uploadHandler   = new UploadHandlerRaw(bytes);
			req.downloadHandler = new DownloadHandlerBuffer();
			req.SetRequestHeader("apikey", AnonKey);
			req.SetRequestHeader("Content-Type", "application/json");
			var op = req.SendWebRequest();
			while (!op.isDone) await Task.Yield();
		}

		// ---- Utilities ----

		static string UnwrapArray(string json)
		{
			json = json?.Trim() ?? "";
			if (json.StartsWith("[") && json.Length > 2)
				json = json.Substring(1, json.LastIndexOf(']') - 1).Trim();
			return json;
		}

		static List<T> ParseJsonArray<T>(string json)
		{
			// JsonUtility doesn't support top-level arrays; wrap it
			json = json?.Trim() ?? "[]";
			string wrapped = $"{{\"items\":{json}}}";
			JsonWrapper<T> wrapper = JsonUtility.FromJson<JsonWrapper<T>>(wrapped);
			return wrapper?.items ?? new List<T>();
		}

		static string FormatDate(string iso)
		{
			if (string.IsNullOrEmpty(iso)) return "";
			if (DateTime.TryParse(iso, out DateTime dt))
				return dt.ToString("MMM d, yyyy");
			return iso;
		}

		static string Escape(string s) => s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";

		[Serializable] class JsonWrapper<T> { public List<T> items; }
		[Serializable] class ShareInfoRaw      { public string owner_email; public string project_name; public string created_at; public int download_count; }
		[Serializable] class ShareHistoryEntryRaw { public string share_code; public string project_name; public string created_at; public int download_count; }
		[Serializable] class DownloadLogRaw    { public string share_code; public string downloaded_at; public ShareInfoRaw project_shares; }
	}
}
